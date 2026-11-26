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
        [SerializeField] private Color mainMenuScrimColor = new Color32(0, 0, 0, 255);
        [SerializeField] private Color pauseScrimColor = new Color32(0, 0, 0, 210);
        [SerializeField] private bool swapSelectedButtonSprites = true;
        [SerializeField] private Sprite activeSelectionSprite;
        [SerializeField] private Sprite inactiveSelectionSprite;
        [SerializeField] private bool colorCodeDynamicHudStates = false;
        [SerializeField] private Color livesHealthyColor = Color.white;
        [SerializeField] private Color livesWarningColor = Color.white;
        [SerializeField] private Color livesCriticalColor = Color.white;
        [SerializeField] private Color accentFlashColor = Color.white;
        [SerializeField] private Color dangerColor = Color.white;
        [SerializeField] private Color overloadDimColor = new Color32(120, 120, 120, 255);
        [SerializeField] private Color runOverVictoryColor = Color.white;
        [SerializeField] private Color runOverDefeatColor = Color.white;

        private string seedInputString = "101";
        private int selectedLevel = 1;
        private float lastEscToggleTime = -10f;
        private bool listenersWired = false;
        private Color cachedJetpackFillColor = Color.white;
        private Color cachedJetpackPctColor = Color.white;
        private Color cachedGravityArrowColor = Color.white;

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
        [SerializeField] private TextMeshProUGUI sectorStatusText;
        [SerializeField] private TextMeshProUGUI reqSwitchText;
        [SerializeField] private TextMeshProUGUI reqCoresText;
        [SerializeField] private TextMeshProUGUI reqAirlockText;
        [SerializeField] private TextMeshProUGUI optDronesText;
        [SerializeField] private TextMeshProUGUI jetpackLabelText;
        [SerializeField] private TextMeshProUGUI jetpackPctText;
        [SerializeField] private RectTransform jetpackFillRt;
        [SerializeField] private UnityEngine.UI.Image jetpackFillImg;
        [SerializeField] private RectTransform gravityArrowRt;
        [SerializeField] private UnityEngine.UI.Image gravityArrowImg;
        [SerializeField] private TextMeshProUGUI gravityLabelText;

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
        }

        private void EnsureReportTextures()
        {
            if (levelsChartTex == null)
                levelsChartTex = new Texture2D(500, 450, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            if (aiChartTex == null)
                aiChartTex = new Texture2D(500, 450, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            if (deathChartTex == null)
                deathChartTex = new Texture2D(500, 450, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };

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
                else if (currentState == UIState.Gameplay || currentState == UIState.PauseMenu || currentState == UIState.RunOver)
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
            if (returnTo == UIState.RunOver)
            {
                SessionReportRecorder.Instance?.CheckAndRecordTagDeaths();
                SessionReportRecorder.Instance?.ArchiveRunEndIfPending("RunOver");
            }
            SetUIState(UIState.SessionReport);
        }

        public void OpenRunOverScreen(bool isVictory)
        {
            if (currentState == UIState.SessionReport && menuReturnState == UIState.RunOver)
            {
                return;
            }
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
            if (sessionReportRoot != null)
            {
                bool showReport = currentState == UIState.SessionReport;
                sessionReportRoot.SetActive(showReport);
                if (showReport) sessionReportRoot.transform.SetAsLastSibling();
            }
            if (runOverRoot != null)
            {
                bool showRunOver = currentState == UIState.RunOver;
                runOverRoot.SetActive(showRunOver);
                if (showRunOver) runOverRoot.transform.SetAsLastSibling();
            }
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
            levelStepperText = GravityDriftUITheme.CreateText(lvlRow, "LevelLabel", $"STARTING LEVEL  {selectedLevel} / 20", 14f, GravityDriftUITheme.TextPrimary, true);
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
                levelStepperText.text = $"STARTING LEVEL  {selectedLevel} / 20";
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
                    ? "DUALSENSE CONNECTED (TOUCHPAD + HAPTICS)"
                    : "KEYBOARD READY (CONNECT DUALSENSE VIA USB/BT)";
                controllerStatusText.text = $"INPUT: {conn}  |  SCHEME: {InputHelper.activeScheme}";
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

            CreateReportGraphCard(graphsRow, "LevelsGraphCard", "FIG 1. LEVELS PLAYED & PROGRESSION",
                "Bars: Clear (Green) / Death (Red) | Lines: Time s (Blue), Complexity (Orange)\nY-Axis: Room Time (s) & Complexity %  |  X-Axis: Attempt #",
                levelsChartTex, out levelsChartImage, out levelsChartFooterText);

            CreateReportGraphCard(graphsRow, "AiInputGraphCard", "FIG 2. AI IMPROVEMENT PER INPUT",
                "Blue: Posterior pi | Orange: Entropy Precision | Green: AI Imp % | Black: Pat %\nY-Axis: Metric Score (0-100%)  |  X-Axis: Input Event #",
                aiChartTex, out aiChartImage, out aiChartFooterText);

            CreateReportGraphCard(graphsRow, "DeathRateGraphCard", "FIG 3. DEATH RATE & IMPROVEMENT",
                "Red: Death Rate % | Blue: AI Imp % | Green: Crate-Dodge Skill %\nY-Axis: Rate / Skill (0-100%)  |  X-Axis: Level Attempt #",
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

            var leg = GravityDriftUITheme.CreateText(panel, "CardLegend", legendStr, 11f, GravityDriftUITheme.TextSecondary);
            leg.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            var imgGo = new GameObject("PlotRawImage", typeof(RectTransform), typeof(RawImage));
            imgGo.transform.SetParent(panel, false);
            var imgLe = imgGo.AddComponent<LayoutElement>();
            imgLe.flexibleHeight = 1f;
            imgLe.minHeight = 360f;
            rawImg = imgGo.GetComponent<RawImage>();
            rawImg.texture = tex;
            rawImg.color = Color.white;

            footerTxt = GravityDriftUITheme.CreateText(panel, "CardFooter", "Samples recorded: 0", 12f, GravityDriftUITheme.TextPrimary);
            footerTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        }

        public void RefreshSessionReportPanel()
        {
            EnsureReportTextures();
            var rec = SessionReportRecorder.Instance;
            bool includeInProgress = menuReturnState != UIState.RunOver && menuReturnState != UIState.MainMenu;
            var levelRows = (rec != null) ? rec.GetLevelRecordsSnapshot(includeCurrentInProgress: includeInProgress) : new List<SessionLevelRecord>();
            IReadOnlyList<PerInputAIEvent> inputEvts = (rec != null) ? rec.InputEvents : System.Array.Empty<PerInputAIEvent>();

            SessionGraphRenderer.RenderLevelsChart(levelsChartTex, levelRows);
            SessionGraphRenderer.RenderPerInputAIChart(aiChartTex, inputEvts);
            SessionGraphRenderer.RenderDeathAndImprovementChart(deathChartTex, levelRows);

            if (rec == null || (levelRows.Count == 0 && inputEvts.Count == 0))
            {
                if (reportKpiText != null)
                    reportKpiText.text = "NO LIVE SESSION DATA YET (N = 0 LEVELS, 0 INPUTS)  |  START A MISSION TO RECORD TELEMETRY";
                if (levelsChartFooterText != null) levelsChartFooterText.text = "X: Attempt Index | Y: Time (s) & Complexity % | Recorded Samples: n = 0 attempts";
                if (aiChartFooterText != null) aiChartFooterText.text = "X: Input Event # | Y: Confidence / Precision / Imp (%) | Recorded Samples: n = 0 inputs";
                if (deathChartFooterText != null) deathChartFooterText.text = "X: Level Attempt # | Y: Death Rate / Skill (%) | Recorded Samples: n = 0 levels";
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
                    $"SESSION {rec.CurrentSessionId}  |  SECTORS: {distinctLevels.Count}/20 ({cleared} CLEARED)  |  " +
                    $"LIVES: {latestLivesRem}/{latestMaxLives}  |  DEATHS: {latestDeathRate:F1}%  |  PAT ADAPT: {latestPat:F0}%  |  SAMPLES: {levelRows.Count} LVL / {inputEvts.Count} IN";
            }

            if (levelsChartFooterText != null)
            {
                levelsChartFooterText.text = $"X: Attempt # (n = {levelRows.Count} attempts)  |  Y: Time (s) & Complexity  |  Cleared: {cleared}/20, Mean: {avgTime:F1}s";
            }

            if (aiChartFooterText != null)
            {
                if (inputEvts.Count > 0)
                {
                    var first = inputEvts[0];
                    var last = inputEvts[inputEvts.Count - 1];
                    aiChartFooterText.text = $"X: Input Event # (n = {inputEvts.Count} inputs)  |  Y: 0-100%  |  pi: {first.top_cluster_confidence * 100f:F0}%->{last.top_cluster_confidence * 100f:F0}%, Pat: {last.pattern_adaptation_pct:F0}%";
                }
                else
                {
                    aiChartFooterText.text = "X: Input Event # (n = 0 inputs)  |  Y: 0-100%  |  Awaiting player input samples...";
                }
            }

            if (deathChartFooterText != null)
            {
                deathChartFooterText.text = $"X: Level # (n = {levelRows.Count} levels)  |  Y: 0-100%  |  Death: {latestDeathRate:F1}%, AI Imp: {latestImp:F1}%, Dodge: {latestDodge:F0}%";
            }

            if (reportSaveStatusText != null)
            {
                string pathNote = string.IsNullOrEmpty(rec.LastSavedProjectDir) ? "Telemetry/Sessions/" : rec.LastSavedProjectDir;
                reportSaveStatusText.text = $"{rec.LastSavedStatus}  [{pathNote}]";
            }
            Canvas.ForceUpdateCanvases();
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
                    $"<b>CLEARED:</b> {cleared}  |  <b>ATTEMPTS:</b> {rows}  |  <b>DEATH RATE:</b> {latestDeathRate:F1}%\n" +
                    $"<b>DRONE AI IMP:</b> {latestImp:F1}%  |  <b>PATTERN ADAPT:</b> {latestPat:F0}%\n" +
                    $"{(rec != null ? rec.LastSavedStatus : "Session telemetry archived.")}";
            }
        }

        private void BuildGameplayHUD(Transform parent)
        {
            var hudGo = new GameObject("GameplayHUD", typeof(RectTransform));
            hudGo.transform.SetParent(parent, false);
            var hudRt = hudGo.GetComponent<RectTransform>();
            GravityDriftUITheme.StretchFill(hudRt);
            gameplayHudRoot = hudGo;

            // 1. UNBOXED TOP-LEFT OBJECTIVES OVERLAY (Plain white text directly on screen, zero boxes/cards)
            var objOverlay = new GameObject("ObjectivesOverlay", typeof(RectTransform));
            objOverlay.transform.SetParent(hudRt, false);
            var objRt = objOverlay.GetComponent<RectTransform>();
            objRt.anchorMin = new Vector2(0f, 1f);
            objRt.anchorMax = new Vector2(0f, 1f);
            objRt.pivot = new Vector2(0f, 1f);
            objRt.anchoredPosition = new Vector2(24f, -18f);
            objRt.sizeDelta = new Vector2(960f, 132f);

            var objVlg = objOverlay.AddComponent<VerticalLayoutGroup>();
            objVlg.spacing = 4f;
            objVlg.childControlWidth = true;
            objVlg.childControlHeight = true;
            objVlg.childForceExpandWidth = true;
            objVlg.childForceExpandHeight = false;

            sectorStatusText = GravityDriftUITheme.CreateText(objRt, "SectorStatusText", "LEVEL 01/20  |  LIVES 5/5  |  SEED #101", 15f, Color.white, false);
            sectorStatusText.fontStyle = FontStyles.Bold;
            sectorStatusText.raycastTarget = false;
            sectorStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            reqSwitchText = GravityDriftUITheme.CreateText(objRt, "ReqSwitchText", "Required: Flip the switch [PENDING]", 14f, Color.white, false);
            reqSwitchText.raycastTarget = false;
            reqSwitchText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            reqCoresText = GravityDriftUITheme.CreateText(objRt, "ReqCoresText", "Required: Collect all orbs/cores (0/0)", 14f, Color.white, false);
            reqCoresText.raycastTarget = false;
            reqCoresText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            reqAirlockText = GravityDriftUITheme.CreateText(objRt, "ReqAirlockText", "Required: Reach the exit airlock [LOCKED]", 14f, Color.white, false);
            reqAirlockText.raycastTarget = false;
            reqAirlockText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            optDronesText = GravityDriftUITheme.CreateText(objRt, "OptDronesText", "Optional: Defeat drones (0/0)", 14f, Color.white, false);
            optDronesText.raycastTarget = false;
            optDronesText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            // 2. BOTTOM HUD STRIP ONLY: Jetpack Fuel Bar + Static Gravity Vector Arrow & Charges
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
            jetpackLabelText = GravityDriftUITheme.CreateText(fuelTop, "JetLabel", "JETPACK FUEL", 13f, Color.white, true);
            jetpackLabelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            jetpackPctText = GravityDriftUITheme.CreateText(fuelTop, "JetPct", "100%", 14f, Color.white, true, TextAlignmentOptions.Right);
            jetpackPctText.gameObject.AddComponent<LayoutElement>().preferredWidth = 65f;

            var barBg = GravityDriftUITheme.CreatePanel(fuelCol, "FuelBarBg", GravityDriftUITheme.GetControlSprite(), Color.white);
            barBg.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            jetpackFillRt = GravityDriftUITheme.CreatePanel(barBg, "FuelBarFill", GravityDriftUITheme.GetSolidSprite(), Color.white);
            jetpackFillRt.anchorMin = Vector2.zero;
            jetpackFillRt.anchorMax = Vector2.one;
            jetpackFillRt.offsetMin = new Vector2(2f, 2f);
            jetpackFillRt.offsetMax = new Vector2(-2f, -2f);
            jetpackFillImg = jetpackFillRt.GetComponent<UnityEngine.UI.Image>();

            var vSep = GravityDriftUITheme.CreatePanel(bot, "Sep", GravityDriftUITheme.GetSolidSprite(), GravityDriftUITheme.BorderHairline);
            vSep.gameObject.AddComponent<LayoutElement>().preferredWidth = 1f;

            var gravCol = CreateHorizontalRow(bot, "GravityVectorBox", 48f, 10f);
            gravCol.gameObject.GetComponent<LayoutElement>().preferredWidth = 185f;
            gravCol.gameObject.GetComponent<LayoutElement>().flexibleWidth = 0f;

            var arrowHolder = GravityDriftUITheme.CreatePanel(gravCol, "ArrowHolder", GravityDriftUITheme.GetControlSprite(), Color.white);
            var ahLe = arrowHolder.gameObject.AddComponent<LayoutElement>();
            ahLe.preferredWidth = 42f;
            ahLe.preferredHeight = 42f;

            gravityArrowRt = GravityDriftUITheme.CreatePanel(arrowHolder, "GravityArrow", GravityDriftUITheme.GetGravityArrowSprite(), Color.white);
            gravityArrowRt.anchorMin = new Vector2(0.5f, 0.5f);
            gravityArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
            gravityArrowRt.sizeDelta = new Vector2(26f, 26f);
            gravityArrowImg = gravityArrowRt.GetComponent<UnityEngine.UI.Image>();

            gravityLabelText = GravityDriftUITheme.CreateText(gravCol, "GravText", "GRAV: DOWN\nCHARGES 3/3", 12f, Color.white, true);
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

            // Count Drones cleanly for optional objective readout
            int totalDrones = 0;
            int destroyedDrones = 0;
            var droneQuery = em.CreateEntityQuery(typeof(DroneState));
            totalDrones = droneQuery.CalculateEntityCount();
            if (totalDrones > 0)
            {
                using var dStates = droneQuery.ToComponentDataArray<DroneState>(Allocator.Temp);
                for (int i = 0; i < dStates.Length; i++)
                {
                    if (dStates[i].IsDestroyed) destroyedDrones++;
                }
            }
            droneQuery.Dispose();

            // 1. Plain Unboxed Objectives & Sector Status
            var objQuery = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (objQuery.CalculateEntityCount() > 0)
            {
                using var objArray = objQuery.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                var lvl = objArray[0];

                int maxL = Mathf.Max(3, lvl.MaxLives);
                int remL = Mathf.Clamp(lvl.LivesRemaining, 0, maxL);

                if (sectorStatusText != null)
                {
                    sectorStatusText.text = $"LEVEL {lvl.LevelIndex:00}/20  |  LIVES {remL}/{maxL}  |  SEED #{lvl.CurrentSeed}";
                }

                string swState = lvl.SwitchFlipped ? "DONE" : "PENDING";
                string exitState = lvl.RoomCleared ? "CLEARED" : (lvl.AirlockUnlocked ? "OPEN" : "LOCKED");

                if (reqSwitchText != null)
                {
                    reqSwitchText.text = $"Required: Flip the switch [{swState}]";
                }

                if (reqCoresText != null)
                {
                    reqCoresText.text = $"Required: Collect all orbs/cores ({lvl.CoresCollected}/{lvl.TotalCores})";
                }

                if (reqAirlockText != null)
                {
                    reqAirlockText.text = $"Required: Reach the exit airlock [{exitState}]";
                }

                if (optDronesText != null)
                {
                    optDronesText.text = $"Optional: Defeat drones ({destroyedDrones}/{totalDrones})";
                }
            }
            objQuery.Dispose();

            // 2. Jetpack Fuel / Boost / Overload (Bottom HUD)
            var playerQuery = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
            if (playerQuery.CalculateEntityCount() > 0)
            {
                using var jetArray = playerQuery.ToComponentDataArray<JetpackComponent>(Allocator.Temp);
                var jet = jetArray[0];
                float fuelPct = Mathf.Clamp01(jet.Fuel / Mathf.Max(1f, jet.MaxFuel));

                if (jet.IsInCooldown)
                {
                    if (jetpackLabelText != null) jetpackLabelText.text = $"JETPACK OVERLOADED ({jet.CooldownTimer:F1}s)";
                    if (jetpackPctText != null)
                    {
                        jetpackPctText.text = "0%";
                        jetpackPctText.color = colorCodeDynamicHudStates ? dangerColor : cachedJetpackPctColor;
                    }
                    if (jetpackFillImg != null)
                    {
                        float blink = Mathf.PingPong(Time.unscaledTime * 11f, 1f);
                        jetpackFillImg.color = (blink > 0.42f) ? cachedJetpackFillColor : overloadDimColor;
                    }
                    if (jetpackFillRt != null) jetpackFillRt.anchorMax = new Vector2(0.08f, 1f);
                }
                else
                {
                    if (jetpackLabelText != null)
                    {
                        jetpackLabelText.text = jet.IsBoosting
                            ? "JETPACK  [BOOST]"
                            : (jet.IsThrusting ? "JETPACK  [THRUST]" : "JETPACK  [RECHARGING]");
                    }
                    if (jetpackPctText != null)
                    {
                        jetpackPctText.text = $"{fuelPct * 100f:F0}%";
                        jetpackPctText.color = cachedJetpackPctColor;
                    }
                    if (jetpackFillImg != null) jetpackFillImg.color = cachedJetpackFillColor;
                    if (jetpackFillRt != null) jetpackFillRt.anchorMax = new Vector2(Mathf.Max(0.01f, fuelPct), 1f);
                }
            }
            playerQuery.Dispose();

            // 3. Static Gravity Direction Indicator (No animation/bobbing — purely flipped/rotated)
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
                if (gravityArrowRt != null)
                {
                    gravityArrowRt.localRotation = Quaternion.Euler(0f, 0f, angleZ);
                    gravityArrowRt.anchoredPosition = Vector2.zero;
                    gravityArrowRt.localScale = Vector3.one;
                }
                if (gravityArrowImg != null)
                {
                    gravityArrowImg.color = cachedGravityArrowColor;
                }

                if (gravityLabelText != null)
                {
                    gravityLabelText.text = $"GRAV: {g.CurrentDirection.ToString().ToUpperInvariant()}\nCHARGES {g.CurrentCharges}/{g.MaxCharges}";
                }
            }
            gravQuery.Dispose();
        }
    }
}
