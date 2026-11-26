using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace GravityDrift.Editor
{
    [InitializeOnLoad]
    public static class SessionReportVerifier
    {
        private const string PhaseKey = "GravityDrift_ReportTestPhase";
        private const string WaitKey = "GravityDrift_ReportTestWait";

        static SessionReportVerifier()
        {
            if (SessionState.GetInt(PhaseKey, 0) > 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;
            }
        }

        [MenuItem("GravityDrift/Verify Session Report & Graphs")]
        public static void VerifySessionReport()
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

        [MenuItem("GravityDrift/Verify RunOver -> SessionReport")]
        public static void VerifyRunOverSessionReport()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            SessionState.SetString("GravityDrift_LastRunOverReportVerify", string.Empty);
            SessionState.SetInt(PhaseKey, 100);
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
            var pcg = Object.FindAnyObjectByType<PCGRoomGenerator>();
            var world = World.DefaultGameObjectInjectionWorld;
            if (ui == null || world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            if (phase == 1)
            {
                // Start 3-Lives Roguelite Run at Level 1 (SplitBaffles)
                ui.SetConfiguredLives(3);
                ui.StartNewGameFromMenu();
                SessionState.SetInt(PhaseKey, 2);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 2)
            {
                // Predictable repeated flip & lane -> PatternAdaptation climbs
                SimulateStep(em, "DualSense_Touchpad", true, 0, new float2(1f, 0f), false, PlayerActionType.GravityFlip, adaptUp: true);
                SessionState.SetInt(PhaseKey, 3);
                SessionState.SetInt(WaitKey, 3);
            }
            else if (phase == 3)
            {
                TriggerRoomClear(em); // Advances to Lv 2 (CentralCitadel)
                SessionState.SetInt(PhaseKey, 4);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 4)
            {
                // Break pattern -> PatternAdaptation & AI regress
                SimulateStep(em, "DualSense_Touchpad", true, 2, new float2(-1f, 0.5f), true, PlayerActionType.JetpackBoost, adaptUp: false);
                TriggerRoomClear(em); // Advances to Lv 3 (ZigZagGauntlet)
                SessionState.SetInt(PhaseKey, 5);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 5)
            {
                SimulateStep(em, "PC_WASD", true, 0, new float2(1f, 0f), false, PlayerActionType.ObjectivePush, adaptUp: true);
                TriggerRoomClear(em); // Advances to Lv 4 (TwinChambers)
                SessionState.SetInt(PhaseKey, 6);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 6)
            {
                TriggerRoomClear(em); // Advances to Lv 5 (VerticalShafts)
                SessionState.SetInt(PhaseKey, 7);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 7)
            {
                // Lose 1 life cleanly on Lv 5 (Lives 3 -> 2), then advance to Lv 14 (5 drones)
                SimulateLifeLoss(em);
                SessionState.SetInt(PhaseKey, 8);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 8)
            {
                SessionReportRecorder.Instance?.ArchiveBeforeLevelChange("Cleared", saveImmediately: false);
                if (pcg != null) pcg.GenerateLevel(14, 290); // 5 drones!
                SessionState.SetInt(PhaseKey, 9);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 9)
            {
                // Predictable flip punished on Lv 14 -> jump to Lv 20
                SimulateStep(em, "PC_ArrowsOrKeys", true, 0, new float2(1f, 0f), false, PlayerActionType.CrateAttack, adaptUp: true);
                SessionReportRecorder.Instance?.ArchiveBeforeLevelChange("Cleared", saveImmediately: false);
                if (pcg != null) pcg.GenerateLevel(20, 368);
                SessionState.SetInt(PhaseKey, 10);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 10)
            {
                // Clear Level 20 -> triggers RunOver Victory screen cleanly!
                SimulateStep(em, "DualSense_Stick", false, 0, new float2(0.8f, 0.2f), true, PlayerActionType.ObjectivePush, adaptUp: true);
                TriggerRoomClear(em);
                SessionState.SetInt(PhaseKey, 11);
                SessionState.SetInt(WaitKey, 6);
            }
            else if (phase == 11)
            {
                string runOverPng = Path.Combine(Application.dataPath, "../Telemetry/UI_RunOver_Preview.png");
                ScreenCapture.CaptureScreenshot(runOverPng);
                Debug.Log($"[ROGUELITE VERIFY] RunOverState={ui.currentState} | LevelsClearedReached=20");
                SessionState.SetInt(PhaseKey, 12);
                SessionState.SetInt(WaitKey, 3);
            }
            else if (phase == 12)
            {
                ui.OpenSessionReport(UIState.RunOver);
                SessionState.SetInt(PhaseKey, 13);
                SessionState.SetInt(WaitKey, 4);
            }
            else if (phase == 13)
            {
                string reportPng = Path.Combine(Application.dataPath, "../Telemetry/UI_SessionReport_Preview.png");
                ScreenCapture.CaptureScreenshot(reportPng);
                var rec = SessionReportRecorder.Instance;
                Debug.Log($"[REPORT VERIFY] Session={rec?.CurrentSessionId} | Levels={rec?.LevelRecords.Count} | Inputs={rec?.InputEvents.Count} | Status={rec?.LastSavedStatus}");
                SessionState.SetInt(PhaseKey, 14);
                SessionState.SetInt(WaitKey, 3);
            }
            else if (phase == 14)
            {
                ui.ReturnToMainMenu();
                SessionState.SetInt(PhaseKey, 0);
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.isPlaying = false;
            }
            else if (phase == 100)
            {
                ui.SetConfiguredLives(3);
                ui.StartNewGameFromMenu();
                SessionState.SetInt(PhaseKey, 101);
                SessionState.SetInt(WaitKey, 5);
            }
            else if (phase == 101)
            {
                SimulateStep(em, "PC_WASD", true, 0, new float2(1f, 0f), false, PlayerActionType.GravityFlip, adaptUp: true);
                SimulateStep(em, "DualSense_Stick", true, 2, new float2(-1f, 0.2f), true, PlayerActionType.JetpackBoost, adaptUp: false);
                SimulateLifeLoss(em);
                SimulateLifeLoss(em);
                SimulateLifeLoss(em);
                SessionState.SetInt(PhaseKey, 102);
                SessionState.SetInt(WaitKey, 6);
            }
            else if (phase == 102)
            {
                var runOverCard = GameObject.Find("GravityDrift_UI_Canvas/RunOverCard");
                var reportBtn = GameObject.Find("GravityDrift_UI_Canvas/RunOverCard/RunOverReportBtn")?.GetComponent<UnityEngine.UI.Button>();
                bool initialRunOver = (ui.currentState == UIState.RunOver) && (runOverCard != null && runOverCard.activeInHierarchy);
                SessionState.SetBool("GD_RO_Step1", initialRunOver);
                if (reportBtn != null) reportBtn.onClick.Invoke();
                SessionState.SetInt(PhaseKey, 103);
                SessionState.SetInt(WaitKey, 10);
            }
            else if (phase == 103)
            {
                var runOverCard = GameObject.Find("GravityDrift_UI_Canvas/RunOverCard");
                var reportCard = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard");
                var kpiTxt = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard/ReportKpiStrip/ReportKpiText")?.GetComponent<TMPro.TextMeshProUGUI>()?.text ?? "";
                var lvlImg = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard/GraphsRow/LevelsGraphCard/PlotRawImage")?.GetComponent<UnityEngine.UI.RawImage>();
                var aiImg = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard/GraphsRow/AiInputGraphCard/PlotRawImage")?.GetComponent<UnityEngine.UI.RawImage>();
                var deathImg = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard/GraphsRow/DeathRateGraphCard/PlotRawImage")?.GetComponent<UnityEngine.UI.RawImage>();

                bool stayedReport10Frames = (ui.currentState == UIState.SessionReport)
                    && (reportCard != null && reportCard.activeInHierarchy)
                    && (runOverCard == null || !runOverCard.activeInHierarchy);
                bool chartsAndDataReady = lvlImg != null && lvlImg.texture != null
                    && aiImg != null && aiImg.texture != null
                    && deathImg != null && deathImg.texture != null
                    && !string.IsNullOrEmpty(kpiTxt) && !kpiTxt.Contains("NO LIVE SESSION DATA");

                SessionState.SetBool("GD_RO_Step2", stayedReport10Frames);
                SessionState.SetBool("GD_RO_Charts", chartsAndDataReady);
                SessionState.SetString("GD_RO_KPI", kpiTxt);

                string reportPng = Path.Combine(Application.dataPath, "../Telemetry/UI_SessionReport_Preview.png");
                ScreenCapture.CaptureScreenshot(reportPng);

                SessionState.SetInt(PhaseKey, 104);
                SessionState.SetInt(WaitKey, 2);
            }
            else if (phase == 104)
            {
                var backBtn = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard/ReportFooterRow/ReportBackBtn")?.GetComponent<UnityEngine.UI.Button>();
                if (backBtn != null) backBtn.onClick.Invoke();
                SessionState.SetInt(PhaseKey, 105);
                SessionState.SetInt(WaitKey, 4);
            }
            else if (phase == 105)
            {
                var runOverCard = GameObject.Find("GravityDrift_UI_Canvas/RunOverCard");
                var reportCard = GameObject.Find("GravityDrift_UI_Canvas/SessionReportCard");
                bool returnedRunOver = (ui.currentState == UIState.RunOver)
                    && (runOverCard != null && runOverCard.activeInHierarchy)
                    && (reportCard == null || !reportCard.activeInHierarchy);

                bool step1 = SessionState.GetBool("GD_RO_Step1", false);
                bool step2 = SessionState.GetBool("GD_RO_Step2", false);
                bool charts = SessionState.GetBool("GD_RO_Charts", false);
                string kpi = SessionState.GetString("GD_RO_KPI", "");
                string summary = $"[RUNOVER REPORT VERIFY] RunOverShown={step1} | StayedInReport10Frames={step2} | ChartsAndDataReady={charts} | BackToRunOver={returnedRunOver} | KPI='{kpi}'";
                SessionState.SetString("GravityDrift_LastRunOverReportVerify", summary);
                Debug.Log(summary);

                SessionState.SetInt(PhaseKey, 0);
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.isPlaying = false;
            }
        }

        private static void TriggerRoomClear(EntityManager em)
        {
            var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (oQ.CalculateEntityCount() > 0)
            {
                using var arr = oQ.ToEntityArray(Allocator.Temp);
                var lvl = em.GetComponentData<LevelObjectiveState>(arr[0]);
                lvl.SwitchFlipped = true;
                lvl.AirlockUnlocked = true;
                lvl.AirlockFullyOpen = true;
                lvl.RoomCleared = true;
                em.SetComponentData(arr[0], lvl);
            }
            oQ.Dispose();
        }

        private static void SimulateLifeLoss(EntityManager em)
        {
            var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (oQ.CalculateEntityCount() > 0)
            {
                using var arr = oQ.ToEntityArray(Allocator.Temp);
                var lvl = em.GetComponentData<LevelObjectiveState>(arr[0]);
                lvl.LivesRemaining = Mathf.Max(0, lvl.LivesRemaining - 1);
                if (lvl.LivesRemaining <= 0) lvl.IsRunOver = true;
                em.SetComponentData(arr[0], lvl);
            }
            oQ.Dispose();

            var sQ = em.CreateEntityQuery(typeof(PlayerCounterplayStats));
            if (sQ.CalculateEntityCount() > 0)
            {
                using var arr = sQ.ToEntityArray(Allocator.Temp);
                var st = em.GetComponentData<PlayerCounterplayStats>(arr[0]);
                st.TimesTaggedByDrone++;
                em.SetComponentData(arr[0], st);
            }
            sQ.Dispose();
        }

        private static void SimulateStep(
            EntityManager em,
            string inputSrc,
            bool bumpFlip,
            byte flipDir,
            float2 moveVec,
            bool bumpBoostOverload,
            PlayerActionType pAct,
            bool adaptUp)
        {
            if (bumpFlip)
            {
                InputHelper.lastGravityInputSource = inputSrc;
                var gQ = em.CreateEntityQuery(typeof(GravityState));
                if (gQ.CalculateEntityCount() > 0)
                {
                    using var arr = gQ.ToEntityArray(Allocator.Temp);
                    var g = em.GetComponentData<GravityState>(arr[0]);
                    g.CurrentDirection = (GravityDirection)flipDir;
                    g.TotalFlips++;
                    g.DirectionChangedThisFrame = true;
                    em.SetComponentData(arr[0], g);
                }
                gQ.Dispose();

                var stQ = em.CreateEntityQuery(typeof(PlayerCounterplayStats));
                if (stQ.CalculateEntityCount() > 0)
                {
                    using var arr = stQ.ToEntityArray(Allocator.Temp);
                    var st = em.GetComponentData<PlayerCounterplayStats>(arr[0]);
                    st.GravityFlipsUsed++;
                    em.SetComponentData(arr[0], st);
                }
                stQ.Dispose();
            }
            else
            {
                InputHelper.lastThrustInputSource = inputSrc;
                InputHelper.lastBoostInputSource = inputSrc == "DualSense_R2" ? "DualSense_R2" : "PC_Boost";
            }

            if (bumpBoostOverload)
            {
                var jQ = em.CreateEntityQuery(typeof(PlayerTag), typeof(JetpackComponent));
                if (jQ.CalculateEntityCount() > 0)
                {
                    using var arr = jQ.ToEntityArray(Allocator.Temp);
                    var jet = em.GetComponentData<JetpackComponent>(arr[0]);
                    jet.IsThrusting = true;
                    jet.IsBoosting = true;
                    jet.OverloadCount++;
                    jet.TotalBoostTime += 0.45f;
                    em.SetComponentData(arr[0], jet);
                }
                jQ.Dispose();
            }

            var oQ = em.CreateEntityQuery(typeof(LevelObjectiveState));
            if (oQ.CalculateEntityCount() > 0)
            {
                using var arr = oQ.ToEntityArray(Allocator.Temp);
                var lvl = em.GetComponentData<LevelObjectiveState>(arr[0]);
                lvl.CurrentPlayerAction = pAct;
                em.SetComponentData(arr[0], lvl);
            }
            oQ.Dispose();

            var lQ = em.CreateEntityQuery(typeof(DroneLearningState));
            if (lQ.CalculateEntityCount() > 0)
            {
                using var arr = lQ.ToEntityArray(Allocator.Temp);
                var l = em.GetComponentData<DroneLearningState>(arr[0]);
                DroneAISystem.UpdatePlayerPatternAdaptation(ref l, bumpFlip, flipDir, moveVec * 6f, bumpBoostOverload, 0.25f);
                if (adaptUp)
                {
                    l.TotalEscapesCompleted++;
                    l.TotalCrateDodgesAttempted++;
                    l.TotalCrateDodgesSucceeded++;
                    DroneAISystem.ApplyAdaptationBoost(ref l, 0.14f, 0.25f, 0.08f, 0.28f);
                }
                else
                {
                    l.TotalCrateDodgesAttempted++;
                    DroneAISystem.ApplyOutplayRegression(ref l, 0.16f, 0.22f, 0.10f, 0.35f);
                }
                em.SetComponentData(arr[0], l);
            }
            lQ.Dispose();
        }
    }
}
