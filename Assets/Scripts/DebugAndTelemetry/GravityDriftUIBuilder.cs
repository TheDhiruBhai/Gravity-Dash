using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GravityDrift
{
    /// <summary>
    /// Simple high-contrast Black & White UI theme tokens & fallback runtime uGUI helpers.
    /// Plain black backgrounds (#000000), crisp white text (#FFFFFF), and simple plain borders.
    /// </summary>
    public static class GravityDriftUITheme
    {
        // Plain Black & White Theme
        public static readonly Color BackdropScrim  = new Color32(0, 0, 0, 255);     // #000000
        public static readonly Color PauseScrim     = new Color32(0, 0, 0, 210);     // #000000
        public static readonly Color PanelSurface   = new Color32(0, 0, 0, 255);     // #000000
        public static readonly Color ControlSurface = new Color32(0, 0, 0, 255);     // #000000
        public static readonly Color BorderHairline = new Color32(140, 140, 140, 255);
        public static readonly Color TextPrimary    = new Color32(255, 255, 255, 255); // #FFFFFF
        public static readonly Color TextSecondary  = new Color32(210, 210, 210, 255);

        // Selected / Active Control Highlight (Plain B&W)
        public static readonly Color AccentSkyBlue  = new Color32(255, 255, 255, 255); // #FFFFFF
        public static readonly Color AccentCyan     = new Color32(255, 255, 255, 255); // #FFFFFF
        public static readonly Color AccentTintFill = new Color32(24, 24, 24, 255);    // Subtle dark fill + crisp white border

        // Plain Neutral Fallbacks
        public static readonly Color DangerRed      = new Color32(255, 255, 255, 255);
        public static readonly Color DangerTintFill = new Color32(0, 0, 0, 255);
        public static readonly Color SuccessMint    = new Color32(255, 255, 255, 255);
        public static readonly Color WarningAmber   = new Color32(255, 255, 255, 255);

        private static TMP_FontAsset cachedDisplayFont;
        private static TMP_FontAsset cachedBodyFont;
        private static Sprite cachedSolidWhite;
        private static Sprite cachedPanelBox;
        private static Sprite cachedAccentBox;
        private static Sprite cachedDangerBox;
        private static Sprite cachedControlBox;
        private static Sprite cachedArrowSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            cachedDisplayFont = null;
            cachedBodyFont = null;
            cachedSolidWhite = null;
            cachedPanelBox = null;
            cachedAccentBox = null;
            cachedDangerBox = null;
            cachedControlBox = null;
            cachedArrowSprite = null;
        }

        public static void ResolveFonts(out TMP_FontAsset displayFont, out TMP_FontAsset bodyFont)
        {
            if (cachedDisplayFont == null || cachedBodyFont == null)
            {
                var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                TMP_FontAsset robotoFallback = null;
                for (int i = 0; i < loadedFonts.Length; i++)
                {
                    if (loadedFonts[i] == null) continue;
                    string n = loadedFonts[i].name.ToLowerInvariant();
                    if (n.Contains("aerodome")) cachedDisplayFont = loadedFonts[i];
                    else if (n.Contains("roboto")) robotoFallback = loadedFonts[i];
                    else if (n.Contains("mono") || n.Contains("consol") || n.Contains("tabular")) cachedBodyFont = loadedFonts[i];
                }

                if (cachedDisplayFont == null)
                {
                    cachedDisplayFont = Resources.Load<TMP_FontAsset>("Fonts/Aerodome SDF")
                                     ?? Resources.Load<TMP_FontAsset>("Aerodome SDF")
                                     ?? robotoFallback
                                     ?? TMP_Settings.defaultFontAsset;
                }
                if (cachedBodyFont == null)
                {
                    cachedBodyFont = TMP_Settings.defaultFontAsset ?? cachedDisplayFont;
                }
            }

            displayFont = cachedDisplayFont;
            bodyFont = cachedBodyFont;
        }

        public static Sprite GetSolidSprite()
        {
            if (cachedSolidWhite != null && cachedSolidWhite.texture != null) return cachedSolidWhite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var cols = new Color32[16];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(cols);
            tex.Apply();
            cachedSolidWhite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            cachedSolidWhite.hideFlags = HideFlags.HideAndDontSave;
            return cachedSolidWhite;
        }

        public static Sprite CreateBorderedSprite(Color32 fill, Color32 border)
        {
            const int S = 24;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
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
            var sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
            sp.hideFlags = HideFlags.HideAndDontSave;
            return sp;
        }

        // NOTE: Always use explicit Unity == null checks (NEVER ??=) because Unity destroys cached assets across Play/Edit transitions
        public static Sprite GetPanelSprite()
        {
            if (cachedPanelBox == null || cachedPanelBox.texture == null)
                cachedPanelBox = CreateBorderedSprite(PanelSurface, BorderHairline);
            return cachedPanelBox;
        }

        public static Sprite GetControlSprite()
        {
            if (cachedControlBox == null || cachedControlBox.texture == null)
                cachedControlBox = CreateBorderedSprite(ControlSurface, BorderHairline);
            return cachedControlBox;
        }

        public static Sprite GetAccentSprite()
        {
            if (cachedAccentBox == null || cachedAccentBox.texture == null)
                cachedAccentBox = CreateBorderedSprite(AccentTintFill, AccentSkyBlue);
            return cachedAccentBox;
        }

        public static Sprite GetDangerSprite()
        {
            if (cachedDangerBox == null || cachedDangerBox.texture == null)
                cachedDangerBox = CreateBorderedSprite(DangerTintFill, DangerRed);
            return cachedDangerBox;
        }

        /// <summary>
        /// Crisp procedural directional vector arrow pointing UP (+Y) at 0 degrees rotation.
        /// Rotate Z = 0 (Up), 180 (Down), 90 (Left), -90 (Right).
        /// </summary>
        public static Sprite GetGravityArrowSprite()
        {
            if (cachedArrowSprite != null && cachedArrowSprite.texture != null) return cachedArrowSprite;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
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
            cachedArrowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            cachedArrowSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedArrowSprite;
        }

        public static void EnsureEventSystem()
        {
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }
            else if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                var legacy = es.GetComponent<StandaloneInputModule>();
                if (legacy != null) Object.DestroyImmediate(legacy);
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        public static Canvas CreateMainCanvas(string name, int sortOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            canvas.pixelPerfect = true;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = sprite;
            img.type = (sprite != null && sprite.border != Vector4.zero) ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = color;
            return go.GetComponent<RectTransform>();
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize, Color color, bool isDisplay = false, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            ResolveFonts(out var dispFont, out var bodyFont);
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = isDisplay ? dispFont : bodyFont;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            if (isDisplay)
            {
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 5f;
            }
            return tmp;
        }

        public static UnityEngine.UI.Button CreateButton(Transform parent, string name, string label, bool primaryAccent, float height = 44f)
        {
            var rt = CreatePanel(parent, name, primaryAccent ? GetAccentSprite() : GetControlSprite(), Color.white);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.selectedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = cb;

            var tmp = CreateText(rt, "Label", label, 15f, primaryAccent ? AccentSkyBlue : TextPrimary, true, TextAlignmentOptions.Center);
            StretchFill(tmp.rectTransform, 10f, 4f);
            rt.gameObject.AddComponent<UIAudioButtonRelay>();
            btn.onClick.AddListener(() => SoundManager.Instance?.PlayUIClick());
            return btn;
        }

        public static UnityEngine.UI.Slider CreateSlider(Transform parent, string name, float min, float max, float initial)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.flexibleWidth = 1f;

            var bg = CreatePanel(root.transform, "Background", GetControlSprite(), Color.white);
            StretchFill(bg, 0f, 7f);

            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(root.transform, false);
            StretchFill(fillArea, 2f, 8f);

            var fill = CreatePanel(fillArea, "Fill", GetSolidSprite(), AccentSkyBlue);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(root.transform, false);
            StretchFill(handleArea, 6f, 0f);

            var handle = CreatePanel(handleArea, "Handle", GetSolidSprite(), TextPrimary);
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

        public static UnityEngine.UI.Toggle CreateToggle(Transform parent, string name, string label, bool initial)
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

            var box = CreatePanel(row.transform, "Box", GetControlSprite(), Color.white);
            var boxLe = box.gameObject.AddComponent<LayoutElement>();
            boxLe.preferredWidth = 22f;
            boxLe.preferredHeight = 22f;

            var check = CreatePanel(box, "Check", GetSolidSprite(), AccentSkyBlue);
            StretchFill(check, 4f, 4f);

            var txt = CreateText(row.transform, "Label", label, 14f, TextPrimary, false, TextAlignmentOptions.Left);
            var txtLe = txt.gameObject.AddComponent<LayoutElement>();
            txtLe.flexibleWidth = 1f;

            var toggle = row.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = box.GetComponent<UnityEngine.UI.Image>();
            toggle.graphic = check.GetComponent<UnityEngine.UI.Image>();
            toggle.isOn = initial;
            row.AddComponent<UIAudioButtonRelay>();
            toggle.onValueChanged.AddListener(_ => SoundManager.Instance?.PlayUIClick());
            return toggle;
        }

        public static TMP_InputField CreateInputField(Transform parent, string name, string initial)
        {
            var rt = CreatePanel(parent, name, GetControlSprite(), Color.white);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.preferredWidth = 140f;

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            textArea.SetParent(rt, false);
            StretchFill(textArea, 10f, 4f);

            var textTmp = CreateText(textArea, "Text", initial, 16f, TextPrimary, false, TextAlignmentOptions.Left);
            StretchFill(textTmp.rectTransform, 0f, 0f);

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = textTmp;
            input.text = initial;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            return input;
        }

        public static void StretchFill(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
