using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityDrift.Editor
{
    /// <summary>
    /// Generates persistent UI Sprite assets (Assets/UI/Sprites/), editable uGUI + TextMeshPro Canvas prefabs
    /// (Assets/UI/*.prefab), and wires all serialized UI references onto GameUIManager, DebugOverlay, and
    /// GravityDriftBootstrap inside Assets/Scenes/GravityDrift_Room.unity.
    /// </summary>
    public static class GravityDriftCanvasBuilder
    {
        private const string UIRootFolder = "Assets/UI";
        private const string SpritesFolder = "Assets/UI/Sprites";
        private const string UiCanvasPrefabPath = "Assets/UI/GravityDrift_UI_Canvas.prefab";
        private const string DebugCanvasPrefabPath = "Assets/UI/GravityDrift_Debug_Canvas.prefab";
        private const string FadeCanvasPrefabPath = "Assets/UI/GravityDrift_FadeCanvas.prefab";

        private struct SpriteSet
        {
            public Sprite solidWhite;
            public Sprite panelBox;
            public Sprite controlBox;
            public Sprite accentBox;
            public Sprite dangerBox;
            public Sprite gravityArrow;
        }

        private struct FontPair
        {
            public TMP_FontAsset displayFont;
            public TMP_FontAsset bodyFont;
        }

        [MenuItem("GravityDrift/Rebuild Editable UI Canvases")]
        public static void BuildAndWireSceneUI()
        {
            EnsureFolder(UIRootFolder);
            EnsureFolder(SpritesFolder);

            SpriteSet sprites = EnsureUiSprites();
            FontPair fonts = ResolveProjectFonts();

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                scene = EditorSceneManager.OpenScene("Assets/Scenes/GravityDrift_Room.unity", OpenSceneMode.Single);
            }

            // Ensure single EventSystem with InputSystemUIInputModule
            EnsureSceneEventSystem();

            // Replace existing Canvas roots cleanly (including inactive ones like GravityDrift_FadeCanvas)
            DestroyExistingRootObjects(scene, "GravityDrift_UI_Canvas");
            DestroyExistingRootObjects(scene, "GravityDrift_Debug_Canvas");
            DestroyExistingRootObjects(scene, "GravityDrift_FadeCanvas");

            // 1. Build Main UI Canvas & Save Prefab cleanly without merge warnings
            GameObject uiTempGo = CreateCanvasObject("GravityDrift_UI_Canvas", 10, pixelPerfect: true);
            RectTransform scrimTempRt = CreatePanel(uiTempGo.transform, "BackdropScrim", sprites.solidWhite, GravityDriftUITheme.BackdropScrim, raycastTarget: true);
            GravityDriftUITheme.StretchFill(scrimTempRt);

            GameObject mainMenuTemp = BuildMainMenu(uiTempGo.transform, sprites, fonts, out _);
            GameObject pauseMenuTemp = BuildPauseMenu(uiTempGo.transform, sprites, fonts, out _);
            GameObject settingsMenuTemp = BuildSettingsMenu(uiTempGo.transform, sprites, fonts, out _);
            GameObject sessionReportTemp = BuildSessionReport(uiTempGo.transform, sprites, fonts, out _);
            GameObject runOverTemp = BuildRunOver(uiTempGo.transform, sprites, fonts, out _);
            GameObject gameplayHudTemp = BuildGameplayHUD(uiTempGo.transform, sprites, fonts, out _);

            scrimTempRt.gameObject.SetActive(true);
            mainMenuTemp.SetActive(true);
            pauseMenuTemp.SetActive(false);
            settingsMenuTemp.SetActive(false);
            sessionReportTemp.SetActive(false);
            runOverTemp.SetActive(false);
            gameplayHudTemp.SetActive(false);

            GameObject uiPrefab = ReplaceWithCleanPrefabInstance(uiTempGo, UiCanvasPrefabPath, scene, out GameObject uiCanvasGo);

            // 2. Build Debug Overlay Canvas & Save Prefab cleanly
            GameObject debugTempGo = CreateCanvasObject("GravityDrift_Debug_Canvas", 20, pixelPerfect: true);
            RectTransform dbgPanelTemp = CreatePanel(debugTempGo.transform, "DebugTelemetryCard", sprites.panelBox, Color.white, raycastTarget: false);
            dbgPanelTemp.anchorMin = new Vector2(0f, 0f);
            dbgPanelTemp.anchorMax = new Vector2(0f, 0f);
            dbgPanelTemp.pivot = new Vector2(0f, 0f);
            dbgPanelTemp.anchoredPosition = new Vector2(20f, 18f);
            dbgPanelTemp.sizeDelta = new Vector2(430f, 245f);

            var dbgVlg = dbgPanelTemp.gameObject.AddComponent<VerticalLayoutGroup>();
            dbgVlg.padding = new RectOffset(14, 14, 10, 10);
            dbgVlg.spacing = 6f;
            dbgVlg.childControlWidth = true;
            dbgVlg.childControlHeight = true;

            var dbgHdr = CreateText(dbgPanelTemp, "DebugHdr", "BAYESIAN POSTERIOR (π) & GRID A*  [TAB]", 13f, GravityDriftUITheme.AccentSkyBlue, fonts, isDisplay: true);
            dbgHdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var dbgBodyTemp = CreateText(dbgPanelTemp, "DebugBody", "", 11.5f, GravityDriftUITheme.TextPrimary, fonts, isDisplay: false);
            dbgBodyTemp.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            dbgPanelTemp.gameObject.SetActive(false);

            ReplaceWithCleanPrefabInstance(debugTempGo, DebugCanvasPrefabPath, scene, out GameObject debugCanvasGo);

            // 3. Build Fade Transition Canvas & Save Prefab cleanly
            GameObject fadeTempGo = CreateCanvasObject("GravityDrift_FadeCanvas", 50, pixelPerfect: false);
            var fadeRaycaster = fadeTempGo.GetComponent<GraphicRaycaster>();
            if (fadeRaycaster != null) Object.DestroyImmediate(fadeRaycaster);

            RectTransform fadeTempRt = CreatePanel(fadeTempGo.transform, "FadeOverlay", sprites.solidWhite, new Color(11f / 255f, 14f / 255f, 20f / 255f, 0f), raycastTarget: false);
            GravityDriftUITheme.StretchFill(fadeTempRt);
            fadeTempGo.SetActive(false);

            ReplaceWithCleanPrefabInstance(fadeTempGo, FadeCanvasPrefabPath, scene, out GameObject fadeCanvasGo);

            // 4. Wire serialized references directly from instantiated scene Canvas objects
            WireSceneManagersFromRoots(sprites, uiCanvasGo, debugCanvasGo, fadeCanvasGo);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"<color=#38BDF8><b>[CANVAS BUILDER] Built editable UI Canvases, prefabs ({uiPrefab.name}), and wired scene {scene.path}</b></color>");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, name);
        }

        private static SpriteSet EnsureUiSprites()
        {
            var set = new SpriteSet
            {
                solidWhite = SaveSolidSpriteAsset($"{SpritesFolder}/UI_SolidWhite.png"),
                panelBox = SaveBorderedSpriteAsset($"{SpritesFolder}/UI_PanelBox.png", GravityDriftUITheme.PanelSurface, GravityDriftUITheme.BorderHairline),
                controlBox = SaveBorderedSpriteAsset($"{SpritesFolder}/UI_ControlBox.png", GravityDriftUITheme.ControlSurface, GravityDriftUITheme.BorderHairline),
                accentBox = SaveBorderedSpriteAsset($"{SpritesFolder}/UI_AccentBox.png", GravityDriftUITheme.AccentTintFill, GravityDriftUITheme.AccentSkyBlue),
                dangerBox = SaveBorderedSpriteAsset($"{SpritesFolder}/UI_DangerBox.png", GravityDriftUITheme.DangerTintFill, GravityDriftUITheme.DangerRed),
                gravityArrow = SaveGravityArrowSpriteAsset($"{SpritesFolder}/UI_GravityArrow.png")
            };
            AssetDatabase.Refresh();
            return set;
        }

        private static Sprite SaveSolidSpriteAsset(string assetPath)
        {
            const int S = 8;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < px.Length; i++) px[i] = white;
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            ConfigureSpriteImporter(assetPath, Vector4.zero, FilterMode.Point);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite SaveBorderedSpriteAsset(string assetPath, Color32 fill, Color32 border)
        {
            const int S = 24;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    bool isEdge = (x == 0 || y == 0 || x == S - 1 || y == S - 1);
                    px[y * S + x] = isEdge ? border : fill;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            ConfigureSpriteImporter(assetPath, new Vector4(2f, 2f, 2f, 2f), FilterMode.Point);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite SaveGravityArrowSpriteAsset(string assetPath)
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float nx = (x + 0.5f) / S - 0.5f;
                    float ny = (y + 0.5f) / S - 0.5f;
                    bool shaft = (Mathf.Abs(nx) <= 0.11f && ny >= -0.34f && ny <= 0.10f);
                    bool head = (ny >= 0.02f && ny <= 0.36f && Mathf.Abs(nx) <= (0.36f - ny) * 0.95f);
                    px[y * S + x] = (shaft || head) ? white : clear;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            ConfigureSpriteImporter(assetPath, Vector4.zero, FilterMode.Bilinear);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void ConfigureSpriteImporter(string assetPath, Vector4 border, FilterMode filterMode)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.filterMode = filterMode;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        private static FontPair ResolveProjectFonts()
        {
            var pair = new FontPair();
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null) continue;
                string lower = font.name.ToLowerInvariant();
                if (lower.Contains("roboto-bold")) pair.displayFont = font;
                else if (lower == "liberationsans sdf") pair.bodyFont = font;
            }
            if (pair.displayFont == null) pair.displayFont = TMP_Settings.defaultFontAsset;
            if (pair.bodyFont == null) pair.bodyFont = TMP_Settings.defaultFontAsset ?? pair.displayFont;
            return pair;
        }

        private static void EnsureSceneEventSystem()
        {
            var existing = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            for (int i = 1; i < existing.Length; i++)
            {
                Object.DestroyImmediate(existing[i].gameObject);
            }

            EventSystem es = existing.Length > 0 ? existing[0] : null;
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
                go.AddComponent<InputSystemUIInputModule>();
            }
            else if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                var legacy = es.GetComponent<StandaloneInputModule>();
                if (legacy != null) Object.DestroyImmediate(legacy);
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void DestroyExistingRootObjects(Scene scene, string objName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root != null && root.name == objName)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static GameObject ReplaceWithCleanPrefabInstance(GameObject sourceGo, string prefabPath, Scene scene, out GameObject instanceGo)
        {
            bool wasActive = sourceGo.activeSelf;
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(sourceGo, prefabPath);
            Object.DestroyImmediate(sourceGo);
            instanceGo = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene);
            instanceGo.SetActive(wasActive);
            return prefabAsset;
        }

        private static T FindChildComp<T>(Transform root, string relativePath) where T : Component
        {
            var tf = root.Find(relativePath);
            return tf != null ? tf.GetComponent<T>() : null;
        }

        private static GameObject FindChildGo(Transform root, string relativePath)
        {
            var tf = root.Find(relativePath);
            return tf != null ? tf.gameObject : null;
        }

        private static GameObject CreateCanvasObject(string name, int sortOrder, bool pixelPerfect)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            canvas.pixelPerfect = pixelPerfect;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Sprite sprite, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = sprite;
            img.type = (sprite != null && sprite.border != Vector4.zero) ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycastTarget;
            return go.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string content,
            float fontSize,
            Color color,
            FontPair fonts,
            bool isDisplay = false,
            TextAlignmentOptions align = TextAlignmentOptions.Left,
            bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = isDisplay ? fonts.displayFont : fonts.bodyFont;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = raycastTarget;
            if (isDisplay)
            {
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 5f;
            }
            return tmp;
        }

        private static UnityEngine.UI.Button CreateButton(
            Transform parent,
            string name,
            string label,
            SpriteSet sprites,
            FontPair fonts,
            bool primaryAccent,
            float height = 44f)
        {
            RectTransform rt = CreatePanel(parent, name, primaryAccent ? sprites.accentBox : sprites.controlBox, Color.white, raycastTarget: true);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.22f, 1.32f, 1.45f, 1f);
            cb.selectedColor = primaryAccent ? new Color(1.25f, 1.35f, 1.50f, 1f) : new Color(0.85f, 1.15f, 1.45f, 1f);
            cb.pressedColor = new Color(0.75f, 0.85f, 0.95f, 1f);
            btn.colors = cb;

            var tmp = CreateText(rt, "Label", label, 15f, primaryAccent ? GravityDriftUITheme.AccentSkyBlue : GravityDriftUITheme.TextPrimary, fonts, isDisplay: true, align: TextAlignmentOptions.Center, raycastTarget: false);
            GravityDriftUITheme.StretchFill(tmp.rectTransform, 10f, 4f);
            rt.gameObject.AddComponent<UIAudioButtonRelay>();
            return btn;
        }

        private static UnityEngine.UI.Slider CreateSlider(Transform parent, string name, SpriteSet sprites, float min, float max, float initial)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.flexibleWidth = 1f;

            var bg = CreatePanel(root.transform, "Background", sprites.controlBox, Color.white, raycastTarget: true);
            GravityDriftUITheme.StretchFill(bg, 0f, 7f);

            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(root.transform, false);
            GravityDriftUITheme.StretchFill(fillArea, 2f, 8f);

            var fill = CreatePanel(fillArea, "Fill", sprites.solidWhite, GravityDriftUITheme.AccentSkyBlue, raycastTarget: false);
            GravityDriftUITheme.StretchFill(fill);

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(root.transform, false);
            GravityDriftUITheme.StretchFill(handleArea, 6f, 0f);

            var handle = CreatePanel(handleArea, "Handle", sprites.solidWhite, GravityDriftUITheme.TextPrimary, raycastTarget: true);
            handle.sizeDelta = new Vector2(14f, 20f);

            var slider = root.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            slider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
            return slider;
        }

        private static UnityEngine.UI.Toggle CreateToggle(Transform parent, string name, string label, SpriteSet sprites, FontPair fonts, bool initial)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;

            var box = CreatePanel(row.transform, "Box", sprites.controlBox, Color.white, raycastTarget: true);
            var boxLe = box.gameObject.AddComponent<LayoutElement>();
            boxLe.preferredWidth = 22f;
            boxLe.preferredHeight = 22f;

            var check = CreatePanel(box, "Check", sprites.solidWhite, GravityDriftUITheme.AccentSkyBlue, raycastTarget: false);
            GravityDriftUITheme.StretchFill(check, 4f, 4f);

            var txt = CreateText(row.transform, "Label", label, 14f, GravityDriftUITheme.TextPrimary, fonts, isDisplay: false, align: TextAlignmentOptions.Left, raycastTarget: true);
            txt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var toggle = row.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = box.GetComponent<UnityEngine.UI.Image>();
            toggle.graphic = check.GetComponent<UnityEngine.UI.Image>();
            toggle.isOn = initial;
            row.AddComponent<UIAudioButtonRelay>();
            return toggle;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, SpriteSet sprites, FontPair fonts, string initial)
        {
            var rt = CreatePanel(parent, name, sprites.controlBox, Color.white, raycastTarget: true);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.preferredWidth = 140f;

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            textArea.SetParent(rt, false);
            GravityDriftUITheme.StretchFill(textArea, 10f, 4f);

            var textTmp = CreateText(textArea, "Text", initial, 16f, GravityDriftUITheme.TextPrimary, fonts, isDisplay: false, align: TextAlignmentOptions.Left, raycastTarget: true);
            textTmp.richText = false;
            GravityDriftUITheme.StretchFill(textTmp.rectTransform);

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = rt.GetComponent<UnityEngine.UI.Image>();
            input.textViewport = textArea;
            input.textComponent = textTmp;
            input.fontAsset = fonts.bodyFont;
            input.text = initial;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.customCaretColor = true;
            input.caretColor = GravityDriftUITheme.AccentSkyBlue;
            return input;
        }

        private static RectTransform CreateHorizontalRow(Transform parent, string name, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return go.GetComponent<RectTransform>();
        }

        private struct MainRefs
        {
            public TMP_InputField seedInputField;
            public UnityEngine.UI.Button randomizeSeedBtn;
            public TextMeshProUGUI levelStepperText;
            public UnityEngine.UI.Button levelMinusBtn;
            public UnityEngine.UI.Button levelPlusBtn;
            public UnityEngine.UI.Button mainLives3Btn;
            public UnityEngine.UI.Button mainLives5Btn;
            public UnityEngine.UI.Button startMissionBtn;
            public UnityEngine.UI.Button mainSettingsBtn;
            public UnityEngine.UI.Button mainReportBtn;
            public UnityEngine.UI.Button quitBtn;
        }

        private static GameObject BuildMainMenu(Transform parent, SpriteSet sprites, FontPair fonts, out MainRefs refs)
        {
            refs = default;
            var card = CreatePanel(parent, "MainMenuCard", sprites.panelBox, Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(680f, 584f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 24, 24);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateText(card, "Title", "GRAVITY DRIFT", 36f, GravityDriftUITheme.AccentSkyBlue, fonts, true, TextAlignmentOptions.Center)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            CreateText(card, "Subtitle", "2.5D ORBITAL PUZZLE-ACTION  ·  PC & PLAYSTATION DUALSENSE", 12.5f, GravityDriftUITheme.TextSecondary, fonts, true, TextAlignmentOptions.Center)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            CreatePanel(card, "Divider", sprites.solidWhite, GravityDriftUITheme.BorderHairline)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            var seedRow = CreateHorizontalRow(card, "SeedRow", 38f, 10f);
            CreateText(seedRow, "SeedLabel", "STARTING SEED", 14f, GravityDriftUITheme.TextSecondary, fonts, true)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.seedInputField = CreateInputField(seedRow, "SeedInput", sprites, fonts, "101");
            refs.randomizeSeedBtn = CreateButton(seedRow, "RandomizeBtn", "RANDOMIZE", sprites, fonts, false, 36f);
            refs.randomizeSeedBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 140f;

            var lvlRow = CreateHorizontalRow(card, "LevelRow", 38f, 10f);
            refs.levelStepperText = CreateText(lvlRow, "LevelLabel", "STARTING LEVEL  <color=#38BDF8>1</color> / 20", 14f, GravityDriftUITheme.TextPrimary, fonts, true);
            refs.levelStepperText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.levelMinusBtn = CreateButton(lvlRow, "LevelMinus", "-", sprites, fonts, false, 36f);
            refs.levelMinusBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 44f;
            refs.levelPlusBtn = CreateButton(lvlRow, "LevelPlus", "+", sprites, fonts, false, 36f);
            refs.levelPlusBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 44f;
            CreateText(lvlRow, "ScaleNote", "(20-SECTOR RUN)", 12f, GravityDriftUITheme.TextSecondary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 135f;

            var livesRow = CreateHorizontalRow(card, "MainLivesRow", 34f, 10f);
            CreateText(livesRow, "MainLivesLbl", "ROGUELITE LIVES", 14f, GravityDriftUITheme.TextSecondary, fonts, true)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.mainLives3Btn = CreateButton(livesRow, "MainLives3Btn", "3 LIVES (HARD)", sprites, fonts, false, 34f);
            refs.mainLives3Btn.gameObject.GetComponent<LayoutElement>().preferredWidth = 164f;
            refs.mainLives5Btn = CreateButton(livesRow, "MainLives5Btn", "5 LIVES (STANDARD)", sprites, fonts, true, 34f);
            refs.mainLives5Btn.gameObject.GetComponent<LayoutElement>().preferredWidth = 204f;

            var ctrlBox = CreatePanel(card, "ControlsBox", sprites.controlBox, Color.white);
            ctrlBox.gameObject.AddComponent<LayoutElement>().preferredHeight = 128f;
            var cvlg = ctrlBox.gameObject.AddComponent<VerticalLayoutGroup>();
            cvlg.padding = new RectOffset(16, 16, 10, 10);
            cvlg.spacing = 4f;
            cvlg.childControlWidth = true;
            cvlg.childControlHeight = true;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;

            CreateText(ctrlBox, "CtrlHdr", "CONTROLS REFERENCE", 12.5f, GravityDriftUITheme.AccentSkyBlue, fonts, true)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            CreateText(ctrlBox, "C1", "<b>Left Stick / WASD</b>  —  2D Jetpack Thrust (Gravity-Neutral)", 12.5f, GravityDriftUITheme.TextPrimary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            CreateText(ctrlBox, "C2", "<b>R2 Trigger / Shift / Space</b>  —  Jetpack Boost (+Rumble)", 12.5f, GravityDriftUITheme.TextPrimary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            CreateText(ctrlBox, "C3", "<b>Touchpad Swipe / D-Pad / Arrows / 1-4</b>  —  4-Way Crate Gravity Flip", 12.5f, GravityDriftUITheme.TextPrimary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            CreateText(ctrlBox, "C4", "<b>Options / Esc</b> Pause  ·  <b>Create / Tab</b> Debug  ·  <b>C / V</b> Style  ·  <b>N / P</b> Seeds", 11.5f, GravityDriftUITheme.TextSecondary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 17f;

            refs.startMissionBtn = CreateButton(card, "StartMissionBtn", "START MISSION", sprites, fonts, true, 44f);

            var secRow = CreateHorizontalRow(card, "MainSecondaryRow", 38f, 10f);
            refs.mainSettingsBtn = CreateButton(secRow, "SettingsBtn", "SETTINGS", sprites, fonts, false, 38f);
            refs.mainSettingsBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            refs.mainReportBtn = CreateButton(secRow, "MainReportBtn", "REPORT & GRAPHS", sprites, fonts, false, 38f);
            refs.mainReportBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            refs.quitBtn = CreateButton(card, "QuitBtn", "QUIT TO DESKTOP", sprites, fonts, false, 38f);
            return card.gameObject;
        }

        private struct PauseRefs
        {
            public UnityEngine.UI.Button resumeBtn;
            public UnityEngine.UI.Button restartSeedBtn;
            public UnityEngine.UI.Button newSeedBtn;
            public UnityEngine.UI.Button pauseReportBtn;
            public UnityEngine.UI.Button pauseSettingsBtn;
            public UnityEngine.UI.Button returnMainBtn;
        }

        private static GameObject BuildPauseMenu(Transform parent, SpriteSet sprites, FontPair fonts, out PauseRefs refs)
        {
            refs = default;
            var card = CreatePanel(parent, "PauseMenuCard", sprites.panelBox, Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(460f, 434f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 24, 24);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateText(card, "PauseTitle", "MISSION PAUSED", 28f, GravityDriftUITheme.AccentSkyBlue, fonts, true, TextAlignmentOptions.Center)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            CreatePanel(card, "Divider", sprites.solidWhite, GravityDriftUITheme.BorderHairline)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            refs.resumeBtn = CreateButton(card, "ResumeBtn", "RESUME MISSION", sprites, fonts, true, 46f);
            refs.restartSeedBtn = CreateButton(card, "RestartBtn", "RESTART CURRENT SEED", sprites, fonts, false, 42f);
            refs.newSeedBtn = CreateButton(card, "NewSeedBtn", "NEW SEED (N)", sprites, fonts, false, 42f);
            refs.pauseReportBtn = CreateButton(card, "PauseReportBtn", "GAME REPORT & GRAPHS (G)", sprites, fonts, false, 42f);
            refs.pauseSettingsBtn = CreateButton(card, "PauseSettingsBtn", "SETTINGS", sprites, fonts, false, 42f);
            refs.returnMainBtn = CreateButton(card, "ReturnMainBtn", "RETURN TO MAIN MENU", sprites, fonts, false, 42f);
            return card.gameObject;
        }

        private struct SettingsRefs
        {
            public TextMeshProUGUI volumeLabel;
            public UnityEngine.UI.Slider volumeSlider;
            public UnityEngine.UI.Button celBtn;
            public UnityEngine.UI.Button painterBtn;
            public TextMeshProUGUI controllerStatusText;
            public UnityEngine.UI.Button pcSchemeBtn;
            public UnityEngine.UI.Button dsSchemeBtn;
            public UnityEngine.UI.Toggle hapticsToggle;
            public TextMeshProUGUI hapticsLabel;
            public UnityEngine.UI.Slider hapticsSlider;
            public UnityEngine.UI.Toggle rStickToggle;
            public TextMeshProUGUI deadzoneLabel;
            public UnityEngine.UI.Slider deadzoneSlider;
            public UnityEngine.UI.Button setLives3Btn;
            public UnityEngine.UI.Button setLives5Btn;
            public TextMeshProUGUI displayModeLabel;
            public UnityEngine.UI.Button toggleDisplayBtn;
            public UnityEngine.UI.Button settingsBackBtn;
        }

        private static GameObject BuildSettingsMenu(Transform parent, SpriteSet sprites, FontPair fonts, out SettingsRefs refs)
        {
            refs = default;
            var card = CreatePanel(parent, "SettingsMenuCard", sprites.panelBox, Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(620f, 602f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 22, 22);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateText(card, "SettingsTitle", "SYSTEM SETTINGS", 28f, GravityDriftUITheme.AccentSkyBlue, fonts, true, TextAlignmentOptions.Center)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            CreatePanel(card, "Divider", sprites.solidWhite, GravityDriftUITheme.BorderHairline)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            var volRow = CreateHorizontalRow(card, "VolRow", 32f, 12f);
            refs.volumeLabel = CreateText(volRow, "VolLbl", "MASTER VOLUME  100%", 14f, GravityDriftUITheme.TextPrimary, fonts);
            refs.volumeLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            refs.volumeSlider = CreateSlider(volRow, "VolSlider", sprites, 0f, 1f, 1f);

            var styleRow = CreateHorizontalRow(card, "StyleRow", 38f, 10f);
            CreateText(styleRow, "StyleLbl", "SHADER STYLE (C / V)", 14f, GravityDriftUITheme.TextPrimary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            refs.celBtn = CreateButton(styleRow, "CelBtn", "CEL-SHADED", sprites, fonts, true, 36f);
            refs.celBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            refs.painterBtn = CreateButton(styleRow, "PainterBtn", "PAINTERLY", sprites, fonts, false, 36f);
            refs.painterBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            refs.controllerStatusText = CreateText(card, "CtrlStatus", "CONTROLLER STATUS", 13f, GravityDriftUITheme.TextSecondary, fonts);
            refs.controllerStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var schemeRow = CreateHorizontalRow(card, "SchemeRow", 38f, 10f);
            refs.pcSchemeBtn = CreateButton(schemeRow, "PcBtn", "PC KEYBOARD / MOUSE", sprites, fonts, true, 36f);
            refs.pcSchemeBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            refs.dsSchemeBtn = CreateButton(schemeRow, "DsBtn", "PLAYSTATION DUALSENSE", sprites, fonts, false, 36f);
            refs.dsSchemeBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            refs.hapticsToggle = CreateToggle(card, "HapticsToggle", "Controller Rumble / DualSense Haptics & Lightbar Sync", sprites, fonts, true);

            var hapRow = CreateHorizontalRow(card, "HapticsRow", 32f, 12f);
            refs.hapticsLabel = CreateText(hapRow, "HapLbl", "HAPTICS INTENSITY  1.00X", 14f, GravityDriftUITheme.TextPrimary, fonts);
            refs.hapticsLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            refs.hapticsSlider = CreateSlider(hapRow, "HapSlider", sprites, 0f, 1.5f, 1f);

            refs.rStickToggle = CreateToggle(card, "RStickToggle", "Enable Right-Stick Gravity Flick Fallback (with Touchpad & D-Pad)", sprites, fonts, true);

            var dzRow = CreateHorizontalRow(card, "DeadzoneRow", 32f, 12f);
            refs.deadzoneLabel = CreateText(dzRow, "DzLbl", "L-STICK DEADZONE  0.16", 14f, GravityDriftUITheme.TextPrimary, fonts);
            refs.deadzoneLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            refs.deadzoneSlider = CreateSlider(dzRow, "DzSlider", sprites, 0.05f, 0.35f, 0.16f);

            var setLivesRow = CreateHorizontalRow(card, "SetLivesRow", 36f, 10f);
            CreateText(setLivesRow, "SetLivesLbl", "RUN LIVES", 14f, GravityDriftUITheme.TextPrimary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            refs.setLives3Btn = CreateButton(setLivesRow, "SetLives3Btn", "3 LIVES", sprites, fonts, false, 34f);
            refs.setLives3Btn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            refs.setLives5Btn = CreateButton(setLivesRow, "SetLives5Btn", "5 LIVES (DEFAULT)", sprites, fonts, true, 34f);
            refs.setLives5Btn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var dispRow = CreateHorizontalRow(card, "DisplayRow", 38f, 10f);
            refs.displayModeLabel = CreateText(dispRow, "DispLbl", "DISPLAY MODE", 14f, GravityDriftUITheme.TextPrimary, fonts);
            refs.displayModeLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            refs.toggleDisplayBtn = CreateButton(dispRow, "ToggleDispBtn", "TOGGLE FULLSCREEN", sprites, fonts, false, 36f);
            refs.toggleDisplayBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            refs.settingsBackBtn = CreateButton(card, "SettingsBackBtn", "BACK", sprites, fonts, true, 44f);
            return card.gameObject;
        }

        private struct ReportRefs
        {
            public TextMeshProUGUI reportKpiText;
            public TextMeshProUGUI reportSaveStatusText;
            public RawImage levelsChartImage;
            public TextMeshProUGUI levelsChartFooterText;
            public RawImage aiChartImage;
            public TextMeshProUGUI aiChartFooterText;
            public RawImage deathChartImage;
            public TextMeshProUGUI deathChartFooterText;
            public UnityEngine.UI.Button saveReportBtn;
            public UnityEngine.UI.Button reportBackBtn;
        }

        private static GameObject BuildSessionReport(Transform parent, SpriteSet sprites, FontPair fonts, out ReportRefs refs)
        {
            refs = default;
            var card = CreatePanel(parent, "SessionReportCard", sprites.panelBox, Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1660f, 840f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(28, 28, 22, 22);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdrRow = CreateHorizontalRow(card, "ReportHeaderRow", 38f, 12f);
            CreateText(hdrRow, "ReportTitle", "MISSION TELEMETRY & AI REPORT", 28f, GravityDriftUITheme.AccentSkyBlue, fonts, true)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            CreateText(hdrRow, "ReportCloseHint", "LIVE BAYESIAN POSTERIOR (π) · PER-LEVEL & PER-INPUT ANALYTICS  [G / ESC]", 12.5f, GravityDriftUITheme.TextSecondary, fonts, true, TextAlignmentOptions.Right)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 600f;

            var kpiPanel = CreatePanel(card, "ReportKpiStrip", sprites.controlBox, Color.white);
            kpiPanel.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            refs.reportKpiText = CreateText(kpiPanel, "ReportKpiText", "NO SESSION RECORDED YET — CLICK START MISSION TO COLLECT LIVE DATA", 14f, GravityDriftUITheme.TextPrimary, fonts, true, TextAlignmentOptions.Center);
            GravityDriftUITheme.StretchFill(refs.reportKpiText.rectTransform, 14f, 6f);

            var graphsRow = CreateHorizontalRow(card, "GraphsRow", 590f, 16f);
            graphsRow.gameObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            BuildReportGraphCard(graphsRow, "LevelsGraphCard", "1. LEVELS PLAYED & PROGRESSION",
                "<color=#22C55E>■</color> CLEARED   <color=#EF4444>■</color> DEATH   <color=#00E5FF>●</color> TIME (S)   <color=#F59E0B>●</color> COMPLEXITY",
                sprites, fonts, out refs.levelsChartImage, out refs.levelsChartFooterText);

            BuildReportGraphCard(graphsRow, "AiInputGraphCard", "2. AI IMPROVEMENT PER INPUT",
                "<color=#38BDF8>●</color> POSTERIOR π   <color=#F59E0B>●</color> ENTROPY PRECISION   <color=#22C55E>●</color> AI IMP %",
                sprites, fonts, out refs.aiChartImage, out refs.aiChartFooterText);

            BuildReportGraphCard(graphsRow, "DeathRateGraphCard", "3. DEATH RATE & IMPROVEMENT",
                "<color=#EF4444>●</color> DEATH RATE %   <color=#38BDF8>●</color> AI IMP %   <color=#22C55E>●</color> DODGE SKILL %",
                sprites, fonts, out refs.deathChartImage, out refs.deathChartFooterText);

            var footRow = CreateHorizontalRow(card, "ReportFooterRow", 44f, 12f);
            refs.reportSaveStatusText = CreateText(footRow, "SaveStatus", "Auto-saves on Clear, Death, Reset, Menu Return & Quit", 13f, GravityDriftUITheme.TextSecondary, fonts);
            refs.reportSaveStatusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            refs.saveReportBtn = CreateButton(footRow, "SaveReportBtn", "SAVE REPORT (CSV + JSON)", sprites, fonts, true, 42f);
            refs.saveReportBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 310f;

            refs.reportBackBtn = CreateButton(footRow, "ReportBackBtn", "BACK", sprites, fonts, false, 42f);
            refs.reportBackBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 140f;

            return card.gameObject;
        }

        private static void BuildReportGraphCard(
            Transform parent,
            string name,
            string titleStr,
            string legendStr,
            SpriteSet sprites,
            FontPair fonts,
            out RawImage rawImg,
            out TextMeshProUGUI footerTxt)
        {
            var panel = CreatePanel(parent, name, sprites.controlBox, Color.white);
            panel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 12);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateText(panel, "CardTitle", titleStr, 15.5f, GravityDriftUITheme.AccentSkyBlue, fonts, true)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            CreateText(panel, "CardLegend", legendStr, 11.5f, GravityDriftUITheme.TextSecondary, fonts)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var imgGo = new GameObject("PlotRawImage", typeof(RectTransform), typeof(RawImage));
            imgGo.transform.SetParent(panel, false);
            var imgLe = imgGo.AddComponent<LayoutElement>();
            imgLe.flexibleHeight = 1f;
            imgLe.minHeight = 360f;
            rawImg = imgGo.GetComponent<RawImage>();
            rawImg.color = Color.white;
            rawImg.raycastTarget = false;

            footerTxt = CreateText(panel, "CardFooter", "Waiting for gameplay samples...", 12.5f, GravityDriftUITheme.TextPrimary, fonts);
            footerTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        }

        private struct RunOverRefs
        {
            public TextMeshProUGUI runOverTitleText;
            public TextMeshProUGUI runOverSubText;
            public TextMeshProUGUI runOverStatsText;
            public UnityEngine.UI.Button runOverRetryBtn;
            public UnityEngine.UI.Button runOverReportBtn;
            public UnityEngine.UI.Button runOverMenuBtn;
        }

        private static GameObject BuildRunOver(Transform parent, SpriteSet sprites, FontPair fonts, out RunOverRefs refs)
        {
            refs = default;
            var card = CreatePanel(parent, "RunOverCard", sprites.panelBox, Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(560f, 410f);

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 26, 26);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            refs.runOverTitleText = CreateText(card, "RunOverTitle", "RUN TERMINATED", 32f, GravityDriftUITheme.DangerRed, fonts, true, TextAlignmentOptions.Center);
            refs.runOverTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            refs.runOverSubText = CreateText(card, "RunOverSub", "0 / 5 LIVES REMAINING", 14f, GravityDriftUITheme.TextSecondary, fonts, true, TextAlignmentOptions.Center);
            refs.runOverSubText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            CreatePanel(card, "Divider", sprites.solidWhite, GravityDriftUITheme.BorderHairline)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            var statsPanel = CreatePanel(card, "RunOverStatsBox", sprites.controlBox, Color.white);
            statsPanel.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;
            refs.runOverStatsText = CreateText(statsPanel, "RunOverStatsTxt", "", 13.5f, GravityDriftUITheme.TextPrimary, fonts, false, TextAlignmentOptions.Center);
            GravityDriftUITheme.StretchFill(refs.runOverStatsText.rectTransform, 16f, 12f);

            refs.runOverRetryBtn = CreateButton(card, "RetryRunBtn", "RETRY RUN", sprites, fonts, true, 44f);
            refs.runOverReportBtn = CreateButton(card, "RunOverReportBtn", "VIEW REPORT & GRAPHS", sprites, fonts, false, 40f);
            refs.runOverMenuBtn = CreateButton(card, "RunOverMenuBtn", "MAIN MENU", sprites, fonts, false, 40f);
            return card.gameObject;
        }

        private struct HudRefs
        {
            public TextMeshProUGUI levelBadgeText;
            public TextMeshProUGUI livesBadgeText;
            public TextMeshProUGUI switchStatusText;
            public TextMeshProUGUI airlockStatusText;
            public GameObject coresRowObj;
            public TextMeshProUGUI coresValueText;
            public TextMeshProUGUI timerActionText;
            public GameObject alertBannerObj;
            public TextMeshProUGUI alertBannerText;
            public TextMeshProUGUI droneHeaderText;
            public TextMeshProUGUI[] droneRowTexts;
            public TextMeshProUGUI droneLearnText;
            public TextMeshProUGUI jetpackLabelText;
            public TextMeshProUGUI jetpackPctText;
            public RectTransform jetpackFillRt;
            public UnityEngine.UI.Image jetpackFillImg;
            public RectTransform gravityArrowRt;
            public UnityEngine.UI.Image gravityArrowImg;
            public TextMeshProUGUI gravityLabelText;
        }

        private static GameObject BuildGameplayHUD(Transform parent, SpriteSet sprites, FontPair fonts, out HudRefs refs)
        {
            refs = default;
            var hudGo = new GameObject("GameplayHUD", typeof(RectTransform));
            hudGo.transform.SetParent(parent, false);
            var hudRt = hudGo.GetComponent<RectTransform>();
            GravityDriftUITheme.StretchFill(hudRt);

            // 1. TOP-LEFT PANEL
            var tl = CreatePanel(hudRt, "TopLeftPanel", sprites.panelBox, Color.white);
            tl.anchorMin = new Vector2(0f, 1f);
            tl.anchorMax = new Vector2(0f, 1f);
            tl.pivot = new Vector2(0f, 1f);
            tl.anchoredPosition = new Vector2(20f, -18f);
            tl.sizeDelta = new Vector2(468f, 198f);

            var tlVlg = tl.gameObject.AddComponent<VerticalLayoutGroup>();
            tlVlg.padding = new RectOffset(16, 16, 12, 12);
            tlVlg.spacing = 4f;
            tlVlg.childControlWidth = true;
            tlVlg.childControlHeight = true;
            tlVlg.childForceExpandWidth = true;
            tlVlg.childForceExpandHeight = false;

            refs.levelBadgeText = CreateText(tl, "LevelBadge", "LVL 01 / 20  ·  SEED #101", 16.5f, GravityDriftUITheme.AccentSkyBlue, fonts, true);
            refs.levelBadgeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 23f;

            refs.livesBadgeText = CreateText(tl, "LivesBadge", "LIVES  5 / 5", 13.5f, GravityDriftUITheme.SuccessMint, fonts, true);
            refs.livesBadgeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            refs.switchStatusText = CreateText(tl, "SwitchStatus", "SWITCH  LOCKED", 13f, GravityDriftUITheme.TextSecondary, fonts);
            refs.switchStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            refs.airlockStatusText = CreateText(tl, "AirlockStatus", "AIRLOCK  LOCKED", 13f, GravityDriftUITheme.TextSecondary, fonts);
            refs.airlockStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            refs.coresRowObj = new GameObject("CoresRow", typeof(RectTransform));
            refs.coresRowObj.transform.SetParent(tl, false);
            refs.coresRowObj.AddComponent<LayoutElement>().preferredHeight = 19f;
            refs.coresValueText = CreateText(refs.coresRowObj.transform, "CoresText", "CORES  0 / 0", 13f, GravityDriftUITheme.TextPrimary, fonts);
            GravityDriftUITheme.StretchFill(refs.coresValueText.rectTransform);

            refs.timerActionText = CreateText(tl, "TimerAction", "TIME  0.0s  ·  IDLE", 13f, GravityDriftUITheme.TextSecondary, fonts);
            refs.timerActionText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            var alertRt = CreatePanel(tl, "AlertBanner", sprites.dangerBox, Color.white);
            alertRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            refs.alertBannerObj = alertRt.gameObject;
            refs.alertBannerText = CreateText(alertRt, "AlertText", "HAZARD SHOCK! -25% FUEL", 13f, GravityDriftUITheme.DangerRed, fonts, true, TextAlignmentOptions.Center);
            GravityDriftUITheme.StretchFill(refs.alertBannerText.rectTransform, 6f, 2f);
            refs.alertBannerObj.SetActive(false);

            // 2. TOP-RIGHT PANEL
            var tr = CreatePanel(hudRt, "TopRightPanel", sprites.panelBox, Color.white);
            tr.anchorMin = new Vector2(1f, 1f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(1f, 1f);
            tr.anchoredPosition = new Vector2(-20f, -18f);
            tr.sizeDelta = new Vector2(365f, 194f);

            var trVlg = tr.gameObject.AddComponent<VerticalLayoutGroup>();
            trVlg.padding = new RectOffset(16, 16, 12, 12);
            trVlg.spacing = 4f;
            trVlg.childControlWidth = true;
            trVlg.childControlHeight = true;
            trVlg.childForceExpandWidth = true;
            trVlg.childForceExpandHeight = false;

            refs.droneHeaderText = CreateText(tr, "DroneHeader", "DRONE THREAT MONITOR", 14f, GravityDriftUITheme.AccentSkyBlue, fonts, true);
            refs.droneHeaderText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            refs.droneRowTexts = new TextMeshProUGUI[5];
            for (int d = 0; d < 5; d++)
            {
                var dr = CreateText(tr, $"DroneRow_{d + 1}", $"D{d + 1}  PATROL", 12.5f, GravityDriftUITheme.TextPrimary, fonts);
                dr.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
                refs.droneRowTexts[d] = dr;
            }

            refs.droneLearnText = CreateText(tr, "DroneLearn", "ESCAPES 0  ·  DODGE 0%  ·  IMP +0%", 11.5f, GravityDriftUITheme.TextSecondary, fonts);
            refs.droneLearnText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            // 3. BOTTOM TACTICAL STRIP
            var bot = CreatePanel(hudRt, "BottomTacticalStrip", sprites.panelBox, Color.white);
            bot.anchorMin = new Vector2(0.5f, 0f);
            bot.anchorMax = new Vector2(0.5f, 0f);
            bot.pivot = new Vector2(0.5f, 0f);
            bot.anchoredPosition = new Vector2(0f, 18f);
            bot.sizeDelta = new Vector2(620f, 68f);

            var botHlg = bot.gameObject.AddComponent<HorizontalLayoutGroup>();
            botHlg.padding = new RectOffset(18, 18, 10, 10);
            botHlg.spacing = 18f;
            botHlg.childControlWidth = true;
            botHlg.childControlHeight = true;
            botHlg.childForceExpandWidth = false;

            var fuelCol = new GameObject("JetpackCol", typeof(RectTransform)).GetComponent<RectTransform>();
            fuelCol.SetParent(bot, false);
            fuelCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var fuelVlg = fuelCol.gameObject.AddComponent<VerticalLayoutGroup>();
            fuelVlg.spacing = 6f;
            fuelVlg.childControlWidth = true;
            fuelVlg.childControlHeight = true;
            fuelVlg.childForceExpandWidth = true;
            fuelVlg.childForceExpandHeight = false;

            var fuelTop = CreateHorizontalRow(fuelCol, "FuelHeader", 20f, 8f);
            refs.jetpackLabelText = CreateText(fuelTop, "JetLabel", "JETPACK FUEL", 13f, GravityDriftUITheme.TextPrimary, fonts, true);
            refs.jetpackLabelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.jetpackPctText = CreateText(fuelTop, "JetPct", "100%", 14f, GravityDriftUITheme.AccentSkyBlue, fonts, true, TextAlignmentOptions.Right);
            refs.jetpackPctText.gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;

            var barBg = CreatePanel(fuelCol, "FuelBarBg", sprites.controlBox, Color.white);
            barBg.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            refs.jetpackFillRt = CreatePanel(barBg, "FuelBarFill", sprites.solidWhite, GravityDriftUITheme.AccentSkyBlue);
            GravityDriftUITheme.StretchFill(refs.jetpackFillRt, 2f, 2f);
            refs.jetpackFillImg = refs.jetpackFillRt.GetComponent<UnityEngine.UI.Image>();

            CreatePanel(bot, "Sep", sprites.solidWhite, GravityDriftUITheme.BorderHairline)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 1f;

            var gravCol = CreateHorizontalRow(bot, "GravityVectorBox", 48f, 10f);
            gravCol.gameObject.GetComponent<LayoutElement>().preferredWidth = 185f;
            gravCol.gameObject.GetComponent<LayoutElement>().flexibleWidth = 0f;

            var arrowHolder = CreatePanel(gravCol, "ArrowHolder", sprites.controlBox, Color.white);
            var ahLe = arrowHolder.gameObject.AddComponent<LayoutElement>();
            ahLe.preferredWidth = 42f;
            ahLe.preferredHeight = 42f;

            refs.gravityArrowRt = CreatePanel(arrowHolder, "GravityArrow", sprites.gravityArrow, GravityDriftUITheme.AccentSkyBlue);
            refs.gravityArrowRt.anchorMin = new Vector2(0.5f, 0.5f);
            refs.gravityArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
            refs.gravityArrowRt.sizeDelta = new Vector2(26f, 26f);
            refs.gravityArrowImg = refs.gravityArrowRt.GetComponent<UnityEngine.UI.Image>();

            refs.gravityLabelText = CreateText(gravCol, "GravText", "GRAV: DOWN\nCHARGES 3/3", 12f, GravityDriftUITheme.TextPrimary, fonts, true);
            refs.gravityLabelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            return hudGo;
        }

        private static void WireSceneManagersFromRoots(
            SpriteSet sprites,
            GameObject uiCanvasGo,
            GameObject debugCanvasGo,
            GameObject fadeCanvasGo)
        {
            Transform u = uiCanvasGo.transform;
            var uiMgr = Object.FindAnyObjectByType<GameUIManager>(FindObjectsInactive.Include);
            if (uiMgr != null)
            {
                var so = new SerializedObject(uiMgr);
                so.FindProperty("activeSelectionSprite").objectReferenceValue = sprites.accentBox;
                so.FindProperty("inactiveSelectionSprite").objectReferenceValue = sprites.controlBox;

                so.FindProperty("uiCanvas").objectReferenceValue = uiCanvasGo.GetComponent<Canvas>();
                so.FindProperty("scrimPanel").objectReferenceValue = FindChildGo(u, "BackdropScrim");
                so.FindProperty("scrimImage").objectReferenceValue = FindChildComp<UnityEngine.UI.Image>(u, "BackdropScrim");
                so.FindProperty("mainMenuRoot").objectReferenceValue = FindChildGo(u, "MainMenuCard");
                so.FindProperty("pauseMenuRoot").objectReferenceValue = FindChildGo(u, "PauseMenuCard");
                so.FindProperty("settingsMenuRoot").objectReferenceValue = FindChildGo(u, "SettingsMenuCard");
                so.FindProperty("sessionReportRoot").objectReferenceValue = FindChildGo(u, "SessionReportCard");
                so.FindProperty("runOverRoot").objectReferenceValue = FindChildGo(u, "RunOverCard");
                so.FindProperty("gameplayHudRoot").objectReferenceValue = FindChildGo(u, "GameplayHUD");

                // Run Over
                so.FindProperty("runOverTitleText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "RunOverCard/RunOverTitle");
                so.FindProperty("runOverSubText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "RunOverCard/RunOverSub");
                so.FindProperty("runOverStatsText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "RunOverCard/RunOverStatsBox/RunOverStatsTxt");
                so.FindProperty("runOverRetryBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "RunOverCard/RetryRunBtn");
                so.FindProperty("runOverReportBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "RunOverCard/RunOverReportBtn");
                so.FindProperty("runOverMenuBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "RunOverCard/RunOverMenuBtn");

                // Session Report
                so.FindProperty("reportKpiText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SessionReportCard/ReportKpiStrip/ReportKpiText");
                so.FindProperty("reportSaveStatusText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SessionReportCard/ReportFooterRow/SaveStatus");
                so.FindProperty("levelsChartImage").objectReferenceValue = FindChildComp<RawImage>(u, "SessionReportCard/GraphsRow/LevelsGraphCard/PlotRawImage");
                so.FindProperty("levelsChartFooterText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SessionReportCard/GraphsRow/LevelsGraphCard/CardFooter");
                so.FindProperty("aiChartImage").objectReferenceValue = FindChildComp<RawImage>(u, "SessionReportCard/GraphsRow/AiInputGraphCard/PlotRawImage");
                so.FindProperty("aiChartFooterText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SessionReportCard/GraphsRow/AiInputGraphCard/CardFooter");
                so.FindProperty("deathChartImage").objectReferenceValue = FindChildComp<RawImage>(u, "SessionReportCard/GraphsRow/DeathRateGraphCard/PlotRawImage");
                so.FindProperty("deathChartFooterText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SessionReportCard/GraphsRow/DeathRateGraphCard/CardFooter");
                so.FindProperty("saveReportBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SessionReportCard/ReportFooterRow/SaveReportBtn");
                so.FindProperty("reportBackBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SessionReportCard/ReportFooterRow/ReportBackBtn");

                // Main Menu
                so.FindProperty("seedInputField").objectReferenceValue = FindChildComp<TMP_InputField>(u, "MainMenuCard/SeedRow/SeedInput");
                so.FindProperty("randomizeSeedBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/SeedRow/RandomizeBtn");
                so.FindProperty("levelStepperText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "MainMenuCard/LevelRow/LevelLabel");
                so.FindProperty("levelMinusBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/LevelRow/LevelMinus");
                so.FindProperty("levelPlusBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/LevelRow/LevelPlus");
                so.FindProperty("mainLives3Btn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/MainLivesRow/MainLives3Btn");
                so.FindProperty("mainLives5Btn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/MainLivesRow/MainLives5Btn");
                so.FindProperty("startMissionBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/StartMissionBtn");
                so.FindProperty("mainSettingsBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/MainSecondaryRow/SettingsBtn");
                so.FindProperty("mainReportBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/MainSecondaryRow/MainReportBtn");
                so.FindProperty("quitBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "MainMenuCard/QuitBtn");

                // Pause Menu
                so.FindProperty("resumeBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "PauseMenuCard/ResumeBtn");
                so.FindProperty("restartSeedBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "PauseMenuCard/RestartBtn");
                so.FindProperty("newSeedBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "PauseMenuCard/NewSeedBtn");
                so.FindProperty("pauseReportBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "PauseMenuCard/PauseReportBtn");
                so.FindProperty("pauseSettingsBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "PauseMenuCard/PauseSettingsBtn");
                so.FindProperty("returnMainBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "PauseMenuCard/ReturnMainBtn");

                // Settings Menu
                so.FindProperty("volumeLabel").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SettingsMenuCard/VolRow/VolLbl");
                so.FindProperty("volumeSlider").objectReferenceValue = FindChildComp<UnityEngine.UI.Slider>(u, "SettingsMenuCard/VolRow/VolSlider");
                so.FindProperty("celBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/StyleRow/CelBtn");
                so.FindProperty("painterBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/StyleRow/PainterBtn");
                so.FindProperty("controllerStatusText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SettingsMenuCard/CtrlStatus");
                so.FindProperty("pcSchemeBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/SchemeRow/PcBtn");
                so.FindProperty("dsSchemeBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/SchemeRow/DsBtn");
                so.FindProperty("hapticsToggle").objectReferenceValue = FindChildComp<UnityEngine.UI.Toggle>(u, "SettingsMenuCard/HapticsToggle");
                so.FindProperty("hapticsLabel").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SettingsMenuCard/HapticsRow/HapLbl");
                so.FindProperty("hapticsSlider").objectReferenceValue = FindChildComp<UnityEngine.UI.Slider>(u, "SettingsMenuCard/HapticsRow/HapSlider");
                so.FindProperty("rStickToggle").objectReferenceValue = FindChildComp<UnityEngine.UI.Toggle>(u, "SettingsMenuCard/RStickToggle");
                so.FindProperty("deadzoneLabel").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SettingsMenuCard/DeadzoneRow/DzLbl");
                so.FindProperty("deadzoneSlider").objectReferenceValue = FindChildComp<UnityEngine.UI.Slider>(u, "SettingsMenuCard/DeadzoneRow/DzSlider");
                so.FindProperty("setLives3Btn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/SetLivesRow/SetLives3Btn");
                so.FindProperty("setLives5Btn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/SetLivesRow/SetLives5Btn");
                so.FindProperty("displayModeLabel").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "SettingsMenuCard/DisplayRow/DispLbl");
                so.FindProperty("toggleDisplayBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/DisplayRow/ToggleDispBtn");
                so.FindProperty("settingsBackBtn").objectReferenceValue = FindChildComp<UnityEngine.UI.Button>(u, "SettingsMenuCard/SettingsBackBtn");

                // Gameplay HUD
                so.FindProperty("levelBadgeText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/LevelBadge");
                so.FindProperty("livesBadgeText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/LivesBadge");
                so.FindProperty("switchStatusText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/SwitchStatus");
                so.FindProperty("airlockStatusText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/AirlockStatus");
                so.FindProperty("coresRowObj").objectReferenceValue = FindChildGo(u, "GameplayHUD/TopLeftPanel/CoresRow");
                so.FindProperty("coresValueText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/CoresRow/CoresText");
                so.FindProperty("timerActionText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/TimerAction");
                so.FindProperty("alertBannerObj").objectReferenceValue = FindChildGo(u, "GameplayHUD/TopLeftPanel/AlertBanner");
                so.FindProperty("alertBannerText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopLeftPanel/AlertBanner/AlertText");
                so.FindProperty("droneHeaderText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopRightPanel/DroneHeader");

                var droneRowsProp = so.FindProperty("droneRowTexts");
                droneRowsProp.arraySize = 5;
                for (int i = 0; i < 5; i++)
                {
                    droneRowsProp.GetArrayElementAtIndex(i).objectReferenceValue =
                        FindChildComp<TextMeshProUGUI>(u, $"GameplayHUD/TopRightPanel/DroneRow_{i + 1}");
                }
                so.FindProperty("droneLearnText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/TopRightPanel/DroneLearn");

                so.FindProperty("jetpackLabelText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/BottomTacticalStrip/JetpackCol/FuelHeader/JetLabel");
                so.FindProperty("jetpackPctText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/BottomTacticalStrip/JetpackCol/FuelHeader/JetPct");
                so.FindProperty("jetpackFillRt").objectReferenceValue = FindChildComp<RectTransform>(u, "GameplayHUD/BottomTacticalStrip/JetpackCol/FuelBarBg/FuelBarFill");
                so.FindProperty("jetpackFillImg").objectReferenceValue = FindChildComp<UnityEngine.UI.Image>(u, "GameplayHUD/BottomTacticalStrip/JetpackCol/FuelBarBg/FuelBarFill");
                so.FindProperty("gravityArrowRt").objectReferenceValue = FindChildComp<RectTransform>(u, "GameplayHUD/BottomTacticalStrip/GravityVectorBox/ArrowHolder/GravityArrow");
                so.FindProperty("gravityArrowImg").objectReferenceValue = FindChildComp<UnityEngine.UI.Image>(u, "GameplayHUD/BottomTacticalStrip/GravityVectorBox/ArrowHolder/GravityArrow");
                so.FindProperty("gravityLabelText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(u, "GameplayHUD/BottomTacticalStrip/GravityVectorBox/GravText");

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(uiMgr);
            }

            var dbg = Object.FindAnyObjectByType<DebugOverlay>(FindObjectsInactive.Include);
            if (dbg != null)
            {
                var so = new SerializedObject(dbg);
                so.FindProperty("debugCanvas").objectReferenceValue = debugCanvasGo.GetComponent<Canvas>();
                so.FindProperty("panelRoot").objectReferenceValue = FindChildGo(debugCanvasGo.transform, "DebugTelemetryCard");
                so.FindProperty("bodyText").objectReferenceValue = FindChildComp<TextMeshProUGUI>(debugCanvasGo.transform, "DebugTelemetryCard/DebugBody");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(dbg);
            }

            var boot = Object.FindAnyObjectByType<GravityDriftBootstrap>(FindObjectsInactive.Include);
            if (boot != null)
            {
                var so = new SerializedObject(boot);
                so.FindProperty("fadeCanvas").objectReferenceValue = fadeCanvasGo.GetComponent<Canvas>();
                so.FindProperty("fadeOverlayImg").objectReferenceValue = FindChildComp<UnityEngine.UI.Image>(fadeCanvasGo.transform, "FadeOverlay");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(boot);
            }
        }
    }
}
