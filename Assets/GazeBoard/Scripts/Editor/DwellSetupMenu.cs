using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Editor helper: adds DwellSelectable plus a radial progress ring to every Button
    /// under the selected GameObjects. Safe to run repeatedly; existing setups are skipped.
    /// </summary>
    public static class DwellSetupMenu
    {
        private const string RingName = "DwellRing";

        [MenuItem("GazeBoard/Add Dwell To Buttons In Selection")]
        private static void AddDwellToSelection()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("GazeBoard",
                    "Select the Board (or any parent of the tile buttons) in the Hierarchy first.", "OK");
                return;
            }

            int configured = 0;
            foreach (GameObject root in Selection.gameObjects)
            {
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                {
                    if (SetupButton(button)) configured++;
                }
            }

            Debug.Log($"[GazeBoard] Dwell setup complete. Buttons configured: {configured}.");
        }

        private static bool SetupButton(Button button)
        {
            GameObject go = button.gameObject;
            bool changed = false;

            DwellSelectable dwell = go.GetComponent<DwellSelectable>();
            if (dwell == null)
            {
                dwell = Undo.AddComponent<DwellSelectable>(go);
                changed = true;
            }

            Transform ringTransform = go.transform.Find(RingName);
            Image ring = ringTransform != null ? ringTransform.GetComponent<Image>() : null;
            if (ring == null)
            {
                ring = CreateRing(go.transform);
                changed = true;
            }

            var so = new SerializedObject(dwell);
            SerializedProperty ringProp = so.FindProperty("_progressRing");
            if (ringProp.objectReferenceValue != ring)
            {
                ringProp.objectReferenceValue = ring;
                so.ApplyModifiedProperties();
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(go);
            return changed;
        }

        internal static Image CreateRing(Transform parent)
        {
            var ringGo = new GameObject(RingName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(ringGo, "Create Dwell Ring");
            ringGo.transform.SetParent(parent, false);
            ringGo.transform.SetAsLastSibling();

            // Inset a little so the ring sits inside the tile instead of touching its edges.
            var rect = ringGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.06f, 0.06f);
            rect.anchorMax = new Vector2(0.94f, 0.94f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // A ring stroke that fills clockwise as the dwell progresses; a solid circle before
            // the generated ring sprite exists.
            var image = ringGo.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiAssetFactory.RingPath)
                           ?? AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0.55f);

            return image;
        }
    }
}
