using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Editor helper: adds SpeakOnClick to every Button under the selection and removes the
    /// prototype "SetActive(false)" test handlers that hid tiles when clicked.
    /// </summary>
    public static class SpeakSetupMenu
    {
        [MenuItem("GazeBoard/Add Speak To Buttons In Selection")]
        private static void AddSpeakToSelection()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("GazeBoard",
                    "Select the Board (or any parent of the tile buttons) in the Hierarchy first.", "OK");
                return;
            }

            int added = 0, removedHandlers = 0, relabelled = 0;
            foreach (GameObject root in Selection.gameObjects)
            {
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                {
                    removedHandlers += RemoveSetActiveHandlers(button);
                    if (FixPlaceholderLabel(button)) relabelled++;

                    if (button.GetComponent<SpeakOnClick>() == null)
                    {
                        Undo.AddComponent<SpeakOnClick>(button.gameObject);
                        EditorUtility.SetDirty(button.gameObject);
                        added++;
                    }
                }
            }

            Debug.Log($"[GazeBoard] Speak setup complete. SpeakOnClick added: {added}. Old SetActive handlers removed: {removedHandlers}. Labels set from object name: {relabelled}.");
        }

        /// <summary>Replace Unity's default "Button" label with the GameObject name, e.g. "Yes".</summary>
        private static bool FixPlaceholderLabel(Button button)
        {
            var label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label == null || label.text != "Button") return false;
            Undo.RecordObject(label, "Set tile label");
            label.text = button.gameObject.name;
            EditorUtility.SetDirty(label);
            return true;
        }

        private static int RemoveSetActiveHandlers(Button button)
        {
            int removed = 0;
            Undo.RecordObject(button, "Remove SetActive handlers");
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                if (button.onClick.GetPersistentMethodName(i) == "SetActive")
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
                    removed++;
                }
            }
            if (removed > 0) EditorUtility.SetDirty(button);
            return removed;
        }
    }
}
