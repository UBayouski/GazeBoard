using System.IO;
using GazeBoard.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Sets up the two environments: passthrough (the real room, default) and a calm virtual room.
    /// Enables passthrough in the project config and OVRManager, adds the passthrough layer,
    /// builds the room from primitives, and wires an EnvironmentManager. Safe to run again.
    /// </summary>
    public static class EnvironmentSetupMenu
    {
        private const string EnvFolder = "Assets/GazeBoard/Env";
        private const string RoomRootName = "CalmRoom";
        private const string ManagerName = "Environment";

        [MenuItem("GazeBoard/Add Environment (Passthrough + Calm Room)")]
        private static void AddEnvironment()
        {
            OVRManager ovrManager = Object.FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
            Camera centerCamera = FindCenterCamera();
            if (ovrManager == null || centerCamera == null)
            {
                EditorUtility.DisplayDialog("GazeBoard", "Need an OVRManager (Camera Rig building block) and a MainCamera in the open scene.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Add Environment");
            int group = Undo.GetCurrentGroup();

            // 1. Passthrough support in the Meta project config and at runtime.
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
            {
                config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
                OVRProjectConfig.CommitProjectConfig(config);
            }

            var managerSo = new SerializedObject(ovrManager);
            managerSo.FindProperty("isInsightPassthroughEnabled").boolValue = true;
            managerSo.ApplyModifiedProperties();

            OVRPassthroughLayer layer = ovrManager.GetComponent<OVRPassthroughLayer>();
            if (layer == null) layer = Undo.AddComponent<OVRPassthroughLayer>(ovrManager.gameObject);
            var layerSo = new SerializedObject(layer);
            SerializedProperty overlayType = layerSo.FindProperty("overlayType");
            if (overlayType != null) overlayType.enumValueIndex = (int)OVROverlay.OverlayType.Underlay;
            SerializedProperty depth = layerSo.FindProperty("compositionDepth");
            if (depth != null) depth.intValue = 0;
            layerSo.ApplyModifiedProperties();

            // 2. Camera clears to transparent so the passthrough underlay is visible.
            Undo.RecordObject(centerCamera, "Camera clear for passthrough");
            centerCamera.clearFlags = CameraClearFlags.SolidColor;
            centerCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            // 3. Calm room from primitives, inactive until the user switches to it.
            Directory.CreateDirectory(EnvFolder);
            Material skybox = CreateSkybox();
            GameObject room = BuildRoom();

            // 4. Manager.
            GameObject managerGo = GameObject.Find(ManagerName);
            if (managerGo == null)
            {
                managerGo = new GameObject(ManagerName);
                Undo.RegisterCreatedObjectUndo(managerGo, "Create Environment manager");
            }
            EnvironmentManager manager = managerGo.GetComponent<EnvironmentManager>();
            if (manager == null) manager = Undo.AddComponent<EnvironmentManager>(managerGo);
            var so = new SerializedObject(manager);
            so.FindProperty("_centerCamera").objectReferenceValue = centerCamera;
            so.FindProperty("_passthroughLayer").objectReferenceValue = layer;
            so.FindProperty("_roomRoot").objectReferenceValue = room;
            so.FindProperty("_roomSkybox").objectReferenceValue = skybox;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(centerCamera.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[GazeBoard] Environment ready: passthrough enabled (project config + OVRManager + layer), calm room built. Save the scene.");
        }

        private static Camera FindCenterCamera()
        {
            // The rig's disabled eye cameras can also carry the MainCamera tag; use the rig's own reference.
            OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig != null && rig.centerEyeAnchor != null)
            {
                Camera center = rig.centerEyeAnchor.GetComponent<Camera>();
                if (center != null) return center;
            }
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.enabled && c.CompareTag("MainCamera")) return c;
            }
            return Camera.main;
        }

        private static Material CreateSkybox()
        {
            string path = EnvFolder + "/CalmSkybox.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_SunSize", 0f);
            mat.SetFloat("_SunSizeConvergence", 1f);
            mat.SetFloat("_AtmosphereThickness", 0.55f);
            mat.SetColor("_SkyTint", new Color(0.55f, 0.62f, 0.72f));
            mat.SetColor("_GroundColor", new Color(0.16f, 0.17f, 0.20f));
            mat.SetFloat("_Exposure", 0.85f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject BuildRoom()
        {
            GameObject old = GameObject.Find(RoomRootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject(RoomRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create calm room");

            Material floor = LitMaterial("CalmFloor", new Color(0.20f, 0.21f, 0.24f), 0.15f);
            Material rug = LitMaterial("CalmRug", new Color(0.27f, 0.30f, 0.34f), 0.05f);
            Material wall = LitMaterial("CalmWall", new Color(0.78f, 0.80f, 0.83f), 0.05f);
            Material accent = LitMaterial("CalmAccent", new Color(0.46f, 0.60f, 0.66f), 0.2f);

            // Floor at tracking-space ground level.
            Primitive(root, PrimitiveType.Plane, "Floor", new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(3f, 1f, 3f), floor);
            // Soft rug under the user.
            Primitive(root, PrimitiveType.Cylinder, "Rug", new Vector3(0f, 0.005f, 0.4f), Vector3.zero, new Vector3(3.2f, 0.005f, 3.2f), rug);

            // Four walls, 4 m away, 3 m high, open to the sky.
            const float d = 4.5f, h = 3f;
            Primitive(root, PrimitiveType.Cube, "Wall N", new Vector3(0f, h / 2f, d), Vector3.zero, new Vector3(2 * d, h, 0.1f), wall);
            Primitive(root, PrimitiveType.Cube, "Wall S", new Vector3(0f, h / 2f, -d), Vector3.zero, new Vector3(2 * d, h, 0.1f), wall);
            Primitive(root, PrimitiveType.Cube, "Wall E", new Vector3(d, h / 2f, 0f), Vector3.zero, new Vector3(0.1f, h, 2 * d), wall);
            Primitive(root, PrimitiveType.Cube, "Wall W", new Vector3(-d, h / 2f, 0f), Vector3.zero, new Vector3(0.1f, h, 2 * d), wall);

            // A low accent band along the walls and two soft columns beside the board.
            Primitive(root, PrimitiveType.Cube, "Band N", new Vector3(0f, 0.6f, d - 0.06f), Vector3.zero, new Vector3(2 * d, 0.08f, 0.02f), accent);
            Primitive(root, PrimitiveType.Capsule, "Column L", new Vector3(-1.6f, 1.2f, 1.6f), Vector3.zero, new Vector3(0.25f, 1.2f, 0.25f), accent);
            Primitive(root, PrimitiveType.Capsule, "Column R", new Vector3(1.6f, 1.2f, 1.6f), Vector3.zero, new Vector3(0.25f, 1.2f, 0.25f), accent);

            root.SetActive(false);
            return root;
        }

        private static void Primitive(GameObject parent, PrimitiveType type, string name, Vector3 position, Vector3 euler, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // decoration only
        }

        private static Material LitMaterial(string name, Color color, float smoothness)
        {
            string path = $"{EnvFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
