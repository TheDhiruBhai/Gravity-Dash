using System.Text;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

namespace GravityDrift
{
    /// <summary>
    /// Minimalist AI Debug Overlay (toggled via Tab / DualSense Create).
    /// Anchored Bottom-Left without colliding with Top-Left HUD or Bottom-Center Tactical Strip.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        public bool showDebug = false;
        public bool showBeliefHeatmap = true;
        public bool showGridCells = true;

        [Header("Editable Canvas UI References")]
        [SerializeField] private Canvas debugCanvas;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI bodyText;

        private void Awake()
        {
            if (panelRoot == null || bodyText == null)
            {
                BuildDebugPanel();
            }
            else
            {
                panelRoot.SetActive(false);
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f) return;

            if (InputHelper.GetDebugToggleDown())
            {
                showDebug = !showDebug;
            }
        }

        private void LateUpdate()
        {
            if (panelRoot == null) BuildDebugPanel();

            bool shouldShow = showDebug && (GameUIManager.Instance == null || GameUIManager.Instance.currentState == UIState.Gameplay);
            if (panelRoot.activeSelf != shouldShow)
            {
                panelRoot.SetActive(shouldShow);
            }

            if (shouldShow)
            {
                RefreshTelemetryText();
            }
        }

        private void BuildDebugPanel()
        {
            if (debugCanvas != null) return;

            debugCanvas = GravityDriftUITheme.CreateMainCanvas("GravityDrift_Debug_Canvas", 20);
            var panel = GravityDriftUITheme.CreatePanel(debugCanvas.transform, "DebugTelemetryCard", GravityDriftUITheme.GetPanelSprite(), Color.white);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 0f);
            panel.pivot = new Vector2(0f, 0f);
            panel.anchoredPosition = new Vector2(20f, 18f);
            panel.sizeDelta = new Vector2(430f, 245f);
            panelRoot = panel.gameObject;

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 10, 10);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var hdr = GravityDriftUITheme.CreateText(panel, "DebugHdr", "BAYESIAN POSTERIOR (π) & GRID A*  [TAB]", 13f, GravityDriftUITheme.AccentSkyBlue, true);
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            bodyText = GravityDriftUITheme.CreateText(panel, "DebugBody", "", 11.5f, GravityDriftUITheme.TextPrimary);
            bodyText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            panelRoot.SetActive(false);
        }

        private void RefreshTelemetryText()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var sb = new StringBuilder();
            sb.AppendLine("<color=#8A99AD>π_t(s) ~ P(o_t|s, a_p, a_d) · Σ T(s|s', a_d, g_t)π_{t-1}(s')</color>");

            var droneQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<DroneState>(),
                ComponentType.ReadOnly<DroneTelemetry>(),
                ComponentType.ReadOnly<BayesianBeliefComponent>(),
                ComponentType.ReadOnly<LocalTransform>());

            using var states = droneQuery.ToComponentDataArray<DroneState>(Allocator.Temp);
            using var telemetries = droneQuery.ToComponentDataArray<DroneTelemetry>(Allocator.Temp);
            using var beliefs = droneQuery.ToComponentDataArray<BayesianBeliefComponent>(Allocator.Temp);
            using var transforms = droneQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < states.Length; i++)
            {
                var st = states[i];
                var tel = telemetries[i];
                var bel = beliefs[i];
                var tf = transforms[i];

                sb.AppendLine($"<b>D{i + 1}</b> [a_d=<color=#38BDF8>{st.ActiveAction}</color> | a_p={bel.LastPlayerAction}] ({tf.Position.x:F1},{tf.Position.y:F1})");
                sb.AppendLine($"  π_c={bel.TopClusterConfidence * 100f:F1}% | H={bel.Entropy:F2}b | MAP=({bel.EstimatedTargetCell.x},{bel.EstimatedTargetCell.y}) | A*={tel.ReplanLatencyMs:F2}ms N={tel.NodesExpanded} Hz={tel.HazardCellsAvoided}");
            }
            droneQuery.Dispose();

            var learnQuery = em.CreateEntityQuery(typeof(DroneLearningState));
            if (learnQuery.CalculateEntityCount() > 0)
            {
                using var lArr = learnQuery.ToComponentDataArray<DroneLearningState>(Allocator.Temp);
                var l = lArr[0];
                sb.Append($"<color=#8A99AD>Learn: Esc={l.TotalEscapesCompleted} · Imp={l.EscapeImpulseMultiplier:F2}x · Skill={l.CrateDodgeSkill * 100f:F0}% · Δ=+{l.DroneImprovementPct:F1}%</color>");
            }
            learnQuery.Dispose();

            bodyText.text = sb.ToString();
        }

        private void OnDrawGizmos()
        {
            if (!showDebug) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (showGridCells)
            {
                Gizmos.color = new Color(0.3f, 0.4f, 0.5f, 0.15f);
                for (int x = 0; x < GridAStar.ActiveWidth; x++)
                {
                    for (int y = 0; y < GridAStar.ActiveHeight; y++)
                    {
                        Vector3 cpos = GridAStar.GridToWorld(new int2(x, y), 0f);
                        Gizmos.DrawWireCube(cpos, new Vector3(0.95f, 0.95f, 0.05f));
                    }
                }
            }

            var droneQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BayesianBeliefComponent>(),
                ComponentType.ReadOnly<DroneState>());

            using var entities = droneQuery.ToEntityArray(Allocator.Temp);
            using var beliefs = droneQuery.ToComponentDataArray<BayesianBeliefComponent>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var bel = beliefs[i];
                var entity = entities[i];

                if (showBeliefHeatmap && em.HasBuffer<BeliefCell>(entity))
                {
                    var beliefBuffer = em.GetBuffer<BeliefCell>(entity);
                    for (int b = 0; b < beliefBuffer.Length; b++)
                    {
                        var cell = beliefBuffer[b];
                        if (cell.Probability > 0.012f)
                        {
                            float norm = Mathf.Clamp01(cell.Probability * 10f);
                            Gizmos.color = Color.Lerp(new Color(0.22f, 0.74f, 0.97f, 0.12f), new Color(0.94f, 0.27f, 0.27f, 0.5f), norm);
                            Vector3 pos = GridAStar.GridToWorld(cell.Coordinate, 0.05f);
                            Gizmos.DrawCube(pos, new Vector3(0.85f, 0.85f, 0.02f));
                        }
                    }

                    Gizmos.color = new Color(0.22f, 0.74f, 0.97f, 0.95f);
                    Vector3 mapPos = GridAStar.GridToWorld(bel.EstimatedTargetCell, 0.1f);
                    Gizmos.DrawWireSphere(mapPos, 0.42f);
                    Gizmos.DrawLine(mapPos - Vector3.right * 0.6f, mapPos + Vector3.right * 0.6f);
                    Gizmos.DrawLine(mapPos - Vector3.up * 0.6f, mapPos + Vector3.up * 0.6f);
                }

                if (em.HasBuffer<GridCell>(entity))
                {
                    var pathBuf = em.GetBuffer<GridCell>(entity);
                    if (pathBuf.Length > 0)
                    {
                        Gizmos.color = new Color(0.13f, 0.77f, 0.37f, 0.95f);
                        float3 prev = pathBuf[0].WorldPos;
                        for (int p = 1; p < pathBuf.Length; p++)
                        {
                            float3 curr = pathBuf[p].WorldPos;
                            Gizmos.DrawLine(prev, curr);
                            Gizmos.DrawWireSphere(curr, 0.12f);
                            prev = curr;
                        }
                    }
                }
            }

            droneQuery.Dispose();
        }
    }
}
