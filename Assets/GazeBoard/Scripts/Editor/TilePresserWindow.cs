using GazeBoard.Board;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Play-mode helper: lists the tiles now on the board and presses the one you click. The
    /// board only answers to gaze, hands and controllers, so without a headset the mouse cannot
    /// choose a tile in the Game view; this stands in for it (screenshots, store captures).
    /// </summary>
    public class TilePresserWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("GazeBoard/Press Tiles (Play Mode)")]
        private static void Open() => GetWindow<TilePresserWindow>("Press Tiles");

        private void OnInspectorUpdate() => Repaint(); // the tiles change as pages open

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to press the board's tiles.", MessageType.Info);
                return;
            }

            BoardTile pressed = null;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Transform group = null;
            BoardTile[] tiles = FindObjectsByType<BoardTile>(FindObjectsSortMode.None);
            System.Array.Sort(tiles, (a, b) =>
            {
                Transform pa = a.transform.parent, pb = b.transform.parent;
                if (pa != pb) return (pa != null ? pa.GetInstanceID() : 0).CompareTo(pb != null ? pb.GetInstanceID() : 0);
                return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
            });
            foreach (BoardTile tile in tiles)
            {
                var button = tile.GetComponent<Button>();
                if (button == null || !button.IsInteractable() || tile.Label == null) continue;
                string label = tile.Label.text.Replace("\n", " ").Trim();
                if (label.Length == 0) continue;

                // Tiles come grouped by their place on the board: grid, categories, the fixed rows.
                if (tile.transform.parent != group)
                {
                    group = tile.transform.parent;
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(group != null ? group.name : "Board", EditorStyles.boldLabel);
                }
                if (GUILayout.Button(label)) pressed = tile;
            }
            EditorGUILayout.EndScrollView();

            // Pressed after the loop: a press can rebuild the tiles being listed.
            if (pressed != null) pressed.GetComponent<Button>().onClick.Invoke();
        }
    }
}
