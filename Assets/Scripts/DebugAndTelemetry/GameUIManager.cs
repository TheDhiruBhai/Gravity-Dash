using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GravityDrift
{
    public enum UIState
    {
        MainMenu,
        Gameplay,
        PauseMenu,
        SettingsMenu,
        SessionReport,
        RunOver
    }

    public class GameUIManager : MonoBehaviour
    {
        public static GameUIManager Instance { get; private set; }

        public UIState currentState = UIState.MainMenu;
        private UIState menuReturnState = UIState.MainMenu;

        [Header("Audio & Settings Defaults")]
        public float masterVolume = 1.0f;
        public bool isFullscreen = true;
        public int configuredMaxLives = 5;

        [Header("Editable State Styling (Preserves Inspector Edits)")]
        [SerializeField] private bool manageScrimColor = true;
        [SerializeField] private Color mainMenuScrimColor = new Color32(11, 14, 20, 244);
        [SerializeField] private Color pauseScrimColor = new Color32(11, 14, 20, 195);
        [SerializeField] private bool swapSelectedButtonSprites = true;
        [SerializeField] private Sprite activeSelectionSprite;
        [SerializeField] private Sprite inactiveSelectionSprite;
        [SerializeField] private bool colorCodeDynamicHudStates = true;
        [SerializeField] private Color livesHealthyColor = new Color32(34, 197, 94, 255);
        [SerializeField] private Color livesWarningColor = new Color32(245, 158, 11, 255);
        [SerializeField] private Color livesCriticalColor = new Color32(239, 68, 68, 255);
        [SerializeField] private Color accentFlashColor = new Color32(0, 229, 255, 255);
        [SerializeField] private Color dangerColor = new Color32(239, 68, 68, 255);
        [SerializeField] private Color overloadDimColor = new Color32(140, 28, 28, 255);
        [SerializeField] private Color runOverVictoryColor = new Color32(34, 197, 94, 255);
        [SerializeField] private Color runOverDefeatColor = new Color32(239, 68, 68, 255);

        private string seedInputString = "101";
        private int selectedLevel = 1;
        private float lastEscToggleTime = -10f;
        private bool listenersWired = false;
        private Color cachedJetpackFillColor = new Color32(56, 189, 248, 255);
        private Color cachedJetpackPctColor = new Color32(56, 189, 248, 255);
        private Color cachedGravityArrowColor = new Color32(56, 189, 248, 255);
        private Color cachedLivesBadgeColor = new Color32(34, 197, 94, 255);

        [Header("Canvas & Screen Root Containers")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private GameObject scrimPanel;
        [SerializeField] private UnityEngine.UI.Image scrimImage;
        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private GameObject pauseMenuRoot;
        [SerializeField] private GameObject settingsMenuRoot;
        [SerializeField] private GameObject sessionReportRoot;
        [SerializeField] private GameObject runOverRoot;
        [SerializeField] private GameObject gameplayHudRoot;

        [Header("Run Over / Victory Screen Controls")]
        private bool lastRunVictory = false;
        [SerializeField] private TextMeshProUGUI runOverTitleText;
        [SerializeField] private TextMeshProUGUI runOverSubText;
        [SerializeField] private TextMeshProUGUI runOverStatsText;
        [SerializeField] private UnityEngine.UI.Button runOverRetryBtn;
        [SerializeField] private UnityEngine.UI.Button runOverReportBtn;
        [SerializeField] private UnityEngine.UI.Button runOverMenuBtn;

        [Header("Session Report & Graph Viewer Controls")]
        [SerializeField] private TextMeshProUGUI reportKpiText;
        [SerializeField] private TextMeshProUGUI reportSaveStatusText;
        [SerializeField] private TextMeshProUGUI levelsChartFooterText;
        [SerializeField] private TextMeshProUGUI aiChartFooterText;
        [SerializeField] private TextMeshProUGUI deathChartFooterText;
        [SerializeField] private RawImage levelsChartImage;
        [SerializeField] private RawImage aiChartImage;
        [SerializeField] private RawImage deathChartImage;
        private Texture2D levelsChartTex;
        private Texture2D aiChartTex;
        private Texture2D deathChartTex;
        [SerializeField] private UnityEngine.UI.Button saveReportBtn;
        [SerializeField] private UnityEngine.UI.Button reportBackBtn;

        [Header("Main Menu Controls")]
        [SerializeField] private TMP_InputField seedInputField;
        [SerializeField] private UnityEngine.UI.Button randomizeSeedBtn;
        [SerializeField] private TextMeshProUGUI levelStepperText;
        [SerializeField] private UnityEngine.UI.Button levelMinusBtn;
        [SerializeField] private UnityEngine.UI.Button levelPlusBtn;
        [SerializeField] private UnityEngine.UI.Button mainLives3Btn;
        [SerializeField] private UnityEngine.UI.Button mainLives5Btn;
        [SerializeField] private UnityEngine.UI.Button startMissionBtn;
        [SerializeField] private UnityEngine.UI.Button mainSettingsBtn;
        [SerializeField] private UnityEngine.UI.Button mainReportBtn;
        [SerializeField] private UnityEngine.UI.Button quitBtn;

        [Header("Pause Menu Controls")]
        [SerializeField] private UnityEngine.UI.Button resumeBtn;
        [SerializeField] private UnityEngine.UI.Button restartSeedBtn;
        [SerializeField] private UnityEngine.UI.Button newSeedBtn;
        [SerializeField] private UnityEngine.UI.Button pauseReportBtn;
        [SerializeField] private UnityEngine.UI.Button pauseSettingsBtn;
        [SerializeField] private UnityEngine.UI.Button returnMainBtn;

        [Header("Settings Controls")]
        [SerializeField] private UnityEngine.UI.Button setLives3Btn;
        [SerializeField] private UnityEngine.UI.Button setLives5Btn;
        [SerializeField] private TextMeshProUGUI volumeLabel;
        [SerializeField] private UnityEngine.UI.Slider volumeSlider;
        [SerializeField] private UnityEngine.UI.Button celBtn;
        [SerializeField] private UnityEngine.UI.Button painterBtn;
        [SerializeField] private TextMeshProUGUI controllerStatusText;
        [SerializeField] private UnityEngine.UI.Button pcSchemeBtn;
        [SerializeField] private UnityEngine.UI.Button dsSchemeBtn;
        [SerializeField] private UnityEngine.UI.Toggle hapticsToggle;
        [SerializeField] private TextMeshProUGUI hapticsLabel;
        [SerializeField] private UnityEngine.UI.Slider hapticsSlider;
        [SerializeField] private UnityEngine.UI.Toggle rStickToggle;
        [SerializeField] private TextMeshProUGUI deadzoneLabel;
        [SerializeField] private UnityEngine.UI.Slider deadzoneSlider;
        [SerializeField] private TextMeshProUGUI displayModeLabel;
        [SerializeField] private UnityEngine.UI.Button toggleDisplayBtn;
        [SerializeField] private UnityEngine.UI.Button settingsBackBtn;

        [Header("Gameplay HUD Controls")]
        [SerializeField] private TextMeshProUGUI levelBadgeText;
        [SerializeField] private TextMeshProUGUI livesBadgeText;
        [SerializeField] private TextMeshProUGUI switchStatusText;
        [SerializeField] private TextMeshProUGUI airlockStatusText;
        [SerializeField] private GameObject coresRowObj;
        [SerializeField] private TextMeshProUGUI coresValueText;
        [SerializeField] private TextMeshProUGUI timerActionText;
        [SerializeField] private GameObject alertBannerObj;
        [SerializeField] private TextMeshProUGUI alertBannerText;

        [SerializeField] private TextMeshProUGUI droneHeaderText;
        [SerializeField] private List<TextMeshProUGUI> droneRowTexts = new List<TextMeshProUGUI>();
        [SerializeField] private TextMeshProUGUI droneLearnText;

        [SerializeField] private TextMeshProUGUI jetpackLabelText;
        [SerializeField] private TextMeshProUGUI jetpackPctText;
        [SerializeField] private RectTransform jetpackFillRt;
        [SerializeField] private UnityEngine.UI.Image jetpackFillImg;
        [SerializeField] private RectTransform gravityArrowRt;
        [SerializeField] private UnityEngine.UI.Image gravityArrowImg;
        [SerializeField] private TextMeshProUGUI gravityLabelText;
        private float arrowBounceTimer = 0f;
        private Vector2 arrowBounceDir = Vector2.zero;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }

            GravityDriftUITheme.EnsureEventSystem();

            if (uiCanvas == null || mainMenuRoot == null)
            {
                BuildUICanvasHierarchy();
            }

            CacheInspectorBaseColors();
            EnsureReportTextures();
            SyncControlsToRuntimeDefaults();
            WireSerializedEventListeners();
            SetUIState(currentState);
        }

        private void Start()
        {
            isFullscreen = Screen.fullScreen;
            var pcg = FindAnyObjectByType<PCGRoomGenerator>();
            if (pcg != null)
            {
                seedInputString = pcg.currentSeed.ToString();
                selectedLevel = pcg.currentLevelIndex;
                if (seedInputField != null) seedInputField.SetTextWithoutNotify(seedInputString);
                RefreshMainMenuText();
            }
            SetUIState(currentState);
        }

        private void CacheInspectorBaseColors()
        {
            if (jetpackFillImg != null) cachedJetpackFillColor = jetpackFillImg.color;
            if (jetpackPctText != null) cachedJetpackPctColor = jetpackPctText.color;
            if (gravityArrowImg != null) cachedGravityArrowColor = gravityArrowImg.color;
            if (livesBadgeText != null) cachedLivesBadgeColor = livesBadgeText.color;
        }

        private void EnsureReportTextures()
        {
            if (levelsChartTex == null)
                levelsChartTex = new Texture2D(500, 320, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            if (aiChartTex == null)
                aiChartTex = new Texture2D(500, 320, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            if (deathChartTex == null)
                deathChartTex = new Texture2D(500, 320, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };

            if (levelsChartImage != null) levelsChartImage.texture = levelsChartTex;
            if (aiChartImage != null) aiChartImage.texture = aiChartTex;
            if (deathChartImage != null) deathChartImage.texture = deathChartTex;
        }

        private void SyncControlsToRuntimeDefaults()
        {
            if (seedInputField != null) seedInputField.SetTextWithoutNotify(seedInputString);
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(masterVolume);
            if (hapticsToggle != null) hapticsToggle.SetIsOnWithoutNotify(HapticFeedbackSystem.HapticsEnabled);
            if (hapticsSlider != null) hapticsSlider.SetValueWithoutNotify(HapticFeedbackSystem.HapticsIntensity);
            if (rStickToggle != null) rStickToggle.SetIsOnWithoutNotify(InputHelper.enableRightStickGravity);
            if (deadzoneSlider != null) deadzoneSlider.SetValueWithoutNotify(InputHelper.stickDeadzone);
            if (alertBannerObj != null) alertBannerObj.SetActive(false);
        }

        private void AttachButtonAudio(UnityEngine.UI.Button btn)
        {
            if (btn == null) return;
            if (btn.GetComponent<UIAudioButtonRelay>() == null) btn.gameObject.AddComponent<UIAudioButtonRelay>();
            btn.onClick.AddListener(() => SoundManager.Instance?.PlayUIClick());
        }

        private void AttachToggleAudio(UnityEngine.UI.Toggle toggle)
        {
            if (toggle == null) return;
            if (toggle.GetComponent<UIAudioButtonRelay>() == null) toggle.gameObject.AddComponent<UIAudioButtonRelay>();
            toggle.onValueChanged.AddListener(_ => SoundManager.Instance?.PlayUIClick());
        }

        private void WireSerializedEventListeners()
        {
            if (listenersWired) return;
            listenersWired = true;

            // Main Menu
            if (seedInputField != null)
            {
                seedInputField.onValueChanged.AddListener(v => seedInputString = v);
            }
            if (randomizeSeedBtn != null)
            {
                AttachButtonAudio(randomizeSeedBtn);
                randomizeSeedBtn.onClick.AddListener(() =>
                {
                    seedInputString = PCGRoomGenerator.GenerateUniqueSeed().ToString();
                    if (seedInputField != null) seedInputField.text = seedInputString;
                });
            }
            if (levelMinusBtn != null)
            {
                AttachButtonAudio(levelMinusBtn);
                levelMinusBtn.onClick.AddListener(() => { if (selectedLevel > 1) selectedLevel--; RefreshMainMenuText(); });
            }
            if (levelPlusBtn != null)
            {
                AttachButtonAudio(levelPlusBtn);
                levelPlusBtn.onClick.AddListener(() => { if (selectedLevel < LevelObjectiveState.MaxRunLevels) selectedLevel++; RefreshMainMenuText(); });
            }
            if (mainLives3Btn != null)
            {
                AttachButtonAudio(mainLives3Btn);
                mainLives3Btn.onClick.AddListener(() => SetConfiguredLives(3));
            }
            if (mainLives5Btn != null)
            {
                AttachButtonAudio(mainLives5Btn);
                mainLives5Btn.onClick.AddListener(() => SetConfiguredLives(5));
            }
            if (startMissionBtn != null)
            {
                AttachButtonAudio(startMissionBtn);
                startMissionBtn.onClick.AddListener(StartNewGameFromMenu);
            }
            if (mainSettingsBtn != null)
            {
                AttachButtonAudio(mainSettingsBtn);
                mainSettingsBtn.onClick.AddListener(() => OpenSettingsMenu(UIState.MainMenu));
            }
            if (mainReportBtn != null)
            {
                AttachButtonAudio(mainReportBtn);
                mainReportBtn.onClick.AddListener(() => OpenSessionReport(UIState.MainMenu));
            }
            if (quitBtn != null)
            {
                AttachButtonAudio(quitBtn);
                quitBtn.onClick.AddListener(() =>
                {
                    Application.Quit();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                });
            }

            // Pause Menu
            if (resumeBtn != null)
            {
                AttachButtonAudio(resumeBtn);
                resumeBtn.onClick.AddListener(() => SetUIState(UIState.Gameplay));
            }
            if (restartSeedBtn != null)
            {
                AttachButtonAudio(restartSeedBtn);
                restartSeedBtn.onClick.AddListener(() =>
                {
                    var pcg = FindAnyObjectByType<PCGRoomGenerator>();
                    if (pcg != null) pcg.GenerateLevel(pcg.currentLevelIndex, pcg.currentSeed);
                    SetUIState(UIState.Gameplay);
                });
            }
            if (newSeedBtn != null)
            {
                AttachButtonAudio(newSeedBtn);
                newSeedBtn.onClick.AddListener(() =>
                {
                    var pcg = FindAnyObjectByType<PCGRoomGenerator>();
                    if (pcg != null) pcg.GenerateLevel(pcg.currentLevelIndex, PCGRoomGenerator.GenerateUniqueSeed(pcg.currentSeed));
                    SetUIState(UIState.Gameplay);
                });
            }
            if (pauseReportBtn != null)
            {
                AttachButtonAudio(pauseReportBtn);
                pauseReportBtn.onClick.AddListener(() => OpenSessionReport(UIState.PauseMenu));
            }
            if (pauseSettingsBtn != null)
            {
                AttachButtonAudio(pauseSettingsBtn);
                pauseSettingsBtn.onClick.AddListener(() => OpenSettingsMenu(UIState.PauseMenu));
            }
            if (returnMainBtn != null)
            {
                AttachButtonAudio(returnMainBtn);
                returnMainBtn.onClick.AddListener(ReturnToMainMenu);
            }

            // Settings Menu
            if (volumeSlider != null)
            {
                volumeSlider.onValueChanged.AddListener(v =>
                {
                    masterVolume = v;
                    AudioListener.volume = v;
                    SoundManager.Instance?.SetMasterVolume(v);
                });
            }
            if (celBtn != null)
            {
                AttachButtonAudio(celBtn);
                celBtn.onClick.AddListener(() => { var s = FindAnyObjectByType<StyleManager>(); if (s != null) s.ApplyStyle(StyleManager.ShadingStyle.CelShaded); });
            }
            if (painterBtn != null)
            {
                AttachButtonAudio(painterBtn);
                painterBtn.onClick.AddListener(() => { var s = FindAnyObjectByType<StyleManager>(); if (s != null) s.ApplyStyle(StyleManager.ShadingStyle.Painterly); });
            }
            if (pcSchemeBtn != null)
            {
                AttachButtonAudio(pcSchemeBtn);
                pcSchemeBtn.onClick.AddListener(() => InputHelper.activeScheme = ControlScheme.PC_KeyboardMouse);
            }
            if (dsSchemeBtn != null)
            {
                AttachButtonAudio(dsSchemeBtn);
                dsSchemeBtn.onClick.AddListener(() => InputHelper.activeScheme = ControlScheme.PlayStation_DualSense);
            }
            if (hapticsToggle != null)
            {
                AttachToggleAudio(hapticsToggle);
                hapticsToggle.onValueChanged.AddListener(on =>
                {
                    HapticFeedbackSystem.HapticsEnabled = on;
                    if (!on) HapticFeedbackSystem.StopAllHaptics();
                });
            }
            if (hapticsSlider != null)
            {
                hapticsSlider.onValueChanged.AddListener(v => HapticFeedbackSystem.HapticsIntensity = v);
            }
            if (rStickToggle != null)
            {
                AttachToggleAudio(rStickToggle);
                rStickToggle.onValueChanged.AddListener(on => InputHelper.enableRightStickGravity = on);
            }
            if (deadzoneSlider != null)
            {
                deadzoneSlider.onValueChanged.AddListener(v => InputHelper.stickDeadzone = v);
            }
            if (setLives3Btn != null)
            {
                AttachButtonAudio(setLives3Btn);
                setLives3Btn.onClick.AddListener(() => SetConfiguredLives(3));
            }
            if (setLives5Btn != null)
            {
                AttachButtonAudio(setLives5Btn);
                setLives5Btn.onClick.AddListener(() => SetConfiguredLives(5));
            }
            if (toggleDisplayBtn != null)
            {
                AttachButtonAudio(toggleDisplayBtn);
                toggleDisplayBtn.onClick.AddListener(() =>
                {
                    isFullscreen = !Screen.fullScreen;
                    Screen.fullScreenMode = isFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                    if (isFullscreen)
                    {
                        var res = Screen.currentResolution;
                        Screen.SetResolution(res.width, res.height, FullScreenMode.FullScreenWindow);
                    }
                    else
                    {
                        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                    }
                    var pcg = FindAnyObjectByType<PCGRoomGenerator>();
                    if (pcg != null) pcg.AdjustCameraFraming(pcg.roomWidth, pcg.roomHeight);
                });
            }
            if (settingsBackBtn != null)
            {
                AttachButtonAudio(settingsBackBtn);
                settingsBackBtn.onClick.AddListener(() => SetUIState(menuReturnState));
            }

            // Session Report
            if (saveReportBtn != null)
            {
                AttachButtonAudio(saveReportBtn);
                saveReportBtn.onClick.AddListener(() =>
                {
                    if (SessionReportRecorder.Instance == null) gameObject.AddComponent<SessionReportRecorder>();
                    SessionReportRecorder.Instance.SaveAllReports(includeCurrentInProgress: true);
                    RefreshSessionReportPanel();
                });
            }
            if (reportBackBtn != null)
            {
                AttachButtonAudio(reportBackBtn);
                reportBackBtn.onClick.AddListener(() => SetUIState(menuReturnState));
            }

            // Run Over / Victory
            if (runOverRetryBtn != null)
            {
                AttachButtonAudio(runOverRetryBtn);
                runOverRetryBtn.onClick.AddListener(() =>
                {
                    selectedLevel = 1;
                    seedInputString = PCGRoomGenerator.GenerateUniqueSeed().ToString();
                    if (seedInputField != null) seedInputField.SetTextWithoutNotify(seedInputString);
                    StartNewGameFromMenu();
                });
            }
            if (runOverReportBtn != null)
            {
                AttachButtonAudio(runOverReportBtn);
                runOverReportBtn.onClick.AddListener(() => OpenSessionReport(UIState.RunOver));
            }
            if (runOverMenuBtn != null)
            {
                AttachButtonAudio(runOverMenuBtn);
                runOverMenuBtn.onClick.AddListener(ReturnToMainMenu);
            }
        }

        private void Update()
        {
            if (InputHelper.GetPauseDown() && (Time.unscaledTime - lastEscToggleTime) > 0.12f)
            {
                lastEscToggleTime = Time.unscaledTime;

                if (currentState == UIState.Gameplay)
                {
                    OpenPauseMenu();
                }
                else if (currentState == UIState.PauseMenu)
                {
                    SetUIState(UIState.Gameplay);
                }
                else if (currentState == UIState.SettingsMenu || currentState == UIState.SessionReport)
                {
                    SetUIState(menuReturnState);
                }
            }
            else if (InputHelper.GetReportToggleDown() && (Time.unscaledTime - lastEscToggleTime) > 0.12f)
            {
                lastEscToggleTime = Time.unscaledTime;
                if (currentState == UIState.SessionReport)
                {
                    SetUIState(menuReturnState);
                }
                else if (currentState == UIState.Gameplay || currentState == UIState.PauseMenu)
                {
                    OpenSessionReport(currentState);
                }
            }
        }

        private void OnDestroy()
        {
            if (levelsChartTex != null) Destroy(levelsChartTex);
            if (aiChartTex != null) Destroy(aiChartTex);
            if (deathChartTex != null) Destroy(deathChartTex);
        }

        private void LateUpdate()
        {
            if (currentState == UIState.Gameplay)
            {
                RefreshGameplayHUD();
            }
            else if (currentState == UIState.SettingsMenu)
            {
                RefreshSettingsDynamicLabels();
            }
        }

        public void OpenPauseMenu()
        {
            menuReturnState = UIState.PauseMenu;
            SetUIState(UIState.PauseMenu);
        }

        public void OpenSettingsMenu(UIState returnTo)
        {
            menuReturnState = returnTo;
            SetUIState(UIState.SettingsMenu);
        }

        public void OpenSessionReport(UIState returnTo)
        {
            menuReturnState = returnTo;
            SetUIState(UIState.SessionReport);
        }

        public void OpenRunOverScreen(bool isVictory)
        {
            lastRunVictory = isVictory;
            menuReturnState = UIState.RunOver;
            if (isVictory) SoundManager.Instance?.PlayLevelClearedFanfare();
            else SoundManager.Instance?.PlayGameOverSting();
            SetUIState(UIState.RunOver);
        }

        public void SetConfiguredLives(int lives)
        {
            configuredMaxLives = (lives <= 3) ? 3 : 5;
            RefreshLivesButtons();
        }

        public void StartNewGameFromMenu()
        {
            selectedLevel = Mathf.Clamp(selectedLevel, 1, LevelObjectiveState.MaxRunLevels);
            var pcg = FindAnyObjectByType<PCGRoomGenerator>();
            if (pcg != null)
            {
                string rawSeed = (seedInputField != null) ? seedInputField.text : seedInputString;
                seedInputString = rawSeed;
                int seedVal = int.TryParse(rawSeed, out int parsed) ? parsed : pcg.currentSeed;
                if (SessionReportRecorder.Instance == null) gameObject.AddComponent<SessionReportRecorder>();
                var boot = FindAnyObjectByType<GravityDriftBootstrap>();
                if (boot != null) boot.ResetRunLives(configuredMaxLives);
                SessionReportRecorder.Instance?.BeginNewSession(selectedLevel, seedVal);
                pcg.BeginNewRunVariation();
                pcg.GenerateLevel(selectedLevel, seedVal);
            }
            SetUIState(UIState.Gameplay);
        }

        public void ReturnToMainMenu()
        {
            HapticFeedbackSystem.StopAllHaptics();
            var pcg = FindAnyObjectByType<PCGRoomGenerator>();
            if (pcg != null)
            {
                pcg.TeardownActiveLevel();
                pcg.currentSeed = PCGRoomGenerator.GenerateUniqueSeed(pcg.currentSeed);
                seedInputString = pcg.currentSeed.ToString();
                if (seedInputField != null) seedInputField.text = seedInputString;
            }
            SetUIState(UIState.MainMenu);
        }

        public void SetUIState(UIState newState)
        {
            currentState = newState;

            bool isMenu = (currentState == UIState.PauseMenu || currentState == UIState.MainMenu || currentState == UIState.SettingsMenu || currentState == UIState.SessionReport || currentState == UIState.RunOver);
            Time.timeScale = isMenu ? 0f : 1f;
            if (isMenu)
            {
                HapticFeedbackSystem.StopAllHaptics();
            }

            if (SoundManager.Instance != null)
            {
                bool isGameplay = (currentState == UIState.Gameplay);
                bool isPauseDuck = (currentState == UIState.PauseMenu) ||
                                   ((currentState == UIState.SettingsMenu || currentState == UIState.SessionReport) && menuReturnState == UIState.PauseMenu);
                SoundManager.Instance.SetPausedDuck(isPauseDuck);
                if (isGameplay || isPauseDuck) SoundManager.Instance.PlayGameplayBGM();
                else SoundManager.Instance.PlayMainMenuBGM();
            }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            UpdateScreenVisibility();
            if (currentState == UIState.SessionReport)
            {
                RefreshSessionReportPanel();
            }
            else if (currentState == UIState.RunOver)
            {
                RefreshRunOverPanel();
            }
            else if (currentState == UIState.MainMenu || currentState == UIState.SettingsMenu)
            {
                RefreshLivesButtons();
            }
        }

        private void UpdateScreenVisibility()
        {
            if (uiCanvas == null) return;

            bool showScrim = currentState != UIState.Gameplay;
            if (scrimPanel != null) scrimPanel.SetActive(showScrim);
            if (scrimImage != null && manageScrimColor)
            {
                bool heavyBackdrop = (currentState == UIState.MainMenu) ||
                                     ((currentState == UIState.SettingsMenu || currentState == UIState.SessionReport) && menuReturnState == UIState.MainMenu);
                scrimImage.color = heavyBackdrop ? mainMenuScrimColor : pauseScrimColor;
            }

            if (mainMenuRoot != null) mainMenuRoot.SetActive(currentState == UIState.MainMenu);
            if (pauseMenuRoot != null) pauseMenuRoot.SetActive(currentState == UIState.PauseMenu);
            if (settingsMenuRoot != null) settingsMenuRoot.SetActive(currentState == UIState.SettingsMenu);
            if (sessionReportRoot != null) sessionReportRoot.SetActive(currentState == UIState.SessionReport);
            if (runOverRoot != null) runOverRoot.SetActive(currentState == UIState.RunOver);
            if (gameplayHudRoot != null) gameplayHudRoot.SetActive(currentState == UIState.Gameplay);

            // Keep arena player capsule hidden behind the MainMenu so screen stays clean
            var playerAuth = FindAnyObjectByType<PlayerAuthoring>(FindObjectsInactive.Include);
            if (playerAuth != null)
            {
                bool showAvatar = currentState != UIState.MainMenu &&
                                  !((currentState == UIState.SessionReport || currentState == UIState.SettingsMenu) && menuReturnState == UIState.MainMenu);
                foreach (var mr in playerAuth.GetComponentsInChildren<MeshRenderer>(true))
                {
                    mr.enabled = showAvatar;
                }
            }

            if (EventSystem.current != null)
            {
                GameObject focusTarget = currentState switch
                {
                    UIState.MainMenu => startMissionBtn != null ? startMissionBtn.gameObject : null,
                    UIState.PauseMenu => resumeBtn != null ? resumeBtn.gameObject : null,
                    UIState.SettingsMenu => settingsBackBtn != null ? settingsBackBtn.gameObject : null,
                    UIState.SessionReport => reportBackBtn != null ? reportBackBtn.gameObject : null,
                    UIState.RunOver => runOverRetryBtn != null ? runOverRetryBtn.gameObject : null,
                    _ => null
                };
                EventSystem.current.SetSelectedGameObject(focusTarget);
            }
        }

        // =========================================================================
        // FALLBACK RUNTIME UGUI CONSTRUCTION (Only used if Scene Canvas is missing)
        // =========================================================================
        private void BuildUICanvasHierarchy()
        {
            if (uiCanvas != null) return;

            GravityDriftUITheme.EnsureEventSystem();
            if (activeSelectionSprite == null) activeSelectionSprite = GravityDriftUITheme.GetAccentSprite();
            if (inactiveSelectionSprite == null) inactiveSelectionSprite = GravityDriftUITheme.GetControlSprite();

            uiCanvas = GravityDriftUITheme.CreateMainCanvas("GravityDrift_UI_Canvas", 10);

            // Backdrop Scrim
            var scrimRt = GravityDriftUITheme.CreatePanel(uiCanvas.transform, "BackdropScrim", GravityDriftUITheme.GetSolidSprite(), mainMenuScrimColor);
            GravityDriftUITheme.StretchFill(scrimRt);
            scrimPanel = scrimRt.gameObject;
            scrimImage = scrimRt.GetComponent<UnityEngine.UI.Image>();

            BuildMainMenuUI(uiCanvas.transform);
            BuildPauseMenuUI(uiCanvas.transform);
            BuildSettingsMenuUI(uiCanvas.transform);
            BuildSessionReportUI(uiCanvas.transform);
            BuildRunOverUI(uiCanvas.transform);
            BuildGameplayHUD(uiCanvas.transform);
        }

        private void BuildMainMenuUI(Transform parent)
        {
            var card = GravityDriftUITheme.CreatePanel(parent, "MainMenuCard", GravityDriftUITheme.GetPanelSprite(), Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(680f, 584f);
            mainMenuRoot = card.gameObject;

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 24, 24);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var title = GravityDriftUITheme.CreateText(card, "Title", "GRAVITY DRIFT", 36f, GravityDriftUITheme.AccentSkyBlue, true, TextAlignmentOptions.Center);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

            var sub = GravityDriftUITheme.CreateText(card, "Subtitle", "2.5D ORBITAL PUZZLE-ACTION  ·  PC & PLAYSTATION DUALSENSE", 12.5f, GravityDriftUITheme.TextSecondary, true, TextAlignmentOptions.Center);
            sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var div = GravityDriftUITheme.CreatePanel(card, "Divider", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.BorderHairline);
            div.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            var seedRow = CreateHorizontalRow(card, "SeedRow", 38f, 10f);
            var seedLbl = GravityDriftUITheme.CreateText(seedRow, "SeedLabel", "STARTING SEED", 14f, GravityDriftUITheme.TextSecondary, true);
            seedLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            seedInputField = GravityDriftUITheme.CreateInputField(seedRow, "SeedInput", seedInputString);
            randomizeSeedBtn = GravityDriftUITheme.CreateButton(seedRow, "RandomizeBtn", "RANDOMIZE", false, 36f);
            randomizeSeedBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 140f;

            var lvlRow = CreateHorizontalRow(card, "LevelRow", 38f, 10f);
            levelStepperText = GravityDriftUITheme.CreateText(lvlRow, "LevelLabel", $"STARTING LEVEL  <color=#38BDF8>{selectedLevel}</color> / 20", 14f, GravityDriftUITheme.TextPrimary, true);
            levelStepperText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            levelMinusBtn = GravityDriftUITheme.CreateButton(lvlRow, "LevelMinus", "-", false, 36f);
            levelMinusBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 44f;

            levelPlusBtn = GravityDriftUITheme.CreateButton(lvlRow, "LevelPlus", "+", false, 36f);
            levelPlusBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 44f;

            var scaleNote = GravityDriftUITheme.CreateText(lvlRow, "ScaleNote", "(20-SECTOR RUN)", 12f, GravityDriftUITheme.TextSecondary);
            scaleNote.gameObject.AddComponent<LayoutElement>().preferredWidth = 135f;

            var livesRow = CreateHorizontalRow(card, "MainLivesRow", 34f, 10f);
            var livesLbl = GravityDriftUITheme.CreateText(livesRow, "MainLivesLbl", "ROGUELITE LIVES", 14f, GravityDriftUITheme.TextSecondary, true);
            livesLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            mainLives3Btn = GravityDriftUITheme.CreateButton(livesRow, "MainLives3Btn", "3 LIVES (HARD)", false, 34f);
            mainLives3Btn.gameObject.GetComponent<LayoutElement>().preferredWidth = 148f;

            mainLives5Btn = GravityDriftUITheme.CreateButton(livesRow, "MainLives5Btn", "5 LIVES (STANDARD)", false, 34f);
            mainLives5Btn.gameObject.GetComponent<LayoutElement>().preferredWidth = 178f;

            var ctrlBox = GravityDriftUITheme.CreatePanel(card, "ControlsBox", GravityDriftUITheme.GetControlSprite(), Color.white);
            ctrlBox.gameObject.AddComponent<LayoutElement>().preferredHeight = 128f;
            var cvlg = ctrlBox.gameObject.AddComponent<VerticalLayoutGroup>();
            cvlg.padding = new RectOffset(16, 16, 10, 10);
            cvlg.spacing = 4f;
            cvlg.childControlWidth = true;
            cvlg.childControlHeight = true;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;

            var ch = GravityDriftUITheme.CreateText(ctrlBox, "CtrlHdr", "CONTROLS REFERENCE", 12.5f, GravityDriftUITheme.AccentSkyBlue, true);
            ch.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            var c1 = GravityDriftUITheme.CreateText(ctrlBox, "C1", "<b>Left Stick / WASD</b>  —  2D Jetpack Thrust (Gravity-Neutral)", 12.5f, GravityDriftUITheme.TextPrimary);
            c1.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            var c2 = GravityDriftUITheme.CreateText(ctrlBox, "C2", "<b>R2 Trigger / Shift / Space</b>  —  Jetpack Boost (+Rumble)", 12.5f, GravityDriftUITheme.TextPrimary);
            c2.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            var c3 = GravityDriftUITheme.CreateText(ctrlBox, "C3", "<b>Touchpad Swipe / D-Pad / Arrows / 1-4</b>  —  4-Way Crate Gravity Flip", 12.5f, GravityDriftUITheme.TextPrimary);
            c3.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            var c4 = GravityDriftUITheme.CreateText(ctrlBox, "C4", "<b>Options / Esc</b> Pause  ·  <b>Create / Tab</b> Debug  ·  <b>C / V</b> Style  ·  <b>N / P</b> Seeds", 11.5f, GravityDriftUITheme.TextSecondary);
            c4.gameObject.AddComponent<LayoutElement>().preferredHeight = 17f;

            startMissionBtn = GravityDriftUITheme.CreateButton(card, "StartMissionBtn", "START MISSION", true, 44f);

            var secRow = CreateHorizontalRow(card, "MainSecondaryRow", 38f, 10f);
            mainSettingsBtn = GravityDriftUITheme.CreateButton(secRow, "SettingsBtn", "SETTINGS", false, 38f);
            mainSettingsBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            mainReportBtn = GravityDriftUITheme.CreateButton(secRow, "MainReportBtn", "REPORT & GRAPHS", false, 38f);
            mainReportBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            quitBtn = GravityDriftUITheme.CreateButton(card, "QuitBtn", "QUIT TO DESKTOP", false, 38f);
        }

        private void RefreshMainMenuText()
        {
            if (levelStepperText != null)
            {
                levelStepperText.text = $"STARTING LEVEL  <color=#38BDF8>{selectedLevel}</color> / 20";
            }
            RefreshLivesButtons();
        }

        private void ApplySelectionState(UnityEngine.UI.Button btn, bool isSelected)
        {
            if (btn == null || !swapSelectedButtonSprites) return;
            if (activeSelectionSprite == null || inactiveSelectionSprite == null) return;
            var img = btn.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
            {
                img.sprite = isSelected ? activeSelectionSprite : inactiveSelectionSprite;
            }
        }

        private void RefreshLivesButtons()
        {
            bool three = configuredMaxLives <= 3;
            ApplySelectionState(mainLives3Btn, three);
            ApplySelectionState(mainLives5Btn, !three);
            ApplySelectionState(setLives3Btn, three);
            ApplySelectionState(setLives5Btn, !three);
        }

        private void BuildPauseMenuUI(Transform parent)
        {
            var card = GravityDriftUITheme.CreatePanel(parent, "PauseMenuCard", GravityDriftUITheme.GetPanelSprite(), Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(460f, 434f);
            pauseMenuRoot = card.gameObject;

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 24, 24);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var title = GravityDriftUITheme.CreateText(card, "PauseTitle", "MISSION PAUSED", 28f, GravityDriftUITheme.AccentSkyBlue, true, TextAlignmentOptions.Center);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

            var div = GravityDriftUITheme.CreatePanel(card, "Divider", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.BorderHairline);
            div.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            resumeBtn = GravityDriftUITheme.CreateButton(card, "ResumeBtn", "RESUME MISSION", true, 46f);
            restartSeedBtn = GravityDriftUITheme.CreateButton(card, "RestartBtn", "RESTART CURRENT SEED", false, 42f);
            newSeedBtn = GravityDriftUITheme.CreateButton(card, "NewSeedBtn", "NEW SEED (N)", false, 42f);
            pauseReportBtn = GravityDriftUITheme.CreateButton(card, "PauseReportBtn", "GAME REPORT & GRAPHS (G)", false, 42f);
            pauseSettingsBtn = GravityDriftUITheme.CreateButton(card, "PauseSettingsBtn", "SETTINGS", false, 42f);
            returnMainBtn = GravityDriftUITheme.CreateButton(card, "ReturnMainBtn", "RETURN TO MAIN MENU", false, 42f);
        }

        private void BuildSettingsMenuUI(Transform parent)
        {
            var card = GravityDriftUITheme.CreatePanel(parent, "SettingsMenuCard", GravityDriftUITheme.GetPanelSprite(), Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(620f, 602f);
            settingsMenuRoot = card.gameObject;

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 22, 22);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var title = GravityDriftUITheme.CreateText(card, "SettingsTitle", "SYSTEM SETTINGS", 28f, GravityDriftUITheme.AccentSkyBlue, true, TextAlignmentOptions.Center);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            var div = GravityDriftUITheme.CreatePanel(card, "Divider", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.BorderHairline);
            div.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            var volRow = CreateHorizontalRow(card, "VolRow", 32f, 12f);
            volumeLabel = GravityDriftUITheme.CreateText(volRow, "VolLbl", "MASTER VOLUME  100%", 14f, GravityDriftUITheme.TextPrimary);
            volumeLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            volumeSlider = GravityDriftUITheme.CreateSlider(volRow, "VolSlider", 0f, 1f, masterVolume);

            var styleRow = CreateHorizontalRow(card, "StyleRow", 38f, 10f);
            var styleLbl = GravityDriftUITheme.CreateText(styleRow, "StyleLbl", "SHADER STYLE (C / V)", 14f, GravityDriftUITheme.TextPrimary);
            styleLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            celBtn = GravityDriftUITheme.CreateButton(styleRow, "CelBtn", "CEL-SHADED", false, 36f);
            celBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            painterBtn = GravityDriftUITheme.CreateButton(styleRow, "PainterBtn", "PAINTERLY", false, 36f);
            painterBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            controllerStatusText = GravityDriftUITheme.CreateText(card, "CtrlStatus", "CONTROLLER STATUS", 13f, GravityDriftUITheme.TextSecondary);
            controllerStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var schemeRow = CreateHorizontalRow(card, "SchemeRow", 38f, 10f);
            pcSchemeBtn = GravityDriftUITheme.CreateButton(schemeRow, "PcBtn", "PC KEYBOARD / MOUSE", false, 36f);
            pcSchemeBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            dsSchemeBtn = GravityDriftUITheme.CreateButton(schemeRow, "DsBtn", "PLAYSTATION DUALSENSE", false, 36f);
            dsSchemeBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            hapticsToggle = GravityDriftUITheme.CreateToggle(card, "HapticsToggle", "Controller Rumble / DualSense Haptics & Lightbar Sync", HapticFeedbackSystem.HapticsEnabled);

            var hapRow = CreateHorizontalRow(card, "HapticsRow", 32f, 12f);
            hapticsLabel = GravityDriftUITheme.CreateText(hapRow, "HapLbl", "HAPTICS INTENSITY  1.00X", 14f, GravityDriftUITheme.TextPrimary);
            hapticsLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            hapticsSlider = GravityDriftUITheme.CreateSlider(hapRow, "HapSlider", 0f, 1.5f, HapticFeedbackSystem.HapticsIntensity);

            rStickToggle = GravityDriftUITheme.CreateToggle(card, "RStickToggle", "Enable Right-Stick Gravity Flick Fallback (with Touchpad & D-Pad)", InputHelper.enableRightStickGravity);

            var dzRow = CreateHorizontalRow(card, "DeadzoneRow", 32f, 12f);
            deadzoneLabel = GravityDriftUITheme.CreateText(dzRow, "DzLbl", "L-STICK DEADZONE  0.16", 14f, GravityDriftUITheme.TextPrimary);
            deadzoneLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            deadzoneSlider = GravityDriftUITheme.CreateSlider(dzRow, "DzSlider", 0.05f, 0.35f, InputHelper.stickDeadzone);

            var setLivesRow = CreateHorizontalRow(card, "SetLivesRow", 36f, 10f);
            var setLivesLbl = GravityDriftUITheme.CreateText(setLivesRow, "SetLivesLbl", "RUN LIVES", 14f, GravityDriftUITheme.TextPrimary);
            setLivesLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            setLives3Btn = GravityDriftUITheme.CreateButton(setLivesRow, "SetLives3Btn", "3 LIVES", false, 34f);
            setLives3Btn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            setLives5Btn = GravityDriftUITheme.CreateButton(setLivesRow, "SetLives5Btn", "5 LIVES (DEFAULT)", false, 34f);
            setLives5Btn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var dispRow = CreateHorizontalRow(card, "DisplayRow", 38f, 10f);
            displayModeLabel = GravityDriftUITheme.CreateText(dispRow, "DispLbl", "DISPLAY MODE", 14f, GravityDriftUITheme.TextPrimary);
            displayModeLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 215f;
            toggleDisplayBtn = GravityDriftUITheme.CreateButton(dispRow, "ToggleDispBtn", "TOGGLE FULLSCREEN", false, 36f);
            toggleDisplayBtn.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            settingsBackBtn = GravityDriftUITheme.CreateButton(card, "SettingsBackBtn", "BACK", true, 44f);
        }

        private void RefreshSettingsDynamicLabels()
        {
            if (volumeLabel != null) volumeLabel.text = $"MASTER VOLUME  <mspace=0.58em>{(int)(masterVolume * 100)}%</mspace>";
            if (hapticsLabel != null) hapticsLabel.text = $"HAPTICS INTENSITY  <mspace=0.58em>{HapticFeedbackSystem.HapticsIntensity:F2}X</mspace>";
            if (deadzoneLabel != null) deadzoneLabel.text = $"L-STICK DEADZONE  <mspace=0.58em>{InputHelper.stickDeadzone:F2}</mspace>";
            if (displayModeLabel != null) displayModeLabel.text = $"DISPLAY  ({(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")})";
            if (controllerStatusText != null)
            {
                string conn = InputHelper.IsDualSenseConnected()
                    ? "<color=#38BDF8>DUALSENSE CONNECTED (TOUCHPAD + HAPTICS)</color>"
                    : "<color=#8A99AD>KEYBOARD READY (CONNECT DUALSENSE VIA USB/BT)</color>";
                controllerStatusText.text = $"INPUT: {conn}  ·  SCHEME: <color=#E2E8F0>{InputHelper.activeScheme}</color>";
            }

            // Highlight active shader & control scheme buttons
            var styleMgr = FindAnyObjectByType<StyleManager>();
            if (styleMgr != null)
            {
                ApplySelectionState(celBtn, styleMgr.currentStyle == StyleManager.ShadingStyle.CelShaded);
                ApplySelectionState(painterBtn, styleMgr.currentStyle == StyleManager.ShadingStyle.Painterly);
            }

            ApplySelectionState(pcSchemeBtn, InputHelper.activeScheme == ControlScheme.PC_KeyboardMouse);
            ApplySelectionState(dsSchemeBtn, InputHelper.activeScheme == ControlScheme.PlayStation_DualSense);
        }

        private void BuildSessionReportUI(Transform parent)
        {
            var card = GravityDriftUITheme.CreatePanel(parent, "SessionReportCard", GravityDriftUITheme.GetPanelSprite(), Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1660f, 840f);
            sessionReportRoot = card.gameObject;

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(28, 28, 22, 22);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdrRow = CreateHorizontalRow(card, "ReportHeaderRow", 38f, 12f);
            var title = GravityDriftUITheme.CreateText(hdrRow, "ReportTitle", "MISSION TELEMETRY & AI REPORT", 28f, GravityDriftUITheme.AccentSkyBlue, true);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var hint = GravityDriftUITheme.CreateText(hdrRow, "ReportCloseHint", "LIVE BAYESIAN POSTERIOR (π) · PER-LEVEL & PER-INPUT ANALYTICS  [G / ESC]", 12.5f, GravityDriftUITheme.TextSecondary, true, TextAlignmentOptions.Right);
            hint.gameObject.AddComponent<LayoutElement>().preferredWidth = 600f;

            var kpiPanel = GravityDriftUITheme.CreatePanel(card, "ReportKpiStrip", GravityDriftUITheme.GetControlSprite(), Color.white);
            kpiPanel.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            reportKpiText = GravityDriftUITheme.CreateText(kpiPanel, "ReportKpiText", "NO SESSION RECORDED YET — CLICK START MISSION TO COLLECT LIVE DATA", 14f, GravityDriftUITheme.TextPrimary, true, TextAlignmentOptions.Center);
            GravityDriftUITheme.StretchFill(reportKpiText.rectTransform, 14f, 6f);

            var graphsRow = CreateHorizontalRow(card, "GraphsRow", 590f, 16f);
            var gLe = graphsRow.gameObject.GetComponent<LayoutElement>();
            gLe.flexibleHeight = 1f;

            EnsureReportTextures();

            CreateReportGraphCard(graphsRow, "LevelsGraphCard", "1. LEVELS PLAYED & PROGRESSION",
                "<color=#22C55E>■</color> CLEARED   <color=#EF4444>■</color> DEATH   <color=#00E5FF>●</color> TIME (S)   <color=#F59E0B>●</color> COMPLEXITY",
                levelsChartTex, out levelsChartImage, out levelsChartFooterText);

            CreateReportGraphCard(graphsRow, "AiInputGraphCard", "2. AI IMPROVEMENT PER INPUT",
                "<color=#38BDF8>●</color> POSTERIOR π   <color=#F59E0B>●</color> ENTROPY PRECISION   <color=#22C55E>●</color> AI IMP %",
                aiChartTex, out aiChartImage, out aiChartFooterText);

            CreateReportGraphCard(graphsRow, "DeathRateGraphCard", "3. DEATH RATE & IMPROVEMENT",
                "<color=#EF4444>●</color> DEATH RATE %   <color=#38BDF8>●</color> AI IMP %   <color=#22C55E>●</color> DODGE SKILL %",
                deathChartTex, out deathChartImage, out deathChartFooterText);

            var footRow = CreateHorizontalRow(card, "ReportFooterRow", 44f, 12f);
            reportSaveStatusText = GravityDriftUITheme.CreateText(footRow, "SaveStatus", "Auto-saves on Clear, Death, Reset, Menu Return & Quit", 13f, GravityDriftUITheme.TextSecondary);
            reportSaveStatusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            saveReportBtn = GravityDriftUITheme.CreateButton(footRow, "SaveReportBtn", "SAVE REPORT (CSV + JSON)", true, 42f);
            saveReportBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 310f;

            reportBackBtn = GravityDriftUITheme.CreateButton(footRow, "ReportBackBtn", "BACK", false, 42f);
            reportBackBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 140f;
        }

        private void CreateReportGraphCard(
            Transform parent,
            string name,
            string titleStr,
            string legendStr,
            Texture2D tex,
            out RawImage rawImg,
            out TextMeshProUGUI footerTxt)
        {
            var panel = GravityDriftUITheme.CreatePanel(parent, name, GravityDriftUITheme.GetControlSprite(), Color.white);
            panel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 12);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var title = GravityDriftUITheme.CreateText(panel, "CardTitle", titleStr, 15.5f, GravityDriftUITheme.AccentSkyBlue, true);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            var leg = GravityDriftUITheme.CreateText(panel, "CardLegend", legendStr, 11.5f, GravityDriftUITheme.TextSecondary);
            leg.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var imgGo = new GameObject("PlotRawImage", typeof(RectTransform), typeof(RawImage));
            imgGo.transform.SetParent(panel, false);
            var imgLe = imgGo.AddComponent<LayoutElement>();
            imgLe.flexibleHeight = 1f;
            imgLe.minHeight = 360f;
            rawImg = imgGo.GetComponent<RawImage>();
            rawImg.texture = tex;
            rawImg.color = Color.white;

            footerTxt = GravityDriftUITheme.CreateText(panel, "CardFooter", "Waiting for gameplay samples...", 12.5f, GravityDriftUITheme.TextPrimary);
            footerTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        }

        public void RefreshSessionReportPanel()
        {
            var rec = SessionReportRecorder.Instance;
            var levelRows = (rec != null) ? rec.GetLevelRecordsSnapshot(includeCurrentInProgress: true) : new List<SessionLevelRecord>();
            IReadOnlyList<PerInputAIEvent> inputEvts = (rec != null) ? rec.InputEvents : System.Array.Empty<PerInputAIEvent>();

            SessionGraphRenderer.RenderLevelsChart(levelsChartTex, levelRows);
            SessionGraphRenderer.RenderPerInputAIChart(aiChartTex, inputEvts);
            SessionGraphRenderer.RenderDeathAndImprovementChart(deathChartTex, levelRows);

            if (rec == null || (levelRows.Count == 0 && inputEvts.Count == 0))
            {
                if (reportKpiText != null)
                    reportKpiText.text = "NO LIVE SESSION DATA YET  ·  START A MISSION AND PLAY TO RECORD PER-LEVEL & PER-INPUT TELEMETRY";
                if (levelsChartFooterText != null) levelsChartFooterText.text = "No level attempts logged yet.";
                if (aiChartFooterText != null) aiChartFooterText.text = "No player/AI input events logged yet.";
                if (deathChartFooterText != null) deathChartFooterText.text = "No death or AI progression samples yet.";
                return;
            }

            var distinctLevels = new HashSet<int>();
            int cleared = 0, deaths = 0;
            float avgTime = 0f, latestImp = 0f, latestDodge = 0f, latestDeathRate = 0f, latestPat = 0f;
            int latestLivesRem = configuredMaxLives, latestMaxLives = configuredMaxLives;
            for (int i = 0; i < levelRows.Count; i++)
            {
                var r = levelRows[i];
                distinctLevels.Add(r.level_index);
                if (r.room_cleared || r.outcome == "Cleared") cleared++;
                deaths += r.deaths_in_attempt;
                avgTime += r.room_timer_sec;
                latestImp = r.drone_improvement_pct;
                latestDodge = r.crate_dodge_skill * 100f;
                latestDeathRate = r.cumulative_death_rate_pct;
                latestPat = r.pattern_adaptation_pct;
                if (r.max_lives >= 3)
                {
                    latestMaxLives = r.max_lives;
                    latestLivesRem = r.lives_remaining;
                }
            }
            if (levelRows.Count > 0) avgTime /= levelRows.Count;

            int dsCount = 0, pcCount = 0;
            foreach (var kv in rec.InputSourceCounts)
            {
                if (kv.Key.StartsWith("DualSense")) dsCount += kv.Value;
                else pcCount += kv.Value;
            }

            if (reportKpiText != null)
            {
                reportKpiText.text =
                    $"SESSION <color=#38BDF8>{rec.CurrentSessionId}</color>  ·  SECTORS: <color=#E2E8F0>{distinctLevels.Count}/20</color> ({cleared} CLEARED)  ·  " +
                    $"LIVES: <color=#22C55E>{latestLivesRem}/{latestMaxLives}</color>  ·  DEATHS: <color=#EF4444>{latestDeathRate:F1}%</color>  ·  PAT ADAPT: <color=#F59E0B>{latestPat:F0}%</color>  ·  DS={dsCount}/PC={pcCount}";
            }

            if (levelsChartFooterText != null)
            {
                levelsChartFooterText.text = $"Attempts: {levelRows.Count}  ·  Cleared: {cleared}/20  ·  Mean Room Time: {avgTime:F1}s";
            }

            if (aiChartFooterText != null)
            {
                if (inputEvts.Count > 0)
                {
                    var first = inputEvts[0];
                    var last = inputEvts[inputEvts.Count - 1];
                    aiChartFooterText.text = $"Inputs: {inputEvts.Count}  ·  π: {first.top_cluster_confidence * 100f:F0}%→{last.top_cluster_confidence * 100f:F0}%  ·  PatAdapt: {last.pattern_adaptation_pct:F0}%";
                }
                else
                {
                    aiChartFooterText.text = "Awaiting movement / gravity flip / boost inputs...";
                }
            }

            if (deathChartFooterText != null)
            {
                deathChartFooterText.text = $"Death Rate: {latestDeathRate:F1}%  ·  AI Imp: {latestImp:F1}%  ·  Dodge Skill: {latestDodge:F0}%  ·  Lives: {latestLivesRem}/{latestMaxLives}";
            }

            if (reportSaveStatusText != null)
            {
                string pathNote = string.IsNullOrEmpty(rec.LastSavedProjectDir) ? "Telemetry/Sessions/" : rec.LastSavedProjectDir;
                reportSaveStatusText.text = $"{rec.LastSavedStatus}  [{pathNote}]";
            }
        }

        private void BuildRunOverUI(Transform parent)
        {
            var card = GravityDriftUITheme.CreatePanel(parent, "RunOverCard", GravityDriftUITheme.GetPanelSprite(), Color.white);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(560f, 410f);
            runOverRoot = card.gameObject;

            var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 26, 26);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            runOverTitleText = GravityDriftUITheme.CreateText(card, "RunOverTitle", "RUN TERMINATED", 32f, GravityDriftUITheme.DangerRed, true, TextAlignmentOptions.Center);
            runOverTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

            runOverSubText = GravityDriftUITheme.CreateText(card, "RunOverSub", "0 / 5 LIVES REMAINING", 14f, GravityDriftUITheme.TextSecondary, true, TextAlignmentOptions.Center);
            runOverSubText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var div = GravityDriftUITheme.CreatePanel(card, "Divider", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.BorderHairline);
            div.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

            var statsPanel = GravityDriftUITheme.CreatePanel(card, "RunOverStatsBox", GravityDriftUITheme.GetControlSprite(), Color.white);
            statsPanel.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;
            runOverStatsText = GravityDriftUITheme.CreateText(statsPanel, "RunOverStatsTxt", "", 13.5f, GravityDriftUITheme.TextPrimary, false, TextAlignmentOptions.Center);
            GravityDriftUITheme.StretchFill(runOverStatsText.rectTransform, 16f, 12f);

            runOverRetryBtn = GravityDriftUITheme.CreateButton(card, "RetryRunBtn", "RETRY RUN", true, 44f);
            runOverReportBtn = GravityDriftUITheme.CreateButton(card, "RunOverReportBtn", "VIEW REPORT & GRAPHS", false, 40f);
            runOverMenuBtn = GravityDriftUITheme.CreateButton(card, "RunOverMenuBtn", "MAIN MENU", false, 40f);
        }

        private void RefreshRunOverPanel()
        {
            if (runOverTitleText != null)
            {
                runOverTitleText.text = lastRunVictory ? "OPERATION COMPLETE" : "RUN TERMINATED";
                if (colorCodeDynamicHudStates)
                {
                    runOverTitleText.color = lastRunVictory ? runOverVictoryColor : runOverDefeatColor;
                }
            }
            if (runOverSubText != null)
            {
                runOverSubText.text = lastRunVictory
                    ? $"ALL 20 / 20 ORBITAL SECTORS CLEARED"
                    : $"0 / {configuredMaxLives} LIVES REMAINING  ·  RETURN ORBITAL PODS FAILED";
            }

            var rec = SessionReportRecorder.Instance;
            int rows = (rec != null) ? rec.LevelRecords.Count : 0;
            float latestImp = 0f, latestPat = 0f, latestDeathRate = 0f;
            int cleared = 0;
            if (rec != null)
            {
                for (int i = 0; i < rec.LevelRecords.Count; i++)
                {
                    var r = rec.LevelRecords[i];
                    if (r.room_cleared || r.outcome == "Cleared") cleared++;
                    latestImp = r.drone_improvement_pct;
                    latestPat = r.pattern_adaptation_pct;
                    latestDeathRate = r.cumulative_death_rate_pct;
                }
            }

            if (runOverStatsText != null)
            {
                runOverStatsText.text =
                    $"<b>CLEARED:</b> {cleared}  ·  <b>ATTEMPTS:</b> {rows}  ·  <b>DEATH RATE:</b> <color=#EF4444>{latestDeathRate:F1}%</color>\n" +
                    $"<b>DRONE AI IMP:</b> <color=#38BDF8>{latestImp:F1}%</color>  ·  <b>PATTERN ADAPT:</b> <color=#F59E0B>{latestPat:F0}%</color>\n" +
                    $"<color=#8A99AD>{(rec != null ? rec.LastSavedStatus : "Session telemetry archived.")}</color>";
            }
        }

        private void BuildGameplayHUD(Transform parent)
        {
            var hudGo = new GameObject("GameplayHUD", typeof(RectTransform));
            hudGo.transform.SetParent(parent, false);
            var hudRt = hudGo.GetComponent<RectTransform>();
            GravityDriftUITheme.StretchFill(hudRt);
            gameplayHudRoot = hudGo;

            // 1. TOP-LEFT PANEL: Level/20, Lives, Seed, Switch, Airlock, Cores, Timer, Conditional Alert
            var tl = GravityDriftUITheme.CreatePanel(hudRt, "TopLeftPanel", GravityDriftUITheme.GetPanelSprite(), Color.white);
            tl.anchorMin = new Vector2(0f, 1f);
            tl.anchorMax = new Vector2(0f, 1f);
            tl.pivot = new Vector2(0f, 1f);
            tl.anchoredPosition = new Vector2(20f, -18f);
            tl.sizeDelta = new Vector2(420f, 198f);

            var tlVlg = tl.gameObject.AddComponent<VerticalLayoutGroup>();
            tlVlg.padding = new RectOffset(16, 16, 12, 12);
            tlVlg.spacing = 4f;
            tlVlg.childControlWidth = true;
            tlVlg.childControlHeight = true;
            tlVlg.childForceExpandWidth = true;
            tlVlg.childForceExpandHeight = false;

            levelBadgeText = GravityDriftUITheme.CreateText(tl, "LevelBadge", "LVL 01 / 20  ·  SEED #101", 16.5f, GravityDriftUITheme.AccentSkyBlue, true);
            levelBadgeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 23f;

            livesBadgeText = GravityDriftUITheme.CreateText(tl, "LivesBadge", "LIVES  5 / 5", 13.5f, GravityDriftUITheme.SuccessMint, true);
            livesBadgeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            switchStatusText = GravityDriftUITheme.CreateText(tl, "SwitchStatus", "SWITCH  LOCKED", 13f, GravityDriftUITheme.TextSecondary);
            switchStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            airlockStatusText = GravityDriftUITheme.CreateText(tl, "AirlockStatus", "AIRLOCK  LOCKED", 13f, GravityDriftUITheme.TextSecondary);
            airlockStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            coresRowObj = new GameObject("CoresRow", typeof(RectTransform));
            coresRowObj.transform.SetParent(tl, false);
            coresRowObj.AddComponent<LayoutElement>().preferredHeight = 19f;
            coresValueText = GravityDriftUITheme.CreateText(coresRowObj.transform, "CoresText", "CORES  0 / 0", 13f, GravityDriftUITheme.TextPrimary);
            GravityDriftUITheme.StretchFill(coresValueText.rectTransform);

            timerActionText = GravityDriftUITheme.CreateText(tl, "TimerAction", "TIME  0.0s  ·  IDLE", 13f, GravityDriftUITheme.TextSecondary);
            timerActionText.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

            var alertRt = GravityDriftUITheme.CreatePanel(tl, "AlertBanner", GravityDriftUITheme.GetDangerSprite(), Color.white);
            alertRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            alertBannerObj = alertRt.gameObject;
            alertBannerText = GravityDriftUITheme.CreateText(alertRt, "AlertText", "HAZARD SHOCK! -25% FUEL", 13f, GravityDriftUITheme.DangerRed, true, TextAlignmentOptions.Center);
            GravityDriftUITheme.StretchFill(alertBannerText.rectTransform, 6f, 2f);
            alertBannerObj.SetActive(false);

            // 2. TOP-RIGHT PANEL: Drone Threat Monitor & Learning Summary
            var tr = GravityDriftUITheme.CreatePanel(hudRt, "TopRightPanel", GravityDriftUITheme.GetPanelSprite(), Color.white);
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

            droneHeaderText = GravityDriftUITheme.CreateText(tr, "DroneHeader", "DRONE THREAT MONITOR", 14f, GravityDriftUITheme.AccentSkyBlue, true);
            droneHeaderText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            droneRowTexts.Clear();
            for (int d = 0; d < 5; d++)
            {
                var dr = GravityDriftUITheme.CreateText(tr, $"DroneRow_{d + 1}", $"D{d + 1}  PATROL", 12.5f, GravityDriftUITheme.TextPrimary);
                dr.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
                droneRowTexts.Add(dr);
            }

            droneLearnText = GravityDriftUITheme.CreateText(tr, "DroneLearn", "ESCAPES 0  ·  DODGE 0%  ·  IMP +0%", 11.5f, GravityDriftUITheme.TextSecondary);
            droneLearnText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            // 3. BOTTOM TACTICAL STRIP: Jetpack Fuel Bar + Gravity Vector & Directional Bounce Arrow
            var bot = GravityDriftUITheme.CreatePanel(hudRt, "BottomTacticalStrip", GravityDriftUITheme.GetPanelSprite(), Color.white);
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

            // Left Half: Jetpack Fuel Bar
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
            jetpackLabelText = GravityDriftUITheme.CreateText(fuelTop, "JetLabel", "JETPACK FUEL", 13f, GravityDriftUITheme.TextPrimary, true);
            jetpackLabelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            jetpackPctText = GravityDriftUITheme.CreateText(fuelTop, "JetPct", "100%", 14f, GravityDriftUITheme.AccentSkyBlue, true, TextAlignmentOptions.Right);
            jetpackPctText.gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;

            var barBg = GravityDriftUITheme.CreatePanel(fuelCol, "FuelBarBg", GravityDriftUITheme.GetControlSprite(), Color.white);
            barBg.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            jetpackFillRt = GravityDriftUITheme.CreatePanel(barBg, "FuelBarFill", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.AccentSkyBlue);
            jetpackFillRt.anchorMin = Vector2.zero;
            jetpackFillRt.anchorMax = Vector2.one;
            jetpackFillRt.offsetMin = new Vector2(2f, 2f);
            jetpackFillRt.offsetMax = new Vector2(-2f, -2f);
            jetpackFillImg = jetpackFillRt.GetComponent<UnityEngine.UI.Image>();

            // Vertical Hairline Separator
            var vSep = GravityDriftUITheme.CreatePanel(bot, "Sep", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.BorderHairline);
            vSep.gameObject.AddComponent<LayoutElement>().preferredWidth = 1f;

            // Right Side: Directional Gravity Vector Arrow & Charges
            var gravCol = CreateHorizontalRow(bot, "GravityVectorBox", 48f, 10f);
            gravCol.gameObject.GetComponent<LayoutElement>().preferredWidth = 185f;
            gravCol.gameObject.GetComponent<LayoutElement>().flexibleWidth = 0f;

            var arrowHolder = GravityDriftUITheme.CreatePanel(gravCol, "ArrowHolder", GravityDriftUITheme.GetControlSprite(), Color.white);
            var ahLe = arrowHolder.gameObject.AddComponent<LayoutElement>();
            ahLe.preferredWidth = 42f;
            ahLe.preferredHeight = 42f;

            gravityArrowRt = GravityDriftUITheme.CreatePanel(arrowHolder, "GravityArrow", GravityDriftUITheme.GetGravityArrowSprite(), GravityDriftUITheme.AccentSkyBlue);
            gravityArrowRt.anchorMin = new Vector2(0.5f, 0.5f);
            gravityArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
            gravityArrowRt.sizeDelta = new Vector2(26f, 26f);
            gravityArrowImg = gravityArrowRt.GetComponent<UnityEngine.UI.Image>();

            gravityLabelText = GravityDriftUITheme.CreateText(gravCol, "GravText", "GRAV: DOWN\nCHARGES 3/3", 12f, GravityDriftUITheme.TextPrimary, true);
            gravityLabelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private RectTransform CreateHorizontalRow(Transform parent, string name, float height, float spacing)
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

        // =========================================================================
        // RUNTIME ECS DATA BINDING
        // =========================================================================
        private void RefreshGameplayHUD()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            bool hazardAlertActive = false;

            // 1. Objective & Timer & Hazard Alert
            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() > 0)
            {
                using var objArray = objQuery.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                var lvl = objArray[0];

                var pcgRef = FindAnyObjectByType<PCGRoomGenerator>();
                string arch = (pcgRef != null && !string.IsNullOrEmpty(pcgRef.LastArchetypeName)) ? $"  ·  {pcgRef.LastArchetypeName.ToUpperInvariant()}" : "";
                levelBadgeText.text = $"LVL <mspace=0.55em>{lvl.LevelIndex:00}/20</mspace>  ·  #{lvl.CurrentSeed}{arch}";

                int maxL = Mathf.Max(3, lvl.MaxLives);
                int remL = Mathf.Clamp(lvl.LivesRemaining, 0, maxL);
                string hearts = new string('■', remL) + new string('□', maxL - remL);
                if (colorCodeDynamicHudStates)
                {
                    livesBadgeText.color = (remL <= 1) ? livesCriticalColor : (remL == 2 ? livesWarningColor : livesHealthyColor);
                }
                else
                {
                    livesBadgeText.color = cachedLivesBadgeColor;
                }
                livesBadgeText.text = $"LIVES  {hearts}  ({remL}/{maxL})";

                switchStatusText.text = lvl.SwitchFlipped
                    ? "SWITCH  <color=#38BDF8><b>ACTIVE - SWITCH FLIPPED</b></color>"
                    : "SWITCH  <color=#8A99AD>LOCKED (FLY NEAR PAD)</color>";

                if (lvl.RoomCleared)
                    airlockStatusText.text = "AIRLOCK  <color=#22C55E><b>CLEARED — ADVANCING</b></color>";
                else if (lvl.AirlockUnlocked)
                    airlockStatusText.text = "AIRLOCK  <color=#22C55E><b>UNLOCKED — ENTER EXIT</b></color>";
                else
                    airlockStatusText.text = "AIRLOCK  <color=#EF4444>LOCKED</color>";

                bool showCores = lvl.TotalCores > 0;
                if (coresRowObj.activeSelf != showCores) coresRowObj.SetActive(showCores);
                if (showCores)
                {
                    string cCol = (lvl.CoresCollected >= lvl.TotalCores) ? "#38BDF8" : "#E2E8F0";
                    coresValueText.text = $"CORES  <color={cCol}><b><mspace=0.55em>{lvl.CoresCollected}/{lvl.TotalCores}</mspace></b></color>";
                }

                timerActionText.text = $"TIME <mspace=0.55em>{lvl.RoomTimer:F1}s</mspace>  ·  <color=#E2E8F0>{lvl.CurrentPlayerAction}</color>";

                if (lvl.HazardWarningTimer > 0f)
                {
                    hazardAlertActive = true;
                    if (!alertBannerObj.activeSelf) alertBannerObj.SetActive(true);
                    alertBannerText.text = $"HAZARD SHOCK! -25% FUEL (HITS: {lvl.HazardHits})";
                }
            }
            objQuery.Dispose();

            // 2. Jetpack Fuel / Boost / Overload
            bool overloadAlertActive = false;
            var playerQuery = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
            if (playerQuery.CalculateEntityCount() > 0)
            {
                using var jetArray = playerQuery.ToComponentDataArray<JetpackComponent>(Allocator.Temp);
                var jet = jetArray[0];
                float fuelPct = Mathf.Clamp01(jet.Fuel / Mathf.Max(1f, jet.MaxFuel));

                if (jet.IsInCooldown)
                {
                    overloadAlertActive = true;
                    float blink = Mathf.PingPong(Time.unscaledTime * 11f, 1f);
                    Color pulseRed = (blink > 0.42f) ? dangerColor : overloadDimColor;
                    jetpackLabelText.text = $"<color=#EF4444><b>JETPACK OVERLOADED ({jet.CooldownTimer:F1}s)</b></color>";
                    jetpackPctText.text = "0%";
                    if (colorCodeDynamicHudStates)
                    {
                        jetpackPctText.color = dangerColor;
                        jetpackFillImg.color = pulseRed;
                    }
                    jetpackFillRt.anchorMax = new Vector2(0.08f, 1f);

                    if (!hazardAlertActive)
                    {
                        if (!alertBannerObj.activeSelf) alertBannerObj.SetActive(true);
                        alertBannerText.text = $"JETPACK OVERLOADED  ·  RECHARGING {jet.CooldownTimer:F1}s";
                    }
                }
                else
                {
                    jetpackLabelText.text = jet.IsBoosting
                        ? "JETPACK  <color=#38BDF8><b>[BOOST]</b></color>"
                        : (jet.IsThrusting ? "JETPACK  [THRUST]" : "JETPACK  [RECHARGING]");
                    jetpackPctText.text = $"<mspace=0.55em>{fuelPct * 100f:F0}%</mspace>";
                    jetpackPctText.color = cachedJetpackPctColor;
                    jetpackFillImg.color = cachedJetpackFillColor;
                    jetpackFillRt.anchorMax = new Vector2(Mathf.Max(0.01f, fuelPct), 1f);
                }
            }
            playerQuery.Dispose();

            if (!hazardAlertActive && !overloadAlertActive && alertBannerObj.activeSelf)
            {
                alertBannerObj.SetActive(false);
            }

            // 3. Gravity Vector & Directional Bounce Indicator
            var gravQuery = em.CreateEntityQuery(typeof(GravityState));
            if (gravQuery.CalculateEntityCount() > 0)
            {
                using var gArr = gravQuery.ToComponentDataArray<GravityState>(Allocator.Temp);
                var g = gArr[0];

                float angleZ = g.CurrentDirection switch
                {
                    GravityDirection.Up => 0f,
                    GravityDirection.Down => 180f,
                    GravityDirection.Left => 90f,
                    GravityDirection.Right => -90f,
                    _ => 180f
                };
                gravityArrowRt.localRotation = Quaternion.Euler(0f, 0f, angleZ);

                if (g.DirectionChangedThisFrame)
                {
                    arrowBounceTimer = 0.28f;
                    arrowBounceDir = g.CurrentDirection switch
                    {
                        GravityDirection.Up => Vector2.up,
                        GravityDirection.Down => Vector2.down,
                        GravityDirection.Left => Vector2.left,
                        GravityDirection.Right => Vector2.right,
                        _ => Vector2.zero
                    };
                }

                if (arrowBounceTimer > 0f)
                {
                    arrowBounceTimer = Mathf.Max(0f, arrowBounceTimer - Time.unscaledDeltaTime);
                    float t = arrowBounceTimer / 0.28f;
                    float kick = Mathf.Sin(t * Mathf.PI);
                    gravityArrowRt.anchoredPosition = arrowBounceDir * (kick * 7f);
                    gravityArrowRt.localScale = Vector3.one * (1f + kick * 0.35f);
                    if (colorCodeDynamicHudStates) gravityArrowImg.color = accentFlashColor;
                }
                else
                {
                    gravityArrowRt.anchoredPosition = Vector2.zero;
                    gravityArrowRt.localScale = Vector3.one;
                    gravityArrowImg.color = cachedGravityArrowColor;
                }

                gravityLabelText.text = $"GRAV: <color=#38BDF8>{g.CurrentDirection.ToString().ToUpperInvariant()}</color>\nCHARGES <mspace=0.55em>{g.CurrentCharges}/{g.MaxCharges}</mspace>";
            }
            gravQuery.Dispose();

            // 4. Drone Threat Monitor
            var droneQuery = em.CreateEntityQuery(typeof(DroneState), typeof(BayesianBeliefComponent));
            int droneCount = droneQuery.CalculateEntityCount();
            if (droneCount > 0)
            {
                using var dStates = droneQuery.ToComponentDataArray<DroneState>(Allocator.Temp);
                using var dBeliefs = droneQuery.ToComponentDataArray<BayesianBeliefComponent>(Allocator.Temp);

                for (int i = 0; i < droneRowTexts.Count; i++)
                {
                    bool active = i < dStates.Length;
                    if (droneRowTexts[i].gameObject.activeSelf != active) droneRowTexts[i].gameObject.SetActive(active);
                    if (!active) continue;

                    var st = dStates[i];
                    var bel = dBeliefs[i];
                    if (st.IsDestroyed)
                    {
                        droneRowTexts[i].text = $"D{i + 1}  <color=#22C55E><b>CRUSHED / VAPORIZED</b></color>";
                    }
                    else if (st.ActiveAction == DroneActionType.EvadeCrateDodge)
                    {
                        droneRowTexts[i].text = $"D{i + 1}  <color=#F59E0B><b>EVADING CRATE</b></color>  <color=#8A99AD>π={bel.TopClusterConfidence * 100f:F0}%</color>";
                    }
                    else
                    {
                        string stCol = (st.CurrentState == DroneFSMState.Pursue) ? "#EF4444" : "#38BDF8";
                        droneRowTexts[i].text = $"D{i + 1}  <color={stCol}><b>{st.CurrentState.ToString().ToUpperInvariant()}</b></color>  <color=#8A99AD>π=<mspace=0.55em>{bel.TopClusterConfidence * 100f:F0}%</mspace> · H={bel.Entropy:F1}b</color>";
                    }
                }
            }
            droneQuery.Dispose();

            var learnQuery = em.CreateEntityQuery(typeof(DroneLearningState));
            if (learnQuery.CalculateEntityCount() > 0)
            {
                using var lArr = learnQuery.ToComponentDataArray<DroneLearningState>(Allocator.Temp);
                var l = lArr[0];
                float dodgePct = (l.TotalCrateDodgesAttempted > 0)
                    ? (100f * l.TotalCrateDodgesSucceeded / l.TotalCrateDodgesAttempted)
                    : (l.CrateDodgeSkill * 100f);
                droneHeaderText.text = $"DRONE THREAT ({droneCount})  ·  <color=#E2E8F0>AI {l.DroneImprovementPct:F0}%</color>";
                droneLearnText.text = $"ESC {l.TotalEscapesCompleted}  ·  DODGE {dodgePct:F0}%  ·  PAT {l.PatternAdaptationScore * 100f:F0}%";
            }
            learnQuery.Dispose();
        }
    }
}
