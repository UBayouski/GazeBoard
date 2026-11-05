using System.IO;
using GazeBoard.Board;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Rebuilds the Board canvas into the full communication board layout and wires a
    /// BoardController to it. Safe to run again: it replaces the generated "BoardUI" child
    /// and leaves the Interaction SDK canvas objects untouched.
    /// </summary>
    public static class BoardBuilderMenu
    {
        private const string DefinitionPath = "Assets/GazeBoard/DefaultBoard.asset";
        private const string UiRootName = "BoardUI";

        // Every tile is a TileSide square. Categories sit in CategoryColumns columns on the left,
        // phrases in a 4x3 grid on the right; the canvas size follows from those numbers.
        private const float TileSide = 200f;
        private const float Gap = 24f;
        private const int CategoryColumns = 2;
        private const int GridColumns = 4;
        private const int GridRows = 3;
        private const float SentenceBarHeight = 130f;
        private const float CategoryToGridGap = 48f;
        private const float CategoryBarWidth = CategoryColumns * TileSide + (CategoryColumns - 1) * Gap;
        private const float GridWidth = GridColumns * TileSide + (GridColumns - 1) * Gap;
        private const float GridHeight = GridRows * TileSide + (GridRows - 1) * Gap;
        private static readonly Vector2 CanvasSize = new Vector2(
            Gap + CategoryBarWidth + CategoryToGridGap + GridWidth + Gap,
            Gap + SentenceBarHeight + Gap + GridHeight + Gap);

        [MenuItem("GazeBoard/Create Default Board Definition")]
        private static void CreateDefaultDefinition()
        {
            BoardDefinition def = GetOrCreateDefinition(forceDefaults: true);
            Selection.activeObject = def;
            Debug.Log($"[GazeBoard] Default board written to {DefinitionPath} ({def.categories.Count} categories).");
        }

        [MenuItem("GazeBoard/Build Board UI")]
        private static void BuildBoardUi()
        {
            Canvas canvas = FindBoardCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("GazeBoard", "No world-space Canvas named 'Board' found in the open scene.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build Board UI");
            int undoGroup = Undo.GetCurrentGroup();

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            Undo.RecordObject(canvasRect, "Resize board");
            canvasRect.sizeDelta = CanvasSize;

            RemoveLegacyTiles(canvas.transform);

            Transform old = canvas.transform.Find(UiRootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            RectTransform root = CreateRect(UiRootName, canvas.transform);
            Stretch(root);
            root.SetAsLastSibling();

            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.08f, 0.09f, 0.11f, 0.92f);
            background.raycastTarget = false;

            // Template tile, kept inactive and instantiated at runtime.
            BoardTile template = CreateTileTemplate(root);

            // Sentence bar across the top.
            RectTransform sentenceBar = CreateRect("SentenceBar", root);
            sentenceBar.anchorMin = new Vector2(0f, 1f);
            sentenceBar.anchorMax = new Vector2(1f, 1f);
            sentenceBar.pivot = new Vector2(0.5f, 1f);
            sentenceBar.anchoredPosition = new Vector2(0f, -Gap);
            sentenceBar.sizeDelta = new Vector2(-Gap * 2f, SentenceBarHeight);
            var barLayout = sentenceBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = Gap;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childForceExpandHeight = true;

            RectTransform sentenceBox = CreateRect("SentenceBox", sentenceBar);
            Image boxImage = sentenceBox.gameObject.AddComponent<Image>();
            boxImage.sprite = RoundedSprite();
            boxImage.type = Image.Type.Sliced;
            boxImage.color = new Color(0.14f, 0.15f, 0.18f, 1f);
            boxImage.raycastTarget = false;
            var boxElement = sentenceBox.gameObject.AddComponent<LayoutElement>();
            boxElement.flexibleWidth = 1f;

            TMP_Text sentenceText = CreateLabel("SentenceText", sentenceBox, 40f, 24f, 44f, TextAlignmentOptions.MidlineLeft);
            sentenceText.margin = new Vector4(24f, 8f, 24f, 8f);
            sentenceText.textWrappingMode = TextWrappingModes.Normal;
            sentenceText.overflowMode = TextOverflowModes.Ellipsis;
            sentenceText.text = "";

            BoardTile undoTile = CreateTileFromTemplate(template, sentenceBar, "UndoTile", 150f);
            BoardTile clearTile = CreateTileFromTemplate(template, sentenceBar, "ClearTile", 150f);
            BoardTile speakTile = CreateTileFromTemplate(template, sentenceBar, "SpeakTile", 190f);

            // Category column on the left.
            float bodyTop = Gap + SentenceBarHeight + Gap;
            RectTransform categoryBar = CreateRect("CategoryBar", root);
            categoryBar.anchorMin = new Vector2(0f, 0f);
            categoryBar.anchorMax = new Vector2(0f, 1f);
            categoryBar.pivot = new Vector2(0f, 1f);
            categoryBar.anchoredPosition = new Vector2(Gap, -bodyTop);
            categoryBar.sizeDelta = new Vector2(CategoryBarWidth, -(bodyTop + Gap));
            // One-column grid so category tiles can be squares; the controller sets the cell size.
            var catLayout = categoryBar.gameObject.AddComponent<GridLayoutGroup>();
            catLayout.spacing = new Vector2(Gap, Gap);
            catLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            catLayout.constraintCount = CategoryColumns;
            catLayout.childAlignment = TextAnchor.UpperLeft;
            catLayout.cellSize = new Vector2(TileSide, TileSide);

            // Tile grid filling the rest.
            RectTransform grid = CreateRect("TileGrid", root);
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 0.5f);
            float left = Gap + CategoryBarWidth + CategoryToGridGap;
            grid.offsetMin = new Vector2(left, Gap);
            grid.offsetMax = new Vector2(-Gap, -bodyTop);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.spacing = new Vector2(Gap, Gap);
            gridLayout.childAlignment = TextAnchor.MiddleCenter;
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;

            // Theme manager (built-in themes unless assets are assigned) and controller.
            if (canvas.GetComponent<GazeBoard.Theme.ThemeManager>() == null)
            {
                Undo.AddComponent<GazeBoard.Theme.ThemeManager>(canvas.gameObject);
            }

            BoardController controller = canvas.GetComponent<BoardController>();
            if (controller == null) controller = Undo.AddComponent<BoardController>(canvas.gameObject);
            var so = new SerializedObject(controller);
            so.FindProperty("_boardBackground").objectReferenceValue = background;
            so.FindProperty("_sentenceBox").objectReferenceValue = boxImage;
            so.FindProperty("_definition").objectReferenceValue = GetOrCreateDefinition(forceDefaults: false);
            so.FindProperty("_tileTemplate").objectReferenceValue = template;
            so.FindProperty("_tileGrid").objectReferenceValue = grid;
            so.FindProperty("_tileGridLayout").objectReferenceValue = gridLayout;
            so.FindProperty("_categoryBar").objectReferenceValue = categoryBar;
            so.FindProperty("_categoryGridLayout").objectReferenceValue = catLayout;
            so.FindProperty("_sentenceText").objectReferenceValue = sentenceText;
            so.FindProperty("_speakTile").objectReferenceValue = speakTile;
            so.FindProperty("_backspaceTile").objectReferenceValue = undoTile;
            so.FindProperty("_clearTile").objectReferenceValue = clearTile;
            so.ApplyModifiedProperties();

            template.gameObject.SetActive(false);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log("[GazeBoard] Board UI built. Press Play or Build And Run to see it populated.");
        }

        // ---- helpers ----

        private static Canvas FindBoardCanvas()
        {
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.gameObject.name == "Board" && c.renderMode == RenderMode.WorldSpace) return c;
            }
            return null;
        }

        private static void RemoveLegacyTiles(Transform canvas)
        {
            foreach (string n in new[] { "Yes", "No", "Maybe" })
            {
                Transform t = canvas.Find(n);
                if (t != null && t.GetComponent<Button>() != null && t.GetComponent<BoardTile>() == null)
                {
                    Undo.DestroyObjectImmediate(t.gameObject);
                }
            }
        }

        private static BoardDefinition GetOrCreateDefinition(bool forceDefaults)
        {
            var def = AssetDatabase.LoadAssetAtPath<BoardDefinition>(DefinitionPath);
            if (def == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DefinitionPath));
                def = ScriptableObject.CreateInstance<BoardDefinition>();
                def.PopulateDefaults();
                AssetDatabase.CreateAsset(def, DefinitionPath);
                AssetDatabase.SaveAssets();
            }
            else if (forceDefaults)
            {
                Undo.RecordObject(def, "Reset board definition");
                def.PopulateDefaults();
                EditorUtility.SetDirty(def);
                AssetDatabase.SaveAssets();
            }
            return def;
        }

        private static BoardTile CreateTileTemplate(Transform parent)
        {
            RectTransform rect = CreateRect("TileTemplate", parent);
            rect.sizeDelta = new Vector2(200f, 150f);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = null; // plain rectangle
            bg.type = Image.Type.Simple;
            bg.color = Color.white;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TMP_Text label = CreateLabel("Label", rect, 36f, 18f, 48f, TextAlignmentOptions.Center);
            label.margin = new Vector4(10f, 8f, 10f, 8f);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.color = Color.white;

            Image ring = DwellSetupMenu.CreateRing(rect);

            DwellSelectable dwell = rect.gameObject.AddComponent<DwellSelectable>();
            var dwellSo = new SerializedObject(dwell);
            dwellSo.FindProperty("_progressRing").objectReferenceValue = ring;
            dwellSo.ApplyModifiedProperties();

            // Outline for themes that want one; ThemeManager enables and colours it.
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.enabled = false;
            outline.useGraphicAlpha = false;

            BoardTile tile = rect.gameObject.AddComponent<BoardTile>();
            var tileSo = new SerializedObject(tile);
            tileSo.FindProperty("_label").objectReferenceValue = label;
            tileSo.FindProperty("_background").objectReferenceValue = bg;
            tileSo.FindProperty("_outline").objectReferenceValue = outline;
            tileSo.FindProperty("_roundedSprite").objectReferenceValue = RoundedSprite();
            tileSo.ApplyModifiedProperties();

            return tile;
        }

        private static BoardTile CreateTileFromTemplate(BoardTile template, Transform parent, string name, float preferredWidth)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.name = name;
            go.SetActive(true);
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.minWidth = preferredWidth;
            return go.GetComponent<BoardTile>();
        }

        private static TMP_Text CreateLabel(string name, Transform parent, float size, float minSize, float maxSize, TextAlignmentOptions align)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Sprite RoundedSprite() =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }
}
