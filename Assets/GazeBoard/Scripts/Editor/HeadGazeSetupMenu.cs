using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Editor helper: adds HeadGazeFallback to every EyeGaze component in the open scene,
    /// so gaze works on headsets without eye tracking. Safe to run repeatedly.
    /// </summary>
    public static class HeadGazeSetupMenu
    {
        [MenuItem("GazeBoard/Add Head Gaze Fallback To Scene")]
        private static void AddHeadGazeFallback()
        {
            EyeGaze[] gazes = Object.FindObjectsByType<EyeGaze>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (gazes.Length == 0)
            {
                EditorUtility.DisplayDialog("GazeBoard",
                    "No EyeGaze component found in the open scene. Add the Gaze Interaction building block first.", "OK");
                return;
            }

            int added = 0;
            foreach (EyeGaze gaze in gazes)
            {
                if (gaze.GetComponent<HeadGazeFallback>() != null) continue;
                Undo.AddComponent<HeadGazeFallback>(gaze.gameObject);
                EditorUtility.SetDirty(gaze.gameObject);
                added++;
            }

            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(gazes[0].gameObject.scene);
            }

            Debug.Log($"[GazeBoard] HeadGazeFallback added to {added} EyeGaze component(s); {gazes.Length - added} already had it.");
        }
    }
}
