using System.IO;
using System.Text;
using GazeBoard.Board;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Generates the UI assets the board builder needs, so nothing visual is hand-made:
    /// rounded-rectangle, ring and soft-shadow sprites drawn in code, and TextMesh Pro font
    /// assets for Atkinson Hyperlegible (labels) and Lucide (icons). Safe to run again.
    /// </summary>
    public static class UiAssetFactory
    {
        public const string UiFolder = "Assets/GazeBoard/UI";
        public const string FontFolder = "Assets/GazeBoard/Fonts";

        public const string RoundedRectPath = UiFolder + "/RoundedRect.png";
        public const string RingPath = UiFolder + "/Ring.png";
        public const string SoftShadowPath = UiFolder + "/SoftShadow.png";
        public const string DotPath = UiFolder + "/SoftDot.png";

        public const string LabelFontPath = FontFolder + "/AtkinsonHyperlegible-Bold SDF.asset";
        public const string BodyFontPath = FontFolder + "/AtkinsonHyperlegible-Regular SDF.asset";
        public const string IconFontPath = FontFolder + "/Lucide SDF.asset";

        [MenuItem("GazeBoard/Generate UI Assets (sprites + fonts)")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(UiFolder);
            Directory.CreateDirectory(FontFolder);

            RoundedRect(RoundedRectPath, 96, 28f, 40);
            Ring(RingPath, 128, 62f, 54f); // thin stroke near the tile edge, clear of icon and label
            SoftShadow(SoftShadowPath, 128, 48);
            SoftDot(DotPath, 64, 26f, 6f);

            FontAsset(FontFolder + "/AtkinsonHyperlegible-Bold.ttf", LabelFontPath, AsciiAndPunctuation());
            FontAsset(FontFolder + "/AtkinsonHyperlegible-Regular.ttf", BodyFontPath, AsciiAndPunctuation());
            FontAsset(FontFolder + "/Lucide.ttf", IconFontPath, string.Concat(Icons.AllGlyphs()));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GazeBoard] UI assets generated in " + UiFolder + " and " + FontFolder + ".");
        }

        public static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);
        public static TMP_FontAsset LoadFont(string path) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

        public static bool AllPresent() =>
            LoadSprite(RoundedRectPath) != null && LoadSprite(RingPath) != null && LoadSprite(SoftShadowPath) != null &&
            LoadSprite(DotPath) != null && LoadFont(LabelFontPath) != null && LoadFont(BodyFontPath) != null && LoadFont(IconFontPath) != null;

        // ---- sprites ----

        /// <summary>White rounded rectangle with anti-aliased edges, 9-sliced with the given border.</summary>
        private static void RoundedRect(string path, int size, float radius, int border)
        {
            float half = size / 2f;
            WriteSprite(path, size, (x, y) =>
            {
                float d = RoundedBoxDistance(x + 0.5f - half, y + 0.5f - half, half, half, radius);
                return Mathf.Clamp01(0.5f - d);
            }, border);
        }

        /// <summary>A circular ring (stroke only) for the dwell progress indicator.</summary>
        private static void Ring(string path, int size, float outer, float inner)
        {
            float half = size / 2f;
            WriteSprite(path, size, (x, y) =>
            {
                float r = Mathf.Sqrt(Sq(x + 0.5f - half) + Sq(y + 0.5f - half));
                float a = Mathf.Clamp01(outer - r + 0.5f) * Mathf.Clamp01(r - inner + 0.5f);
                return a;
            }, 0);
        }

        /// <summary>A blurred rounded rectangle used as a drop shadow behind the board panel.</summary>
        private static void SoftShadow(string path, int size, int border)
        {
            float half = size / 2f;
            float inset = border - 8f;         // the opaque core
            float blur = border;               // how far the fade reaches
            WriteSprite(path, size, (x, y) =>
            {
                float d = RoundedBoxDistance(x + 0.5f - half, y + 0.5f - half, half - inset, half - inset, 16f);
                float t = Mathf.Clamp01((d + 6f) / blur);
                return 1f - t * t * (3f - 2f * t);   // smoothstep falloff
            }, border);
        }

        /// <summary>A soft-edged dot for the gaze cursor.</summary>
        private static void SoftDot(string path, int size, float radius, float feather)
        {
            float half = size / 2f;
            WriteSprite(path, size, (x, y) =>
            {
                float r = Mathf.Sqrt(Sq(x + 0.5f - half) + Sq(y + 0.5f - half));
                return 1f - Mathf.Clamp01((r - radius + feather) / feather);
            }, 0);
        }

        private static float RoundedBoxDistance(float px, float py, float bx, float by, float r)
        {
            float qx = Mathf.Abs(px) - bx + r;
            float qy = Mathf.Abs(py) - by + r;
            float outside = Mathf.Sqrt(Sq(Mathf.Max(qx, 0f)) + Sq(Mathf.Max(qy, 0f)));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - r;
        }

        private static float Sq(float v) => v * v;

        private static void WriteSprite(string path, int size, System.Func<int, int, float> alphaAt, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alphaAt(x, y));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        // ---- fonts ----

        private static void FontAsset(string ttfPath, string assetPath, string prebakeCharacters)
        {
            if (LoadFont(assetPath) != null) return;

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogError("[GazeBoard] Font file missing: " + ttfPath);
                return;
            }

            // Same recipe as TMP's "Create > TextMeshPro > Font Asset": a dynamic SDF atlas that
            // grows on demand, pre-filled here so the common glyphs ship in the build.
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
            {
                Debug.LogError("[GazeBoard] Could not create a font asset from " + ttfPath);
                return;
            }

            string name = Path.GetFileNameWithoutExtension(assetPath);
            fontAsset.name = name;
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            fontAsset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            fontAsset.atlasTexture.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);

            if (!string.IsNullOrEmpty(prebakeCharacters))
            {
                fontAsset.TryAddCharacters(prebakeCharacters, out string missing);
                if (!string.IsNullOrEmpty(missing)) Debug.LogWarning($"[GazeBoard] {name}: {missing.Length} characters not in the font.");
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
        }

        private static string AsciiAndPunctuation()
        {
            var sb = new StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            sb.Append("’‘“”…éèàñüöä");
            return sb.ToString();
        }
    }
}
