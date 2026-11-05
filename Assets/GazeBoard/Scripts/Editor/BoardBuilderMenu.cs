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
    ///
    /// Layout (canvas units, 1 unit = 1 mm at the canvas scale of 0.001):
    ///   sentence bar      [ sentence box ........ ][Undo][Clear][Speak][gear]
    ///   categories 2 cols | suggestion bar (Spell only)
    ///                     | tile grid 4x3 (or 6x5 for Spell)
    ///   quick row         [Yes][No][Help][Repeat][Save]
    /// </summary>
    public static class BoardBuilderMenu
    {
        private const string DefinitionPath = "Assets/GazeBoard/DefaultBoard.asset";
        private const string UiRootName = "BoardUI";
        private const string ShadowName = "BoardShadow";
        private const string CursorName = "GazeCursor";

        // Every tile is a TileSide square. Categories sit in CategoryColumns columns on the left,
        // phrases in a 4x3 grid on the right; the canvas size follows from those numbers.
        private const float TileSide = 200f;
        private const float Gap = 24f;
        private const int CategoryColumns = 2;
        private const int GridColumns = 4;
        private const int GridRows = 3;
        private const float SentenceBarHeight = 130f;
        private const float QuickRowHeight = 110f;
        private const float SuggestionHeight = 90f;
        private const float CategoryToGridGap = 48f;
        private const float CategoryBarWidth = CategoryColumns * TileSide + (CategoryColumns - 1) * Gap;
        private const float GridWidth = GridColumns * TileSide + (GridColumns - 1) * Gap;
        private const float GridHeight = GridRows * TileSide + (GridRows - 1) * Gap;
        private static readonly Vector2 CanvasSize = new Vector2(
            Gap + CategoryBarWidth + CategoryToGridGap + GridWidth + Gap,
            Gap + SentenceBarHeight + Gap + GridHeight + Gap + QuickRowHeight + Gap);

        [MenuItem("GazeBoard/Setup Board (assets + definition + UI)")]
        private static void SetupEverything()
        {
            UiAssetFactory.GenerateAll();
            CreateDefaultDefinition();
            BuildBoardUi();
        }

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
            if (!UiAssetFactory.AllPresent()) UiAssetFactory.GenerateAll();

            Undo.SetCurrentGroupName("Build Board UI");
            int undoGroup = Undo.GetCurrentGroup();

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            Undo.RecordObject(canvasRect, "Resize board");
            canvasRect.sizeDelta = CanvasSize;

            RemoveLegacyTiles(canvas.transform);
            foreach (string n in new[] { UiRootName, ShadowName, CursorName })
            {
                Transform old = canvas.transform.Find(n);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }

            // Drop shadow behind the panel: a sibling drawn before the panel itself.
            RectTransform shadow = CreateRect(ShadowName, canvas.transform);
            Stretch(shadow);
            shadow.offsetMin = new Vector2(-70f, -84f);
            shadow.offsetMax = new Vector2(70f, 56f);
            Image shadowImage = shadow.gameObject.AddComponent<Image>();
            shadowImage.sprite = UiAssetFactory.LoadSprite(UiAssetFactory.SoftShadowPath);
            shadowImage.type = Image.Type.Sliced;
            shadowImage.color = new Color(0f, 0f, 0f, 0.45f);
            shadowImage.raycastTarget = false;

            RectTransform root = CreateRect(UiRootName, canvas.transform);
            Stretch(root);
            root.SetAsLastSibling();

            Image background = root.gameObject.AddComponent<Image>();
            background.sprite = RoundedSprite();
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 0.7f; // larger corner radius than the tiles
            background.color = new Color(0.09f, 0.10f, 0.13f, 0.92f);
            background.raycastTarget = false;

            TMP_FontAsset labelFont = UiAssetFactory.LoadFont(UiAssetFactory.LabelFontPath);
            TMP_FontAsset bodyFont = UiAssetFactory.LoadFont(UiAssetFactory.BodyFontPath);
            TMP_FontAsset iconFont = UiAssetFactory.LoadFont(UiAssetFactory.IconFontPath);

            // Template tile, kept inactive and instantiated at runtime.
            BoardTile template = CreateTileTemplate(root, labelFont, iconFont);

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
            boxImage.color = new Color(0.15f, 0.17f, 0.21f, 1f);
            boxImage.raycastTarget = false;
            var boxElement = sentenceBox.gameObject.AddComponent<LayoutElement>();
            boxElement.flexibleWidth = 1f;

            TMP_Text sentenceText = CreateLabel("SentenceText", sentenceBox, 48f, 26f, 56f, TextAlignmentOptions.MidlineLeft, bodyFont);
            sentenceText.margin = new Vector4(28f, 8f, 28f, 8f);
            sentenceText.textWrappingMode = TextWrappingModes.Normal;
            sentenceText.overflowMode = TextOverflowModes.Ellipsis;
            sentenceText.text = "";

            BoardTile undoTile = CreateTileFromTemplate(template, sentenceBar, "UndoTile", 150f);
            BoardTile clearTile = CreateTileFromTemplate(template, sentenceBar, "ClearTile", 150f);
            BoardTile speakTile = CreateTileFromTemplate(template, sentenceBar, "SpeakTile", 190f);
            BoardTile settingsTile = CreateTileFromTemplate(template, sentenceBar, "SettingsTile", SentenceBarHeight);

            // Category column on the left, above the quick row.
            float bodyTop = Gap + SentenceBarHeight + Gap;
            float bodyBottom = Gap + QuickRowHeight + Gap;
            RectTransform categoryBar = CreateRect("CategoryBar", root);
            categoryBar.anchorMin = new Vector2(0f, 0f);
            categoryBar.anchorMax = new Vector2(0f, 1f);
            categoryBar.pivot = new Vector2(0f, 1f);
            categoryBar.anchoredPosition = new Vector2(Gap, -bodyTop);
            categoryBar.sizeDelta = new Vector2(CategoryBarWidth, -(bodyTop + bodyBottom));
            var catLayout = categoryBar.gameObject.AddComponent<GridLayoutGroup>();
            catLayout.spacing = new Vector2(Gap, Gap);
            catLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            catLayout.constraintCount = CategoryColumns;
            catLayout.childAlignment = TextAnchor.UpperLeft;
            catLayout.cellSize = new Vector2(TileSide, TileSide);

            // Suggestion bar at the top of the grid area; the controller shows it in Spell only.
            float left = Gap + CategoryBarWidth + CategoryToGridGap;
            RectTransform suggestionBar = CreateRect("SuggestionBar", root);
            suggestionBar.anchorMin = new Vector2(0f, 1f);
            suggestionBar.anchorMax = new Vector2(1f, 1f);
            suggestionBar.pivot = new Vector2(0.5f, 1f);
            suggestionBar.offsetMin = new Vector2(left, -(bodyTop + SuggestionHeight));
            suggestionBar.offsetMax = new Vector2(-Gap, -bodyTop);
            var suggestionLayout = suggestionBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            suggestionLayout.spacing = Gap;
            suggestionLayout.childControlWidth = true;
            suggestionLayout.childControlHeight = true;
            suggestionLayout.childForceExpandWidth = true;
            suggestionLayout.childForceExpandHeight = true;
            suggestionBar.gameObject.SetActive(false);

            // Tile grid filling the rest.
            RectTransform grid = CreateRect("TileGrid", root);
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 0.5f);
            grid.offsetMin = new Vector2(left, bodyBottom);
            grid.offsetMax = new Vector2(-Gap, -bodyTop);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.spacing = new Vector2(Gap, Gap);
            gridLayout.childAlignment = TextAnchor.MiddleCenter;
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;

            // Quick row along the bottom: always-available tiles.
            RectTransform quickRow = CreateRect("QuickRow", root);
            quickRow.anchorMin = new Vector2(0f, 0f);
            quickRow.anchorMax = new Vector2(1f, 0f);
            quickRow.pivot = new Vector2(0.5f, 0f);
            quickRow.anchoredPosition = new Vector2(0f, Gap);
            quickRow.sizeDelta = new Vector2(-Gap * 2f, QuickRowHeight);
            var quickLayout = quickRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            quickLayout.spacing = Gap;
            quickLayout.childControlWidth = true;
            quickLayout.childControlHeight = true;
            quickLayout.childForceExpandWidth = true;
            quickLayout.childForceExpandHeight = true;

            BoardTile yesTile = CreateTileFromTemplate(template, quickRow, "YesTile", 0f);
            BoardTile noTile = CreateTileFromTemplate(template, quickRow, "NoTile", 0f);
            BoardTile helpTile = CreateTileFromTemplate(template, quickRow, "HelpTile", 0f);
            BoardTile repeatTile = CreateTileFromTemplate(template, quickRow, "RepeatTile", 0f);
            BoardTile recentTile = CreateTileFromTemplate(template, quickRow, "RecentTile", 0f);
            BoardTile saveTile = CreateTileFromTemplate(template, quickRow, "SaveTile", 0f);

            // Gaze cursor on top of everything.
            RectTransform cursor = CreateRect(CursorName, canvas.transform);
            cursor.anchorMin = cursor.anchorMax = new Vector2(0.5f, 0.5f);
            cursor.sizeDelta = new Vector2(22f, 22f);
            Image cursorImage = cursor.gameObject.AddComponent<Image>();
            cursorImage.sprite = UiAssetFactory.LoadSprite(UiAssetFactory.DotPath);
            cursorImage.color = new Color(1f, 1f, 1f, 0.55f);
            cursorImage.raycastTarget = false;
            GazeCursor gazeCursor = cursor.gameObject.AddComponent<GazeCursor>();
            var cursorSo = new SerializedObject(gazeCursor);
            cursorSo.FindProperty("_canvasRect").objectReferenceValue = canvasRect;
            cursorSo.ApplyModifiedProperties();
            cursor.SetAsLastSibling();

            // Theme manager (built-in themes unless assets are assigned) and controller.
            if (canvas.GetComponent<GazeBoard.Theme.ThemeManager>() == null)
            {
                Undo.AddComponent<GazeBoard.Theme.ThemeManager>(canvas.gameObject);
            }

            BoardController controller = canvas.GetComponent<BoardController>();
            if (controller == null) controller = Undo.AddComponent<BoardController>(canvas.gameObject);
            var so = new SerializedObject(controller);
            so.FindProperty("_boardBackground").objectReferenceValue = background;
            so.FindProperty("_panelShadow").objectReferenceValue = shadowImage;
            so.FindProperty("_sentenceBox").objectReferenceValue = boxImage;
            so.FindProperty("_definition").objectReferenceValue = GetOrCreateDefinition(forceDefaults: false);
            so.FindProperty("_tileTemplate").objectReferenceValue = template;
            so.FindProperty("_tileGrid").objectReferenceValue = grid;
            so.FindProperty("_tileGridLayout").objectReferenceValue = gridLayout;
            so.FindProperty("_categoryBar").objectReferenceValue = categoryBar;
            so.FindProperty("_categoryGridLayout").objectReferenceValue = catLayout;
            so.FindProperty("_suggestionBar").objectReferenceValue = suggestionBar;
            so.FindProperty("_suggestionBarHeight").floatValue = SuggestionHeight;
            so.FindProperty("_sentenceText").objectReferenceValue = sentenceText;
            so.FindProperty("_speakTile").objectReferenceValue = speakTile;
            so.FindProperty("_backspaceTile").objectReferenceValue = undoTile;
            so.FindProperty("_clearTile").objectReferenceValue = clearTile;
            so.FindProperty("_settingsTile").objectReferenceValue = settingsTile;
            so.FindProperty("_yesTile").objectReferenceValue = yesTile;
            so.FindProperty("_noTile").objectReferenceValue = noTile;
            so.FindProperty("_helpTile").objectReferenceValue = helpTile;
            so.FindProperty("_repeatTile").objectReferenceValue = repeatTile;
            so.FindProperty("_recentTile").objectReferenceValue = recentTile;
            so.FindProperty("_saveTile").objectReferenceValue = saveTile;
            so.FindProperty("_gazeCursor").objectReferenceValue = gazeCursor;
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

        private static BoardTile CreateTileTemplate(Transform parent, TMP_FontAsset labelFont, TMP_FontAsset iconFont)
        {
            RectTransform rect = CreateRect("TileTemplate", parent);
            rect.sizeDelta = new Vector2(TileSide, TileSide);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = RoundedSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // Icon above the label; BoardTile.SetIcon lays the two out depending on whether an icon exists.
            TMP_Text icon = CreateLabel("Icon", rect, 96f, 20f, 120f, TextAlignmentOptions.Center, iconFont);
            icon.margin = new Vector4(6f, 6f, 6f, 0f);
            icon.textWrappingMode = TextWrappingModes.NoWrap;
            icon.color = Color.white;

            TMP_Text label = CreateLabel("Label", rect, 36f, 18f, 48f, TextAlignmentOptions.Center, labelFont);
            label.margin = new Vector4(10f, 6f, 10f, 8f);
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
            tileSo.FindProperty("_icon").objectReferenceValue = icon;
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
            if (preferredWidth > 0f)
            {
                var element = go.AddComponent<LayoutElement>();
                element.preferredWidth = preferredWidth;
                element.minWidth = preferredWidth;
            }
            return go.GetComponent<BoardTile>();
        }

        private static TMP_Text CreateLabel(string name, Transform parent, float size, float minSize, float maxSize, TextAlignmentOptions align, TMP_FontAsset font)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
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
            UiAssetFactory.LoadSprite(UiAssetFactory.RoundedRectPath)
            ?? AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }
}
