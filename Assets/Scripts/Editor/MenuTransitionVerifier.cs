using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace GravityDrift.Editor
{
    [InitializeOnLoad]
    public static class MenuTransitionVerifier
    {
        private const string PhaseKey = "GravityDrift_MenuTestPhase";
        private const string WaitKey = "GravityDrift_MenuTestWait";

        static MenuTransitionVerifier()
        {
            if (SessionState.GetInt(PhaseKey, 0) > 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;
            }
        }

        [MenuItem("GravityDrift/Verify MainMenu Transitions")]
        public static void VerifyMenuTransitions()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            SessionState.SetInt(PhaseKey, 1);
            SessionState.SetInt(WaitKey, 5);
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.isPlaying = true;
        }

        private static void OnEditorUpdate()
        {
            int phase = SessionState.GetInt(PhaseKey, 0);
            if (phase == 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                return;
            }

            if (!EditorApplication.isPlaying) return;

            int wait = SessionState.GetInt(WaitKey, 0);
            if (wait > 0)
            {
                SessionState.SetInt(WaitKey, wait - 1);
                return;
            }

            var ui = Object.FindAnyObjectByType<GameUIManager>();
            var dbg = Object.FindAnyObjectByType<DebugOverlay>();
            var world = World.DefaultGameObjectInjectionWorld;
            if (ui == null || world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var procQ = em.CreateEntityQuery(typeof(ProceduralLevelTag));
            var droneQ = em.CreateEntityQuery(typeof(DroneAITag));
            var objQ = em.CreateEntityQuery(typeof(LevelObjectiveState));

            if (phase == 1)
            {
                int pCnt = procQ.CalculateEntityCount();
                int dCnt = droneQ.CalculateEntityCount();
                float timer = 0f;
                if (objQ.CalculateEntityCount() > 0)
                {
                    using var arr = objQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                    timer = arr[0].RoomTimer;
                }
                var titleTmp = GameObject.Find("GravityDrift_UI_Canvas/MainMenuCard/Title")?.GetComponent<TMPro.TextMeshProUGUI>();
                float titleSize = titleTmp != null ? titleTmp.fontSize : -1f;
                Debug.Log($"[MENU TEST STEP 1 - BOOT] State={ui.currentState} | TimeScale={Time.timeScale} | ProcEntities={pCnt} | Drones={dCnt} | Timer={timer:F2} | TitleFontSize={titleSize}");
                string menuPng = Path.Combine(Application.dataPath, "../Telemetry/UI_MainMenu_Preview.png");
                ScreenCapture.CaptureScreenshot(menuPng);

                SessionState.SetInt(PhaseKey, 10);
                SessionState.SetInt(WaitKey, 3);
            }
            else if (phase == 10)
            {
                ui.OpenSettingsMenu(UIState.MainMenu);
                Debug.Log($"[MENU TEST STEP 1B - SETTINGS] State={ui.currentState} | TimeScale={Time.timeScale}");
                ui.SetConfiguredLives(3);
                ui.StartNewGameFromMenu();
                if (dbg != null) dbg.showDebug = true;
                SessionState.SetInt(PhaseKey, 2);
                SessionState.SetInt(WaitKey, 6);
            }
            else if (phase == 2)
            {
                int pCnt = procQ.CalculateEntityCount();
                int dCnt = droneQ.CalculateEntityCount();
                var sectorTxt = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/ObjectivesOverlay/SectorStatusText")?.GetComponent<TMPro.TextMeshProUGUI>()?.text;
                var reqSwTxt = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/ObjectivesOverlay/ReqSwitchText")?.GetComponent<TMPro.TextMeshProUGUI>()?.text;
                var reqCoresTxt = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/ObjectivesOverlay/ReqCoresText")?.GetComponent<TMPro.TextMeshProUGUI>()?.text;
                var reqAirTxt = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/ObjectivesOverlay/ReqAirlockText")?.GetComponent<TMPro.TextMeshProUGUI>()?.text;
                var optTxt = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/ObjectivesOverlay/OptDronesText")?.GetComponent<TMPro.TextMeshProUGUI>()?.text;
                bool hasOldBoxes = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/TopLeftPanel") != null || GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/TopRightPanel") != null;
                var gravArrow = GameObject.Find("GravityDrift_UI_Canvas/GameplayHUD/BottomTacticalStrip/GravityVectorBox/ArrowHolder/GravityArrow")?.GetComponent<RectTransform>();
                bool arrowStatic = gravArrow != null && gravArrow.anchoredPosition == Vector2.zero && gravArrow.localScale == Vector3.one;
                bool dbgVisible = GameObject.Find("GravityDrift_Debug_Canvas/DebugTelemetryCard")?.activeSelf ?? false;
                Debug.Log($"[MENU TEST STEP 2 - PLAY] State={ui.currentState} | TimeScale={Time.timeScale} | OldBoxes={hasOldBoxes} | ArrowStatic={arrowStatic} | DebugVisible={dbgVisible} | Sector='{sectorTxt}' | Sw='{reqSwTxt}' | Cores='{reqCoresTxt}' | Air='{reqAirTxt}' | Opt='{optTxt}'");
                string hudPng = Path.Combine(Application.dataPath, "../Telemetry/UI_GameplayHUD_Preview.png");
                ScreenCapture.CaptureScreenshot(hudPng);

                SessionState.SetInt(PhaseKey, 3);
                SessionState.SetInt(WaitKey, 4);
            }
            else if (phase == 3)
            {
                ui.OpenPauseMenu();
                float pauseTS = Time.timeScale;
                UIState pauseSt = ui.currentState;
                ui.OpenSessionReport(UIState.PauseMenu);
                UIState reportSt = ui.currentState;
                var plotImg = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard/GraphsRow/LevelsGraphCard/PlotRawImage")?.GetComponent<UnityEngine.UI.RawImage>();
                bool hasChartTex = plotImg != null && plotImg.texture != null;
                ui.ReturnToMainMenu();
                Debug.Log($"[MENU TEST STEP 3 - PAUSE & REPORT] PauseState={pauseSt} | PauseTS={pauseTS} | ReportState={reportSt} | ReportTex={hasChartTex}");

                SessionState.SetInt(PhaseKey, 4);
                SessionState.SetInt(WaitKey, 3);
            }
            else if (phase == 4)
            {
                int pCnt = procQ.CalculateEntityCount();
                int dCnt = droneQ.CalculateEntityCount();
                float timer = 0f;
                if (objQ.CalculateEntityCount() > 0)
                {
                    using var arr = objQ.ToComponentDataArray<LevelObjectiveState>(Allocator.Temp);
                    timer = arr[0].RoomTimer;
                }
                Debug.Log($"[MENU TEST STEP 4 - RETURN] State={ui.currentState} | TimeScale={Time.timeScale} | ProcEntities={pCnt} | Drones={dCnt} | Timer={timer:F2}");

                SessionState.SetInt(PhaseKey, 0);
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.isPlaying = false;
            }

            procQ.Dispose();
            droneQ.Dispose();
            objQ.Dispose();
        }
    }
}
