using System;
using System.Collections.Generic;
using System.IO;
using GazeBoard.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Builds the calm room: a sunlit living room with three windows ahead of the user, a sofa,
    /// bookshelf, plants and a view outside. Everything is generated here (meshes, textures,
    /// materials, lights), then the lighting is baked into lightmaps, so in the headset the room
    /// costs a handful of unlit-priced draw calls and no realtime lights or shadows.
    ///
    /// Layout is in room space: the user sits at the origin facing +Z, the floor is y = 0.
    /// </summary>
    public static class CalmRoomBuilder
    {
        private const string EnvFolder = "Assets/GazeBoard/Env";
        private const string RoomRootName = "CalmRoom";
        private const string MeshAssetPath = EnvFolder + "/CalmRoomMeshes.asset";
        private const string LightingAssetPath = EnvFolder + "/CalmRoomLighting.lighting";
        private const string PreviewFolder = "Builds/RoomPreview";

        // Room shell.
        private const float X0 = -3.2f, X1 = 3.2f, Z0 = -3.0f, Z1 = 4.5f, H = 2.8f;
        private const float Wall = 0.2f;
        private const float SillY = 0.5f, HeadY = 2.5f;
        private static readonly float[] WindowCentres = { -1.85f, 0f, 1.85f };
        private const float WindowWidth = 1.5f;

        /// <summary>Metres covered by one repeat of a tiling texture.</summary>
        private const float TexMetres = 2f;

        // ---- menu ----

        [MenuItem("GazeBoard/Calm Room/Build and Bake Lighting")]
        private static void BuildAndBake()
        {
            if (Lightmapping.isRunning)
            {
                EditorUtility.DisplayDialog("GazeBoard", "A lighting bake is already running.", "OK");
                return;
            }

            GameObject room = Build();
            WireManager(room);
            room.SetActive(true); // only active objects are baked
            ApplyLightingSettings();

            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            if (!Lightmapping.BakeAsync())
            {
                Lightmapping.bakeCompleted -= OnBakeCompleted;
                Debug.LogError("[GazeBoard] The lighting bake could not start. Save the scene and try again.");
                return;
            }
            Debug.Log("[GazeBoard] Calm room built; baking its lighting (progress is at the bottom right of the editor). Preview images are written when it finishes.");
        }

        [MenuItem("GazeBoard/Calm Room/Capture Preview Images")]
        private static void CapturePreviewMenu()
        {
            GameObject room = FindRoom();
            if (room == null)
            {
                EditorUtility.DisplayDialog("GazeBoard", "No calm room in the scene. Run Build and Bake Lighting first.", "OK");
                return;
            }
            bool wasActive = room.activeSelf;
            room.SetActive(true);
            // Also the way to finish up by hand if a bake's own completion step did not run.
            if (LightmapSettings.lightmaps.Length > 0)
            {
                RoomLightmaps keeper = room.GetComponent<RoomLightmaps>();
                if (keeper == null) keeper = room.AddComponent<RoomLightmaps>();
                keeper.Capture();
                EditorUtility.SetDirty(keeper);
                EditorSceneManager.MarkSceneDirty(room.scene);
            }
            CapturePreview(room);
            room.SetActive(wasActive);
        }

        private static void OnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            GameObject room = FindRoom();
            if (room == null) return;

            RoomLightmaps keeper = room.GetComponent<RoomLightmaps>();
            if (keeper == null) keeper = room.AddComponent<RoomLightmaps>();
            keeper.Capture();
            EditorUtility.SetDirty(keeper);

            CapturePreview(room);
            room.SetActive(false); // passthrough is the default view
            EditorSceneManager.MarkSceneDirty(room.scene);
            Debug.Log($"[GazeBoard] Calm room lighting baked. Preview images are in {Path.GetFullPath(PreviewFolder)}. Save the scene (Ctrl+S).");
        }

        // ---- build ----

        /// <summary>Builds the room (replacing an earlier one) and returns its root, left inactive.</summary>
        public static GameObject Build()
        {
            Directory.CreateDirectory(EnvFolder);
            foreach (GameObject rootObject in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (rootObject.name == RoomRootName) Undo.DestroyObjectImmediate(rootObject);
            }

            var root = new GameObject(RoomRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create calm room");
            root.AddComponent<RoomLightmaps>();

            _groups = new Dictionary<string, Group>();
            _matrix = Matrix4x4.identity;
            _random = new System.Random(7);

            Palette p = CreatePalette();
            BuildShell(p);
            BuildWindows(p);
            BuildBookshelf(p);
            BuildSofaCorner(p);
            BuildLeftWall(p);
            BuildBackWall(p);
            BuildPlants(p);
            BuildOutside(p);
            EmitGroups(root);
            BuildLights(root);

            _groups = null;
            root.SetActive(false);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return root;
        }

        private sealed class Palette
        {
            public Material floor, wall, ceiling, trim, beam, wood, woodDark, rug, fabric, cushion, accent,
                linen, metal, terracotta, leafA, leafB, soil, ceramic, door, lampShade, deck, trunk,
                sand, shore, ocean, palmTrunk, palmLeaf, palmLeafDark, rock, rockTop, headland, red, brass,
                artA, artB, artC;
            public Material[] books;
        }

        private static Palette CreatePalette()
        {
            Texture2D planks = SaveTexture("CalmTexPlanks", PlankTexture(512), repeat: true);
            Texture2D plaster = SaveTexture("CalmTexPlaster", NoiseTexture(256, 0.035f, 18f), repeat: true);
            Texture2D grain = SaveTexture("CalmTexGrain", GrainTexture(256), repeat: true);
            Texture2D weave = SaveTexture("CalmTexWeave", WeaveTexture(128), repeat: true);
            Texture2D rug = SaveTexture("CalmTexRug", RugTexture(512), repeat: false);

            var p = new Palette
            {
                floor = Mat("CalmFloor", new Color(0.80f, 0.62f, 0.44f), planks),
                wall = Mat("CalmWall", new Color(0.93f, 0.90f, 0.85f), plaster),
                ceiling = Mat("CalmCeiling", new Color(0.96f, 0.95f, 0.93f), plaster),
                trim = Mat("CalmTrim", new Color(0.97f, 0.96f, 0.94f)),
                beam = Mat("CalmBeam", new Color(0.50f, 0.36f, 0.25f), grain),
                wood = Mat("CalmWood", new Color(0.74f, 0.57f, 0.40f), grain),
                woodDark = Mat("CalmWoodDark", new Color(0.36f, 0.26f, 0.19f), grain),
                rug = Mat("CalmRug", Color.white, rug),
                fabric = Mat("CalmFabric", new Color(0.55f, 0.63f, 0.62f), weave),
                cushion = Mat("CalmCushion", new Color(0.62f, 0.69f, 0.68f), weave),
                accent = Mat("CalmAccent", new Color(0.83f, 0.55f, 0.40f), weave),
                linen = Mat("CalmLinen", new Color(0.93f, 0.89f, 0.80f), weave),
                metal = Mat("CalmMetal", new Color(0.17f, 0.17f, 0.18f)),
                terracotta = Mat("CalmTerracotta", new Color(0.74f, 0.44f, 0.31f)),
                leafA = Mat("CalmLeafA", new Color(0.30f, 0.50f, 0.30f)),
                leafB = Mat("CalmLeafB", new Color(0.42f, 0.60f, 0.36f)),
                soil = Mat("CalmSoil", new Color(0.22f, 0.17f, 0.13f)),
                ceramic = Mat("CalmCeramic", new Color(0.90f, 0.87f, 0.80f)),
                door = Mat("CalmDoor", new Color(0.88f, 0.86f, 0.82f)),
                lampShade = Mat("CalmLampShade", new Color(1.00f, 0.90f, 0.72f), null, unlit: true),
                deck = Mat("CalmDeck", new Color(0.56f, 0.46f, 0.36f), grain),
                trunk = Mat("CalmTrunk", new Color(0.33f, 0.25f, 0.19f)),
                sand = Mat("CalmSand", new Color(0.93f, 0.84f, 0.66f), plaster),
                shore = Mat("CalmShore", Color.white, SaveTexture("CalmTexShore", ShoreTexture(1024, 128), repeat: false), unlit: true),
                ocean = Mat("CalmOcean", Color.white, SaveTexture("CalmTexOcean", OceanTexture(64, 1024), repeat: false), unlit: true),
                palmTrunk = Mat("CalmPalmTrunk", new Color(0.52f, 0.42f, 0.32f)),
                palmLeaf = Mat("CalmPalmLeaf", new Color(0.32f, 0.52f, 0.26f)),
                palmLeafDark = Mat("CalmPalmLeafDark", new Color(0.24f, 0.42f, 0.22f)),
                rock = Mat("CalmRock", new Color(0.70f, 0.63f, 0.55f)),
                rockTop = Mat("CalmRockTop", new Color(0.42f, 0.56f, 0.38f)),
                headland = Mat("CalmHeadland", new Color(0.62f, 0.71f, 0.78f), null, unlit: true),
                red = Mat("CalmRed", new Color(0.76f, 0.26f, 0.22f)),
                brass = Mat("CalmBrass", new Color(0.78f, 0.62f, 0.30f)),
                artA = Mat("CalmArtA", Color.white, SaveTexture("CalmTexArtA", ArtTexture(256, 320, 0), repeat: false)),
                artB = Mat("CalmArtB", Color.white, SaveTexture("CalmTexArtB", ArtTexture(256, 320, 1), repeat: false)),
                artC = Mat("CalmArtC", Color.white, SaveTexture("CalmTexArtC", ArtTexture(320, 256, 2), repeat: false)),
            };
            p.books = new[]
            {
                Mat("CalmBook1", new Color(0.72f, 0.36f, 0.28f)),
                Mat("CalmBook2", new Color(0.27f, 0.42f, 0.50f)),
                Mat("CalmBook3", new Color(0.86f, 0.74f, 0.50f)),
                Mat("CalmBook4", new Color(0.38f, 0.48f, 0.36f)),
                Mat("CalmBook5", new Color(0.88f, 0.86f, 0.80f)),
                Mat("CalmBook6", new Color(0.30f, 0.28f, 0.33f)),
            };
            return p;
        }

        private static void BuildShell(Palette p)
        {
            float midZ = (Z0 + Z1) * 0.5f, depth = Z1 - Z0, width = X1 - X0;

            Box(G("Floor", p.floor), new Vector3(0f, -0.1f, midZ), new Vector3(width + 2 * Wall, 0.2f, depth + 2 * Wall));
            Box(G("Ceiling", p.ceiling), new Vector3(0f, H + 0.1f, midZ), new Vector3(width + 2 * Wall, 0.2f, depth + 2 * Wall));

            Group walls = G("Walls", p.wall);
            Box(walls, new Vector3(0f, H / 2f, Z0 - Wall / 2f), new Vector3(width + 2 * Wall, H, Wall));
            Box(walls, new Vector3(X0 - Wall / 2f, H / 2f, midZ), new Vector3(Wall, H, depth));
            Box(walls, new Vector3(X1 + Wall / 2f, H / 2f, midZ), new Vector3(Wall, H, depth));

            // Window wall: a band under the sills, a band over the heads, and piers between.
            float wz = Z1 + Wall / 2f;
            Box(walls, new Vector3(0f, SillY / 2f, wz), new Vector3(width + 2 * Wall, SillY, Wall));
            Box(walls, new Vector3(0f, (HeadY + H) / 2f, wz), new Vector3(width + 2 * Wall, H - HeadY, Wall));
            float left = X0 - Wall;
            foreach (float c in WindowCentres)
            {
                float right = c - WindowWidth / 2f;
                Box(walls, new Vector3((left + right) / 2f, (SillY + HeadY) / 2f, wz), new Vector3(right - left, HeadY - SillY, Wall));
                left = c + WindowWidth / 2f;
            }
            Box(walls, new Vector3((left + X1 + Wall) / 2f, (SillY + HeadY) / 2f, wz), new Vector3(X1 + Wall - left, HeadY - SillY, Wall));

            // Skirting boards.
            Group trim = G("Trim", p.trim);
            Box(trim, new Vector3(X0 + 0.01f, 0.06f, midZ), new Vector3(0.02f, 0.12f, depth));
            Box(trim, new Vector3(X1 - 0.01f, 0.06f, midZ), new Vector3(0.02f, 0.12f, depth));
            Box(trim, new Vector3(0f, 0.06f, Z0 + 0.01f), new Vector3(width, 0.12f, 0.02f));
            Box(trim, new Vector3(0f, 0.06f, Z1 - 0.01f), new Vector3(width, 0.12f, 0.02f));

            // Ceiling beams across the room give the eye depth to read.
            Group beams = G("Beams", p.beam);
            foreach (float z in new[] { -1.9f, -0.1f, 1.7f, 3.5f })
            {
                Box(beams, new Vector3(0f, H - 0.09f, z), new Vector3(width, 0.18f, 0.14f));
            }

            // Rug under the user and the board.
            Disc(G("Rug", p.rug), new Vector3(0f, 0.012f, 0.9f), 1.65f);
        }

        private static void BuildWindows(Palette p)
        {
            Group trim = G("Trim", p.trim);
            float z = Z1 + 0.04f;
            float h = HeadY - SillY, midY = (SillY + HeadY) / 2f;
            foreach (float c in WindowCentres)
            {
                float half = WindowWidth / 2f;
                // Frame.
                Box(trim, new Vector3(c - half + 0.03f, midY, z), new Vector3(0.06f, h, 0.1f));
                Box(trim, new Vector3(c + half - 0.03f, midY, z), new Vector3(0.06f, h, 0.1f));
                Box(trim, new Vector3(c, HeadY - 0.03f, z), new Vector3(WindowWidth, 0.06f, 0.1f));
                Box(trim, new Vector3(c, SillY + 0.03f, z), new Vector3(WindowWidth, 0.06f, 0.1f));
                // Glazing bars: these are what draw the pattern of light on the floor.
                Box(trim, new Vector3(c, midY, z), new Vector3(0.04f, h, 0.05f));
                Box(trim, new Vector3(c, SillY + h * 0.62f, z), new Vector3(WindowWidth, 0.04f, 0.05f));
                // Sill.
                Box(trim, new Vector3(c, SillY + 0.02f, Z1 - 0.07f), new Vector3(WindowWidth + 0.12f, 0.04f, 0.24f));
            }

            // Curtains drawn back to both ends of the wall, as rows of soft folds.
            Group linen = G("Curtains", p.linen);
            for (int i = 0; i < 7; i++)
            {
                float offset = 0.07f + i * 0.072f;
                float zFold = Z1 - 0.13f - (i % 2) * 0.035f;
                Cyl(linen, new Vector3(X0 + offset, 1.3f, zFold), 0.095f, 2.5f);
                Cyl(linen, new Vector3(X1 - offset, 1.3f, zFold), 0.095f, 2.5f);
            }
            Cyl(G("Metal", p.metal), new Vector3(0f, 2.58f, Z1 - 0.14f), 0.025f, X1 - X0 - 0.1f, new Vector3(0f, 0f, 90f));

            // Small pots on the two outer sills.
            SillPlant(p, new Vector3(-2.25f, SillY + 0.04f, Z1 - 0.08f), 0.9f);
            SillPlant(p, new Vector3(-1.5f, SillY + 0.04f, Z1 - 0.08f), 1.15f);
            SillPlant(p, new Vector3(2.2f, SillY + 0.04f, Z1 - 0.08f), 1.1f);
        }

        private static void SillPlant(Palette p, Vector3 at, float scale)
        {
            Cyl(G("Ceramic", p.ceramic), at + new Vector3(0f, 0.06f * scale, 0f), 0.14f * scale, 0.12f * scale);
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = new Vector3(Range(-0.07f, 0.07f), Range(0.14f, 0.26f), Range(-0.05f, 0.05f)) * scale;
                Vector3 size = new Vector3(Range(0.09f, 0.15f), Range(0.10f, 0.18f), Range(0.09f, 0.15f)) * scale;
                Ball(G("Leaves", i % 2 == 0 ? p.leafA : p.leafB), at + offset, size, RandomEuler());
            }
        }

        private static void BuildBookshelf(Palette p)
        {
            // Against the left wall, ahead and to the left of the user.
            const float zA = 1.3f, zB = 3.1f, depth = 0.34f, height = 2.1f;
            float x = X0 + depth / 2f, zMid = (zA + zB) / 2f, length = zB - zA;
            Group wood = G("Furniture", p.wood);

            Box(wood, new Vector3(x, height / 2f, zA + 0.015f), new Vector3(depth, height, 0.03f));
            Box(wood, new Vector3(x, height / 2f, zB - 0.015f), new Vector3(depth, height, 0.03f));
            Box(wood, new Vector3(X0 + 0.012f, height / 2f, zMid), new Vector3(0.02f, height, length));
            float[] shelves = { 0.08f, 0.50f, 0.92f, 1.34f, 1.76f };
            foreach (float y in shelves) Box(wood, new Vector3(x, y, zMid), new Vector3(depth, 0.03f, length - 0.06f));
            Box(wood, new Vector3(x, height - 0.015f, zMid), new Vector3(depth, 0.03f, length));

            int souvenir = 0;
            foreach (float shelf in shelves)
            {
                float top = shelf + 0.015f;
                float z = zA + 0.05f;
                // One gap is guaranteed part-way along each shelf, so every shelf shows something.
                float sureGap = Range(zA + 0.25f, zB - 0.6f);
                bool hadGap = false;
                while (z < zB - 0.12f)
                {
                    bool due = !hadGap && z >= sureGap;
                    if (due || _random.NextDouble() < 0.04)
                    {
                        // A gap with something in it: a souvenir, or a few books lying flat.
                        float gap = Range(0.22f, 0.32f);
                        if (z + gap > zB - 0.06f) break;
                        if (_random.NextDouble() < 0.72)
                        {
                            Souvenir(p, souvenir++, new Vector3(x + 0.02f, top, z + gap / 2f));
                        }
                        else
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                Box(G("Books", Pick(p.books)), new Vector3(x + 0.02f, top + 0.02f + i * 0.04f, z + gap / 2f),
                                    new Vector3(0.2f, 0.038f, gap - 0.06f - i * 0.02f), new Vector3(0f, Range(-6f, 6f), 0f));
                            }
                        }
                        z += gap;
                        hadGap = true;
                        continue;
                    }

                    float thickness = Range(0.028f, 0.058f);
                    float tall = Range(0.21f, 0.33f);
                    float deep = Range(0.19f, 0.25f);
                    Box(G("Books", Pick(p.books)), new Vector3(X0 + 0.05f + deep / 2f, top + tall / 2f, z + thickness / 2f), new Vector3(deep, tall, thickness));
                    z += thickness + 0.002f;
                }
            }
        }

        /// <summary>
        /// A small travel keepsake standing on a shelf. at is the point on the shelf under its
        /// middle; the shelf faces +X, so "front" is +X and things are laid out along Z.
        /// </summary>
        private static void Souvenir(Palette p, int index, Vector3 at)
        {
            Group white = G("Souvenirs", p.ceramic);
            Group red = G("Souvenirs", p.red);
            Group dark = G("Souvenirs", p.metal);
            Group wood = G("Souvenirs", p.woodDark);
            Group brass = G("Souvenirs", p.brass);
            Group teal = G("Souvenirs", p.books[1]);
            Group sand = G("Souvenirs", p.books[2]);

            switch (index % 7)
            {
                case 0: // lighthouse
                    Cyl(dark, at + new Vector3(0f, 0.012f, 0f), 0.10f, 0.024f);
                    Cyl(white, at + new Vector3(0f, 0.075f, 0f), 0.064f, 0.10f);
                    Cyl(red, at + new Vector3(0f, 0.14f, 0f), 0.058f, 0.035f);
                    Cyl(white, at + new Vector3(0f, 0.175f, 0f), 0.052f, 0.035f);
                    Cyl(dark, at + new Vector3(0f, 0.20f, 0f), 0.074f, 0.012f);
                    Cyl(brass, at + new Vector3(0f, 0.225f, 0f), 0.04f, 0.04f);
                    Ball(red, at + new Vector3(0f, 0.255f, 0f), new Vector3(0.056f, 0.04f, 0.056f));
                    break;
                case 1: // sailing boat on a stand
                    Box(wood, at + new Vector3(0f, 0.012f, 0f), new Vector3(0.05f, 0.024f, 0.12f));
                    Box(G("Souvenirs", p.wood), at + new Vector3(0f, 0.045f, 0f), new Vector3(0.05f, 0.035f, 0.19f));
                    Cyl(dark, at + new Vector3(0f, 0.15f, 0.01f), 0.007f, 0.18f);
                    Box(white, at + new Vector3(0f, 0.15f, -0.04f), new Vector3(0.004f, 0.14f, 0.085f), new Vector3(-9f, 0f, 0f));
                    Box(white, at + new Vector3(0f, 0.13f, 0.055f), new Vector3(0.004f, 0.10f, 0.06f), new Vector3(12f, 0f, 0f));
                    break;
                case 2: // globe
                    Cyl(wood, at + new Vector3(0f, 0.01f, 0f), 0.09f, 0.02f);
                    Cyl(brass, at + new Vector3(0f, 0.04f, 0f), 0.012f, 0.05f);
                    Ball(teal, at + new Vector3(0f, 0.125f, 0f), new Vector3(0.13f, 0.13f, 0.13f));
                    Cyl(brass, at + new Vector3(0f, 0.125f, 0f), 0.006f, 0.17f, new Vector3(0f, 0f, 23f));
                    break;
                case 3: // stepped pyramid
                    for (int i = 0; i < 5; i++)
                    {
                        float side = 0.15f - i * 0.028f;
                        Box(sand, at + new Vector3(0f, 0.012f + i * 0.024f, 0f), new Vector3(side, 0.024f, side));
                    }
                    break;
                case 4: // framed photograph, leaning back
                    Box(wood, at + new Vector3(-0.01f, 0.095f, 0f), new Vector3(0.014f, 0.19f, 0.15f), new Vector3(0f, 0f, 12f));
                    Box(G("Art", p.artA), at + new Vector3(-0.002f, 0.096f, 0f), new Vector3(0.006f, 0.15f, 0.11f), new Vector3(0f, 0f, 12f), fitUv: true);
                    break;
                case 5: // old camera
                    Box(dark, at + new Vector3(0f, 0.045f, 0f), new Vector3(0.06f, 0.09f, 0.15f));
                    Box(brass, at + new Vector3(0f, 0.097f, 0f), new Vector3(0.062f, 0.014f, 0.152f));
                    Cyl(dark, at + new Vector3(0.045f, 0.045f, 0f), 0.062f, 0.04f, new Vector3(0f, 0f, 90f));
                    Cyl(brass, at + new Vector3(0.067f, 0.045f, 0f), 0.05f, 0.006f, new Vector3(0f, 0f, 90f));
                    Cyl(brass, at + new Vector3(0f, 0.112f, 0.045f), 0.022f, 0.016f);
                    break;
                default: // nesting dolls, two sizes
                    for (int i = 0; i < 2; i++)
                    {
                        float s = i == 0 ? 1f : 0.68f;
                        Vector3 foot = at + new Vector3(0f, 0f, i == 0 ? -0.045f : 0.055f);
                        Ball(red, foot + new Vector3(0f, 0.055f * s, 0f), new Vector3(0.085f, 0.11f, 0.085f) * s);
                        Ball(white, foot + new Vector3(0.012f * s, 0.125f * s, 0f), new Vector3(0.06f, 0.06f, 0.06f) * s);
                        Ball(red, foot + new Vector3(-0.006f * s, 0.132f * s, 0f), new Vector3(0.064f, 0.062f, 0.066f) * s);
                    }
                    break;
            }
        }

        private static void BuildSofaCorner(Palette p)
        {
            // A two-seat sofa along the right wall, facing into the room.
            Matrix4x4 saved = _matrix;
            _matrix = Matrix4x4.TRS(new Vector3(X1 - 0.52f, 0f, 1.45f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
            Group fabric = G("Sofa", p.fabric);
            Group cushion = G("Sofa", p.cushion);
            Group accent = G("Sofa", p.accent);
            Box(fabric, new Vector3(0f, 0.25f, 0f), new Vector3(1.96f, 0.26f, 0.92f));
            Box(fabric, new Vector3(0f, 0.62f, -0.37f), new Vector3(1.96f, 0.58f, 0.18f), new Vector3(-6f, 0f, 0f));
            Box(fabric, new Vector3(-0.89f, 0.47f, 0f), new Vector3(0.18f, 0.36f, 0.92f));
            Box(fabric, new Vector3(0.89f, 0.47f, 0f), new Vector3(0.18f, 0.36f, 0.92f));
            Box(cushion, new Vector3(-0.4f, 0.45f, 0.06f), new Vector3(0.78f, 0.14f, 0.70f));
            Box(cushion, new Vector3(0.4f, 0.45f, 0.06f), new Vector3(0.78f, 0.14f, 0.70f));
            Box(cushion, new Vector3(-0.4f, 0.72f, -0.22f), new Vector3(0.74f, 0.40f, 0.15f), new Vector3(-12f, 0f, 0f));
            Box(cushion, new Vector3(0.4f, 0.72f, -0.22f), new Vector3(0.74f, 0.40f, 0.15f), new Vector3(-12f, 0f, 0f));
            Box(accent, new Vector3(-0.62f, 0.68f, -0.06f), new Vector3(0.38f, 0.36f, 0.12f), new Vector3(-20f, 18f, 6f));
            Box(G("Sofa", p.linen), new Vector3(0.6f, 0.66f, -0.05f), new Vector3(0.36f, 0.34f, 0.12f), new Vector3(-22f, -14f, -5f));
            Group legs = G("Furniture", p.woodDark);
            foreach (float lx in new[] { -0.9f, 0.9f })
            foreach (float lz in new[] { -0.38f, 0.38f })
                Cyl(legs, new Vector3(lx, 0.06f, lz), 0.05f, 0.12f);
            _matrix = saved;

            // Low round table in front of it, with a mug and two books.
            Vector3 table = new Vector3(1.55f, 0f, 1.5f);
            Group wood = G("Furniture", p.wood);
            Cyl(wood, table + new Vector3(0f, 0.40f, 0f), 0.78f, 0.04f);
            Cyl(G("Metal", p.metal), table + new Vector3(0f, 0.20f, 0f), 0.07f, 0.38f);
            Cyl(G("Metal", p.metal), table + new Vector3(0f, 0.012f, 0f), 0.42f, 0.024f);
            Box(G("Books", p.books[1]), table + new Vector3(-0.12f, 0.44f, 0.08f), new Vector3(0.22f, 0.035f, 0.30f), new Vector3(0f, 22f, 0f));
            Box(G("Books", p.books[2]), table + new Vector3(-0.12f, 0.475f, 0.08f), new Vector3(0.20f, 0.03f, 0.27f), new Vector3(0f, 9f, 0f));
            Cyl(G("Ceramic", p.ceramic), table + new Vector3(0.16f, 0.465f, -0.1f), 0.085f, 0.09f);

            // Floor lamp beyond the sofa. Its glow is a baked light, added with the other lights.
            Vector3 lamp = LampPosition;
            Cyl(G("Metal", p.metal), lamp + new Vector3(0f, 0.015f, 0f), 0.30f, 0.03f);
            Cyl(G("Metal", p.metal), lamp + new Vector3(0f, 0.75f, 0f), 0.028f, 1.5f);
            Cyl(G("Lamp", p.lampShade, gi: false), lamp + new Vector3(0f, 1.62f, 0f), 0.40f, 0.30f);

            // Two prints above the sofa.
            Picture(p, p.artA, new Vector3(X1, 1.72f, 1.0f), 0.50f, 0.64f, onRightWall: true);
            Picture(p, p.artB, new Vector3(X1, 1.72f, 1.9f), 0.50f, 0.64f, onRightWall: true);
        }

        private static readonly Vector3 LampPosition = new Vector3(X1 - 0.42f, 0f, 3.1f);

        private static void BuildLeftWall(Palette p)
        {
            // Beside the user: a bench under a wide print.
            Group wood = G("Furniture", p.wood);
            Vector3 bench = new Vector3(X0 + 0.24f, 0f, -0.2f);
            Box(wood, bench + new Vector3(0f, 0.42f, 0f), new Vector3(0.40f, 0.05f, 1.5f));
            foreach (float z in new[] { -0.68f, 0.68f })
                Box(wood, bench + new Vector3(0f, 0.2f, z), new Vector3(0.36f, 0.4f, 0.05f));
            Box(G("Sofa", p.accent), bench + new Vector3(0f, 0.48f, -0.35f), new Vector3(0.36f, 0.07f, 0.55f));
            Cyl(G("Basket", p.linen), bench + new Vector3(0.02f, 0.16f, 0.25f), 0.32f, 0.30f);
            Ball(G("Ceramic", p.ceramic), bench + new Vector3(0f, 0.58f, 0.45f), new Vector3(0.18f, 0.26f, 0.18f));
            Picture(p, p.artC, new Vector3(X0, 1.6f, -0.2f), 1.1f, 0.88f, onRightWall: false);
        }

        private static void BuildBackWall(Palette p)
        {
            // Behind the user: a door and a sideboard, so the room is whole when they look round.
            Group trim = G("Trim", p.trim);
            float z = Z0 + 0.02f;
            Box(G("Door", p.door), new Vector3(1.7f, 1.02f, z), new Vector3(0.86f, 2.04f, 0.04f));
            Box(trim, new Vector3(1.7f - 0.48f, 1.05f, z), new Vector3(0.1f, 2.1f, 0.06f));
            Box(trim, new Vector3(1.7f + 0.48f, 1.05f, z), new Vector3(0.1f, 2.1f, 0.06f));
            Box(trim, new Vector3(1.7f, 2.13f, z), new Vector3(1.06f, 0.1f, 0.06f));
            Ball(G("Metal", p.metal), new Vector3(1.38f, 1.0f, Z0 + 0.07f), new Vector3(0.06f, 0.06f, 0.06f));

            Group wood = G("Furniture", p.wood);
            Vector3 board = new Vector3(-1.1f, 0f, Z0 + 0.24f);
            Box(wood, board + new Vector3(0f, 0.46f, 0f), new Vector3(1.7f, 0.56f, 0.42f));
            foreach (float x in new[] { -0.78f, 0.78f })
                Box(G("Furniture", p.woodDark), board + new Vector3(x, 0.09f, 0f), new Vector3(0.05f, 0.18f, 0.36f));
            Cyl(G("Ceramic", p.ceramic), board + new Vector3(-0.5f, 0.86f, 0f), 0.16f, 0.24f);
            Box(G("Books", p.books[3]), board + new Vector3(0.35f, 0.76f, 0f), new Vector3(0.3f, 0.04f, 0.22f), new Vector3(0f, 12f, 0f));
            Picture(p, p.artB, new Vector3(-1.1f, 1.7f, Z0), 0.8f, 1.0f, onRightWall: false, onBackWall: true);
        }

        /// <summary>A framed print flat against a wall. at is the centre on the wall surface.</summary>
        private static void Picture(Palette p, Material art, Vector3 at, float width, float height, bool onRightWall, bool onBackWall = false)
        {
            const float frame = 0.035f;
            Vector3 frameSize, artSize, outward;
            if (onBackWall)
            {
                outward = Vector3.forward;
                frameSize = new Vector3(width + 2 * frame, height + 2 * frame, 0.03f);
                artSize = new Vector3(width, height, 0.012f);
            }
            else
            {
                outward = onRightWall ? Vector3.left : Vector3.right;
                frameSize = new Vector3(0.03f, height + 2 * frame, width + 2 * frame);
                artSize = new Vector3(0.012f, height, width);
            }
            Box(G("Frames", p.woodDark), at + outward * 0.015f, frameSize);
            Box(G("Art", art), at + outward * 0.028f, artSize, default, fitUv: true);
        }

        private static void BuildPlants(Palette p)
        {
            FloorPlant(p, new Vector3(X0 + 0.55f, 0f, Z1 - 0.6f), 1.25f);
            FloorPlant(p, new Vector3(X1 - 0.5f, 0f, Z1 - 0.55f), 0.95f);
            FloorPlant(p, new Vector3(X0 + 0.5f, 0f, Z0 + 0.5f), 1.05f);
        }

        private static void FloorPlant(Palette p, Vector3 at, float scale)
        {
            Cyl(G("Pots", p.terracotta), at + new Vector3(0f, 0.19f * scale, 0f), 0.44f * scale, 0.38f * scale);
            Cyl(G("Pots", p.soil), at + new Vector3(0f, 0.385f * scale, 0f), 0.38f * scale, 0.01f);
            Cyl(G("Pots", p.trunk), at + new Vector3(0f, 0.7f * scale, 0f), 0.05f * scale, 0.65f * scale, new Vector3(Range(-5f, 5f), 0f, Range(-5f, 5f)));
            Vector3 crown = at + new Vector3(0f, 1.15f * scale, 0f);
            for (int i = 0; i < 11; i++)
            {
                Vector3 offset = new Vector3(Range(-0.3f, 0.3f), Range(-0.2f, 0.42f), Range(-0.3f, 0.3f)) * scale;
                Vector3 size = new Vector3(Range(0.3f, 0.5f), Range(0.22f, 0.36f), Range(0.3f, 0.5f)) * scale;
                Ball(G("Leaves", i % 2 == 0 ? p.leafA : p.leafB), crown + offset, size, RandomEuler());
            }
        }

        private static void BuildOutside(Palette p)
        {
            // A beach house: a short deck, sand running down to the sea, a few palms, and a cliff
            // with a sea stack off to the right. Texels are spent sparingly out here.
            const float sandY = -0.3f, seaY = -1.0f, slopeFrom = 18f, shoreZ = 24f;

            Box(G("Outside", p.deck, lightmapScale: 0.25f), new Vector3(0f, -0.12f, 5.9f), new Vector3(9f, 0.2f, 2.4f));

            // Sand: level near the house, then sloping to the waterline.
            Group sand = G("Beach", p.sand, lightmapScale: 0.2f);
            Box(sand, new Vector3(0f, sandY - 0.1f, (Z1 + slopeFrom) / 2f), new Vector3(80f, 0.2f, slopeFrom - Z1));
            float run = shoreZ + 1.5f - slopeFrom, drop = sandY - seaY + 0.15f;
            float slopeLength = Mathf.Sqrt(run * run + drop * drop);
            Box(sand, new Vector3(0f, sandY - drop / 2f - 0.1f, slopeFrom + run / 2f), new Vector3(80f, 0.2f, slopeLength),
                new Vector3(Mathf.Atan2(drop, run) * Mathf.Rad2Deg, 0f, 0f));

            // The sea is plain colour (a gradient from the shallows out), with a strip of wet
            // sand and foam where it meets the beach. Neither takes part in the bake.
            Box(G("Sea", p.ocean, gi: false), new Vector3(0f, seaY - 0.05f, shoreZ + 400f), new Vector3(1600f, 0.1f, 800f), default, fitUv: true);
            Box(G("Sea", p.shore, gi: false), new Vector3(0f, seaY - 0.03f, shoreZ + 0.4f), new Vector3(160f, 0.1f, 3.2f), default, fitUv: true);

            Palm(p, new Vector3(-5.4f, sandY, 11.5f), 6.4f, 9f, -70f);
            Palm(p, new Vector3(-2.3f, sandY, 15.5f), 5.2f, 6f, 30f);
            Palm(p, new Vector3(4.6f, sandY, 12.6f), 5.9f, 8f, 110f);
            Palm(p, new Vector3(8.2f, sandY, 17.5f), 7.0f, 10f, 160f);
            // A few rocks on the sand break up the flat.
            Group rock = G("Cliff", p.rock, lightmapScale: 0.25f);
            Ball(rock, new Vector3(-7.5f, sandY + 0.1f, 16.5f), new Vector3(1.4f, 0.8f, 1.1f), new Vector3(0f, 30f, 8f));
            Ball(rock, new Vector3(-6.6f, sandY + 0.05f, 17.3f), new Vector3(0.7f, 0.45f, 0.6f), new Vector3(10f, 70f, 0f));
            Ball(rock, new Vector3(6.3f, sandY + 0.05f, 20.5f), new Vector3(1.0f, 0.5f, 0.8f), new Vector3(0f, 110f, 5f));

            // Cliff: blocks stepping down from a headland to a lone stack in the water. It is
            // baked so its faces catch the sun differently, at a fraction of a texel per metre.
            Group cliff = G("Cliff", p.rock, lightmapScale: 0.02f);
            Group top = G("Cliff", p.rockTop, lightmapScale: 0.02f);
            CliffBlock(cliff, top, new Vector3(64f, seaY, 132f), new Vector3(62f, 40f, 46f), 14f);
            CliffBlock(cliff, top, new Vector3(36f, seaY, 120f), new Vector3(30f, 30f, 30f), -18f);
            CliffBlock(cliff, top, new Vector3(20f, seaY, 111f), new Vector3(16f, 17f, 18f), 26f);
            CliffBlock(cliff, null, new Vector3(6f, seaY, 104f), new Vector3(6f, 13f, 6f), 12f);
            CliffBlock(cliff, null, new Vector3(-1f, seaY, 101f), new Vector3(3f, 5f, 3.4f), -30f);

            // A far headland on the other side, already lost in haze.
            Ball(G("Far", p.headland, gi: false), new Vector3(-260f, seaY - 6f, 520f), new Vector3(420f, 50f, 80f));
        }

        /// <summary>One block of cliff rising from the sea, leaning slightly, with an optional green cap.</summary>
        private static void CliffBlock(Group rock, Group cap, Vector3 foot, Vector3 size, float yaw)
        {
            var euler = new Vector3(Range(-3f, 3f), yaw, Range(-4f, 4f));
            // Sunk a little, so the lean never lifts an edge clear of the water.
            Box(rock, foot + new Vector3(0f, size.y / 2f - 3f, 0f), new Vector3(size.x, size.y + 6f, size.z), euler);
            if (cap != null)
            {
                Ball(cap, foot + new Vector3(0f, size.y, 0f), new Vector3(size.x * 0.95f, size.y * 0.16f, size.z * 0.95f), new Vector3(0f, yaw, 0f));
            }
        }

        /// <summary>A coconut palm: a trunk that leans and curves, and a crown of arching fronds.</summary>
        private static void Palm(Palette p, Vector3 foot, float height, float lean, float leanYaw)
        {
            Group trunk = G("Palms", p.palmTrunk, lightmapScale: 0.3f);
            const int segments = 6;
            float segment = height / segments;
            Quaternion heading = Quaternion.Euler(0f, leanYaw, 0f);
            Vector3 at = foot;
            for (int i = 0; i < segments; i++)
            {
                // The lean eases off toward the top, which reads as a trunk curving back upright.
                float tilt = lean * (1.6f - 1.2f * i / (segments - 1f));
                Quaternion rotation = heading * Quaternion.Euler(tilt, 0f, 0f);
                Vector3 up = rotation * Vector3.up;
                float thickness = Mathf.Lerp(0.30f, 0.18f, i / (segments - 1f));
                Cyl(trunk, at + up * (segment / 2f), thickness, segment * 1.06f, rotation.eulerAngles);
                at += up * segment;
            }

            const int fronds = 10;
            for (int i = 0; i < fronds; i++)
            {
                Group leaf = G("Palms", i % 2 == 0 ? p.palmLeaf : p.palmLeafDark, lightmapScale: 0.3f);
                float yaw = i * 360f / fronds + Range(-10f, 10f);
                float rise = Range(8f, 30f);
                float length = Range(1.5f, 1.9f);
                // Two blades per frond: the first rises from the crown, the second droops from its tip.
                Quaternion inner = Quaternion.Euler(-rise, yaw, 0f);
                Vector3 tip = at + inner * Vector3.forward * length;
                Ball(leaf, at + inner * Vector3.forward * (length / 2f), new Vector3(0.62f, 0.05f, length * 1.1f), inner.eulerAngles);
                Quaternion outer = Quaternion.Euler(Range(28f, 48f), yaw, 0f);
                Ball(leaf, tip + outer * Vector3.forward * (length * 0.45f), new Vector3(0.5f, 0.05f, length), outer.eulerAngles);
            }
            Group nuts = G("Palms", p.trunk, lightmapScale: 0.3f);
            Ball(nuts, at + new Vector3(0.14f, -0.16f, 0.05f), new Vector3(0.24f, 0.26f, 0.24f));
            Ball(nuts, at + new Vector3(-0.1f, -0.2f, 0.12f), new Vector3(0.22f, 0.24f, 0.22f));
            Ball(nuts, at + new Vector3(0f, -0.18f, -0.15f), new Vector3(0.22f, 0.24f, 0.22f));
        }

        private static void BuildLights(GameObject root)
        {
            // Afternoon sun from outside, ahead and to the right, low enough to reach deep into the room.
            Light sun = NewLight(root, "Sun", LightType.Directional, new Color(1.00f, 0.92f, 0.80f), 2.4f);
            sun.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.30f, -0.52f, -0.80f));
            sun.shadows = LightShadows.Soft;
            sun.shadowAngle = 1.6f;

            // Broad, faint light from the ceiling: stands in for the many bounces a camera's
            // exposure would pick up, and gives everything a soft contact shadow.
            Light fill = NewLight(root, "Ceiling Fill", LightType.Rectangle, new Color(1.00f, 0.97f, 0.93f), 0.9f);
            fill.transform.localPosition = new Vector3(0f, H - 0.25f, 0.6f);
            fill.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            fill.areaSize = new Vector2(4.5f, 5f);
            fill.shadows = LightShadows.Soft;

            Light lamp = NewLight(root, "Lamp Glow", LightType.Point, new Color(1.00f, 0.80f, 0.55f), 2.2f);
            lamp.transform.localPosition = LampPosition + new Vector3(0f, 1.5f, 0f);
            lamp.range = 4.5f;
            lamp.shadows = LightShadows.Soft;
            lamp.shadowRadius = 0.12f;

            RenderSettings.sun = sun;
        }

        private static Light NewLight(GameObject root, string name, LightType type, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            Light light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.lightmapBakeType = LightmapBakeType.Baked;
            return light;
        }

        // ---- lighting and manager ----

        private static void ApplyLightingSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingAssetPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "CalmRoomLighting" };
                AssetDatabase.CreateAsset(settings, LightingAssetPath);
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.lightmapResolution = 40f;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 4;
            settings.lightmapCompression = LightmapCompression.HighQuality;
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.aoExponentIndirect = 1.2f;
            settings.aoExponentDirect = 0.25f;
            settings.directSampleCount = 48;
            settings.indirectSampleCount = 384;
            settings.environmentSampleCount = 384;
            settings.maxBounces = 4;
            settings.indirectScale = 1.35f;
            settings.albedoBoost = 1.6f;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            settings.denoiserTypeDirect = LightingSettings.DenoiserType.OpenImage;
            settings.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage;
            settings.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            Material skybox = CreateSkybox();
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.25f;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>The sky seen through the windows, and the source of the room's daylight.</summary>
        public static Material CreateSkybox()
        {
            Directory.CreateDirectory(EnvFolder);
            string path = EnvFolder + "/CalmSkybox.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_SunSize", 0.03f);
            mat.SetFloat("_SunSizeConvergence", 6f);
            mat.SetFloat("_AtmosphereThickness", 0.75f);
            mat.SetColor("_SkyTint", new Color(0.52f, 0.60f, 0.68f));
            // Below the horizon the sky takes the colour of the far sea, so the two meet without a seam.
            mat.SetColor("_GroundColor", new Color(0.30f, 0.52f, 0.68f));
            mat.SetFloat("_Exposure", 1.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void WireManager(GameObject room)
        {
            EnvironmentManager manager = Object.FindFirstObjectByType<EnvironmentManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogWarning("[GazeBoard] No EnvironmentManager in the scene; run GazeBoard > Add Environment so the View tile can switch to the room.");
                return;
            }
            var so = new SerializedObject(manager);
            so.FindProperty("_roomRoot").objectReferenceValue = room;
            so.FindProperty("_roomSkybox").objectReferenceValue = CreateSkybox();
            so.ApplyModifiedProperties();
        }

        private static GameObject FindRoom()
        {
            foreach (GameObject rootObject in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (rootObject.name == RoomRootName) return rootObject;
            }
            return null;
        }

        // ---- preview ----

        private static void CapturePreview(GameObject room)
        {
            Directory.CreateDirectory(PreviewFolder);

            // The board hides the middle of the room; show the room alone, then once with it.
            GameObject board = null;
            foreach (GameObject rootObject in room.scene.GetRootGameObjects())
            {
                if (rootObject.name == "Board") board = rootObject;
            }
            bool boardWasActive = board != null && board.activeSelf;

            var cameraObject = new GameObject("RoomPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 600f;
            camera.fieldOfView = 70f;
            camera.enabled = false;

            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            Material previousSkybox = RenderSettings.skybox;
            RenderSettings.skybox = CreateSkybox();

            var eye = new Vector3(0f, 1.2f, 0f);
            var views = new (string name, Vector3 position, Quaternion rotation, bool withBoard)[]
            {
                ("1_front", eye, Quaternion.identity, false),
                ("2_front_with_board", eye, Quaternion.identity, true),
                ("3_left", eye, Quaternion.Euler(0f, -65f, 0f), false),
                ("4_right", eye, Quaternion.Euler(0f, 65f, 0f), false),
                ("5_back", eye, Quaternion.Euler(0f, 180f, 0f), false),
                ("6_overview", new Vector3(-2.7f, 2.35f, -2.6f), Quaternion.LookRotation(new Vector3(0.6f, 0.9f, 2.2f) - new Vector3(-2.7f, 2.35f, -2.6f)), false),
            };

            try
            {
                foreach (var view in views)
                {
                    if (board != null) board.SetActive(view.withBoard && boardWasActive);
                    camera.transform.SetPositionAndRotation(room.transform.TransformPoint(view.position), room.transform.rotation * view.rotation);
                    camera.targetTexture = target;

                    var request = new RenderPipeline.StandardRequest { destination = target };
                    if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                    else camera.Render();

                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(PreviewFolder, view.name + ".png"), image.EncodeToPNG());
                }
            }
            finally
            {
                if (board != null) board.SetActive(boardWasActive);
                RenderSettings.skybox = previousSkybox;
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
            Debug.Log($"[GazeBoard] Room preview images written to {Path.GetFullPath(PreviewFolder)}.");
        }

        // ---- geometry collection ----

        private sealed class Group
        {
            public string name;
            public Material material;
            public float lightmapScale = 1f;
            public bool gi = true;
            public readonly List<CombineInstance> parts = new List<CombineInstance>();
        }

        private static Dictionary<string, Group> _groups;
        private static Matrix4x4 _matrix = Matrix4x4.identity;
        private static System.Random _random;

        /// <summary>
        /// Parts that share a name and a material become one mesh and one draw call. gi false
        /// leaves the group out of the bake (unlit backdrop and anything that glows).
        /// </summary>
        private static Group G(string name, Material material, float lightmapScale = 1f, bool gi = true)
        {
            string key = name + "/" + material.name;
            if (!_groups.TryGetValue(key, out Group group))
            {
                group = new Group { name = $"{name} ({material.name})", material = material, lightmapScale = lightmapScale, gi = gi };
                _groups.Add(key, group);
            }
            return group;
        }

        private static void Box(Group group, Vector3 centre, Vector3 size, Vector3 euler = default, bool fitUv = false)
        {
            group.parts.Add(new CombineInstance
            {
                mesh = BoxMesh(size, fitUv),
                transform = _matrix * Matrix4x4.TRS(centre, Quaternion.Euler(euler), Vector3.one),
            });
        }

        private static void Cyl(Group group, Vector3 centre, float diameter, float height, Vector3 euler = default)
        {
            group.parts.Add(new CombineInstance
            {
                mesh = Builtin("Cylinder.fbx"), // 1 m across, 2 m tall
                transform = _matrix * Matrix4x4.TRS(centre, Quaternion.Euler(euler), new Vector3(diameter, height / 2f, diameter)),
            });
        }

        private static void Ball(Group group, Vector3 centre, Vector3 size, Vector3 euler = default)
        {
            group.parts.Add(new CombineInstance
            {
                mesh = Builtin("Sphere.fbx"), // 1 m across
                transform = _matrix * Matrix4x4.TRS(centre, Quaternion.Euler(euler), size),
            });
        }

        private static void Disc(Group group, Vector3 centre, float radius)
        {
            group.parts.Add(new CombineInstance
            {
                mesh = DiscMesh(radius, 64),
                transform = _matrix * Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one),
            });
        }

        private static Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name);

        /// <summary>One mesh, one renderer per group; lightmap UVs unwrapped for the merged shape.</summary>
        private static void EmitGroups(GameObject root)
        {
            AssetDatabase.DeleteAsset(MeshAssetPath);
            bool first = true;
            var staticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;

            foreach (Group group in _groups.Values)
            {
                if (group.parts.Count == 0) continue;

                long vertices = 0;
                foreach (CombineInstance part in group.parts) vertices += part.mesh.vertexCount;

                var mesh = new Mesh { name = group.name };
                if (vertices > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(group.parts.ToArray(), mergeSubMeshes: true, useMatrices: true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (group.gi)
                {
                    UnwrapParam.SetDefaults(out UnwrapParam unwrap);
                    unwrap.packMargin = 0.008f;
                    Unwrapping.GenerateSecondaryUVSet(mesh, unwrap);
                }

                if (first) AssetDatabase.CreateAsset(mesh, MeshAssetPath);
                else AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
                first = false;

                var go = new GameObject(group.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = group.material;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (group.gi)
                {
                    GameObjectUtility.SetStaticEditorFlags(go, staticFlags);
                    renderer.receiveGI = ReceiveGI.Lightmaps;
                    renderer.scaleInLightmap = group.lightmapScale;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }
                else
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            AssetDatabase.SaveAssets();
        }

        // ---- meshes ----

        /// <summary>
        /// A box at its real size, so textures tile at one scale everywhere: UVs are metres over
        /// TexMetres. fitUv instead stretches the texture once across each face (for prints).
        /// </summary>
        private static Mesh BoxMesh(Vector3 size, bool fitUv)
        {
            Vector3 h = size * 0.5f;
            // Per face: normal, U axis, V axis. Cross(U, V) is -normal, which makes the face wind outward.
            var faces = new (Vector3 n, Vector3 u, Vector3 v)[]
            {
                (Vector3.right, Vector3.forward, Vector3.up),
                (Vector3.left, Vector3.back, Vector3.up),
                (Vector3.up, Vector3.right, Vector3.forward),
                (Vector3.down, Vector3.left, Vector3.forward),
                (Vector3.forward, Vector3.left, Vector3.up),
                (Vector3.back, Vector3.right, Vector3.up),
            };

            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];
            for (int f = 0; f < 6; f++)
            {
                (Vector3 n, Vector3 u, Vector3 v) = faces[f];
                float hu = Mathf.Abs(Vector3.Dot(u, h)), hv = Mathf.Abs(Vector3.Dot(v, h)), hn = Mathf.Abs(Vector3.Dot(n, h));
                Vector3 c = n * hn;
                int i = f * 4;
                vertices[i] = c - u * hu - v * hv;
                vertices[i + 1] = c - u * hu + v * hv;
                vertices[i + 2] = c + u * hu + v * hv;
                vertices[i + 3] = c + u * hu - v * hv;
                float su = fitUv ? 1f : hu * 2f / TexMetres, sv = fitUv ? 1f : hv * 2f / TexMetres;
                uvs[i] = new Vector2(0f, 0f);
                uvs[i + 1] = new Vector2(0f, sv);
                uvs[i + 2] = new Vector2(su, sv);
                uvs[i + 3] = new Vector2(su, 0f);
                for (int k = 0; k < 4; k++) normals[i + k] = n;
                int t = f * 6;
                triangles[t] = i; triangles[t + 1] = i + 1; triangles[t + 2] = i + 2;
                triangles[t + 3] = i; triangles[t + 4] = i + 2; triangles[t + 5] = i + 3;
            }
            return new Mesh { vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
        }

        /// <summary>A flat disc facing up, with the texture laid once across it.</summary>
        private static Mesh DiscMesh(float radius, int segments)
        {
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                vertices[i + 1] = dir * radius;
                normals[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.z * 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            return new Mesh { vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
        }

        // ---- materials and textures ----

        private static Material Mat(string name, Color color, Texture2D texture = null, bool unlit = false)
        {
            string path = $"{EnvFolder}/{name}.mat";
            // Baked Lit shows the lightmap times the colour and ignores realtime lights: the
            // cheapest lit look there is, and the scene's own light cannot wash the room out.
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Baked Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            mat.mainTextureScale = Vector2.one;
            mat.doubleSidedGI = true; // thin and single-sided parts still block light in the bake
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D SaveTexture(string name, Texture2D source, bool repeat)
        {
            string path = $"{EnvFolder}/{name}.png";
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D NewTexture(int width, int height, Func<int, int, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Perlin noise that repeats over size in both directions, so tiling shows no seam.</summary>
        private static float TileNoise(float x, float y, float size, float frequency, float seed)
        {
            float u = x / size, v = y / size;
            float a = Mathf.PerlinNoise(seed + x * frequency, seed + y * frequency);
            float b = Mathf.PerlinNoise(seed + (x - size) * frequency, seed + y * frequency);
            float c = Mathf.PerlinNoise(seed + x * frequency, seed + (y - size) * frequency);
            float d = Mathf.PerlinNoise(seed + (x - size) * frequency, seed + (y - size) * frequency);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>Floorboards running along V: eight boards across, joints staggered, grain along the board.</summary>
        private static Texture2D PlankTexture(int size)
        {
            const int boards = 8;
            var offsets = new float[boards];
            var rng = new System.Random(3);
            for (int i = 0; i < boards; i++) offsets[i] = (float)rng.NextDouble();

            return NewTexture(size, size, (x, y) =>
            {
                float fx = x * boards / (float)size;
                int board = Mathf.Min(boards - 1, (int)fx);
                float across = fx - board;
                float along = y / (float)size + offsets[board];
                int piece = board * 7 + (int)along;
                float tint = 0.90f + 0.16f * Hash(piece);
                // Stretched noise reads as grain; a slower layer gives each board lighter and darker runs.
                float grain = TileNoise(x * 6f + board * 31f, y * 0.22f, size, 0.11f, 5f) * 0.12f
                              + TileNoise(x, y * 0.5f, size, 0.02f, 40f) * 0.10f;
                float value = tint * (0.86f + grain);
                bool gap = across < 0.018f || (along - (int)along) < 0.005f;
                if (gap) value *= 0.62f;
                return new Color(value, value, value);
            });
        }

        private static Texture2D NoiseTexture(int size, float strength, float scale)
        {
            return NewTexture(size, size, (x, y) =>
            {
                float n = TileNoise(x, y, size, scale / size, 11f) * 0.6f + TileNoise(x, y, size, scale * 4f / size, 23f) * 0.4f;
                float value = 1f - strength + n * strength * 2f;
                return new Color(value, value, value);
            });
        }

        private static Texture2D GrainTexture(int size)
        {
            return NewTexture(size, size, (x, y) =>
            {
                float n = TileNoise(x * 5f, y * 0.3f, size, 0.09f, 7f);
                float value = 0.90f + n * 0.16f;
                return new Color(value, value, value);
            });
        }

        private static Texture2D WeaveTexture(int size)
        {
            return NewTexture(size, size, (x, y) =>
            {
                float warp = Mathf.Sin(x * Mathf.PI * 2f * 16f / size) * 0.5f + 0.5f;
                float weft = Mathf.Sin(y * Mathf.PI * 2f * 16f / size) * 0.5f + 0.5f;
                float value = 0.90f + 0.07f * warp * weft + 0.04f * TileNoise(x, y, size, 0.2f, 3f);
                return new Color(value, value, value);
            });
        }

        private static Texture2D RugTexture(int size)
        {
            var cream = new Color(0.87f, 0.83f, 0.75f);
            var teal = new Color(0.44f, 0.57f, 0.57f);
            var clay = new Color(0.80f, 0.56f, 0.44f);
            return NewTexture(size, size, (x, y) =>
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = cream;
                if (r > 0.90f) c = teal;
                else if (r > 0.82f && r < 0.86f) c = clay;
                else if (r > 0.56f && r < 0.64f) c = teal;
                else if (r > 0.30f && r < 0.33f) c = clay;
                else if (r < 0.12f) c = clay;
                float pile = 0.93f + 0.10f * Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                return c * pile;
            });
        }

        /// <summary>
        /// The sea from the waterline (v = 0) to the horizon (v = 1): turquoise shallows deepening
        /// to blue, paling a little into the haze, with faint bands that read as swell.
        /// </summary>
        private static Texture2D OceanTexture(int width, int height)
        {
            var shallow = new Color(0.44f, 0.82f, 0.78f);
            var mid = new Color(0.16f, 0.60f, 0.70f);
            var deep = new Color(0.08f, 0.36f, 0.56f);
            var haze = new Color(0.30f, 0.52f, 0.68f);
            return NewTexture(width, height, (x, y) =>
            {
                float v = y / (height - 1f);
                Color c = v < 0.03f ? Color.Lerp(shallow, mid, v / 0.03f)
                    : v < 0.14f ? Color.Lerp(mid, deep, (v - 0.03f) / 0.11f)
                    : Color.Lerp(deep, haze, Mathf.Pow((v - 0.14f) / 0.86f, 1.6f));
                // Bands get finer with distance, as swell does toward the horizon.
                float bands = Mathf.PerlinNoise(x * 0.07f, Mathf.Pow(v, 0.35f) * 150f);
                float strength = Mathf.Lerp(0.10f, 0.03f, v);
                return c * (1f - strength + bands * strength * 2f);
            });
        }

        /// <summary>The waterline strip: wet sand (v = 0), a ragged line of foam, then the shallows (v = 1).</summary>
        private static Texture2D ShoreTexture(int width, int height)
        {
            var wetSand = new Color(0.72f, 0.63f, 0.47f);
            var foam = new Color(0.97f, 0.98f, 0.97f);
            var shallow = new Color(0.44f, 0.82f, 0.78f);
            return NewTexture(width, height, (x, y) =>
            {
                float v = y / (height - 1f);
                // The foam's edges wander along the beach instead of running ruler-straight.
                float wander = (Mathf.PerlinNoise(x * 0.012f, 3f) - 0.5f) * 0.22f + (Mathf.PerlinNoise(x * 0.07f, 9f) - 0.5f) * 0.06f;
                float inner = 0.34f + wander, outer = 0.52f + wander * 0.6f;
                if (v < inner - 0.05f) return wetSand;
                if (v < inner) return Color.Lerp(wetSand, foam, (v - (inner - 0.05f)) / 0.05f);
                if (v < outer) return Color.Lerp(foam, shallow, Mathf.Pow((v - inner) / (outer - inner), 0.7f) * 0.85f);
                // A second, thinner line of foam further out.
                float second = Mathf.Abs(v - (0.76f + wander * 0.5f));
                return second < 0.02f ? Color.Lerp(foam, shallow, second / 0.02f) : shallow;
            });
        }

        /// <summary>Three simple prints: a sun over hills, an arch, and bands of colour.</summary>
        private static Texture2D ArtTexture(int width, int height, int variant)
        {
            var paper = new Color(0.94f, 0.91f, 0.85f);
            var clay = new Color(0.80f, 0.50f, 0.36f);
            var teal = new Color(0.33f, 0.50f, 0.51f);
            var sand = new Color(0.88f, 0.76f, 0.55f);
            var ink = new Color(0.24f, 0.27f, 0.31f);
            return NewTexture(width, height, (x, y) =>
            {
                float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                float aspect = width / (float)height;
                Color c = paper;
                if (u < 0.07f || u > 0.93f || v < 0.07f * aspect || v > 1f - 0.07f * aspect) return paper; // mount
                switch (variant)
                {
                    case 0:
                        if (Vector2.Distance(new Vector2(u * aspect, v), new Vector2(0.55f * aspect, 0.66f)) < 0.17f) c = clay;
                        if (v < 0.36f + 0.07f * Mathf.Sin(u * 7f)) c = teal;
                        if (v < 0.24f + 0.05f * Mathf.Sin(u * 5f + 2f)) c = ink;
                        break;
                    case 1:
                        float ax = Mathf.Abs(u - 0.5f) * aspect;
                        bool arch = v < 0.58f ? ax < 0.24f : Vector2.Distance(new Vector2(ax, v), new Vector2(0f, 0.58f)) < 0.24f;
                        bool inner = v < 0.56f ? ax < 0.13f : Vector2.Distance(new Vector2(ax, v), new Vector2(0f, 0.56f)) < 0.13f;
                        if (arch) c = sand;
                        if (inner) c = clay;
                        if (v < 0.2f) c = teal;
                        break;
                    default:
                        if (v > 0.62f) c = sand;
                        else if (v > 0.44f) c = clay;
                        else if (v > 0.30f) c = teal;
                        else c = ink;
                        if (Vector2.Distance(new Vector2(u * aspect, v), new Vector2(0.72f * aspect, 0.74f)) < 0.09f) c = paper;
                        break;
                }
                return c * (0.96f + 0.06f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f));
            });
        }

        // ---- small helpers ----

        private static float Hash(int n)
        {
            unchecked
            {
                uint h = (uint)n * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        private static Vector3 RandomEuler() => new Vector3(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f));
        private static T Pick<T>(T[] items) => items[_random.Next(items.Length)];
    }
}
