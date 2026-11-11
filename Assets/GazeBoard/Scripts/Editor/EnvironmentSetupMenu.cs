using System.IO;
using GazeBoard.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

            // 3. Calm room, inactive until the user switches to it. Its lighting is baked separately
            //    (GazeBoard > Calm Room > Build and Bake Lighting).
            Directory.CreateDirectory(EnvFolder);
            Material skybox = CalmRoomBuilder.CreateSkybox();
            GameObject room = CalmRoomBuilder.Build();

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
    }
}
