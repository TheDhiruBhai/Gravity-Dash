using System.Collections.Generic;
using UnityEngine;

namespace GravityDrift
{
    /// <summary>
    /// Future-minimalist Texture2D chart rasterizer for the in-game Game Report & Graphs panel.
    /// Renders three live telemetry charts from SessionReportRecorder:
    /// 1) Levels Played & Progression (Clears/Deaths + Clear Timer)
    /// 2) AI Improvement Per Input (Posterior Confidence, Inverse Entropy Precision, Drone Improvement %)
    /// 3) Death Rate & Improvement (Cumulative Death Rate % vs AI Improvement % & Crate-Dodge Skill %)
    /// </summary>
    public static class SessionGraphRenderer
    {
        private static readonly Color32 ChartBg      = new Color32(14, 18, 26, 255);
        private static readonly Color32 ChartGrid    = new Color32(34, 44, 58, 255);
        private static readonly Color32 ChartAxis    = new Color32(95, 112, 134, 255);
        private static readonly Color32 SkyBlue      = new Color32(56, 189, 248, 255);
        private static readonly Color32 CyanGlow     = new Color32(0, 229, 255, 255);
        private static readonly Color32 MintGreen    = new Color32(34, 197, 94, 255);
        private static readonly Color32 DangerRed    = new Color32(239, 68, 68, 255);
        private static readonly Color32 AmberGold    = new Color32(245, 158, 11, 255);
        private static readonly Color32 SlateFill    = new Color32(42, 52, 68, 255);

        public static void RenderLevelsChart(Texture2D tex, IReadOnlyList<SessionLevelRecord> rows)
        {
            if (tex == null) return;
            int w = tex.width, h = tex.height;
            var px = InitGridFrame(w, h);

            int padL = 36, padR = 16, padT = 16, padB = 26;
            int plotW = w - padL - padR;
            int plotH = h - padT - padB;
            int baseY = padB;

            if (rows == null || rows.Count == 0)
            {
                DrawBaselineHint(px, w, h, padL, baseY, plotW, plotH, SkyBlue);
                Commit(tex, px);
                return;
            }

            float maxTimer = 5f;
            float maxComp = 120f;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].room_timer_sec > maxTimer) maxTimer = rows[i].room_timer_sec;
                if (rows[i].complexity_score > maxComp) maxComp = rows[i].complexity_score;
            }
            maxTimer = Mathf.Ceil(maxTimer / 5f) * 5f;

            int n = rows.Count;
            float slotW = (float)plotW / Mathf.Max(1, n);
            int barW = Mathf.Clamp(Mathf.RoundToInt(slotW * 0.44f), 8, 34);

            var timerPts = new Vector2[n];
            var compPts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                float cx = padL + (i + 0.5f) * slotW;
                int xLeft = Mathf.RoundToInt(cx - barW * 0.5f);

                // Bar height represents RoomTimer / maxTimer (at least 18% visible for Outcome badge)
                float frac = Mathf.Clamp(r.room_timer_sec / maxTimer, 0.14f, 1f);
                int barH = Mathf.Max(10, Mathf.RoundToInt(frac * plotH));

                Color32 barCol = r.outcome == "Death_Tagged" ? DangerRed
                               : (r.room_cleared || r.outcome == "Cleared" ? MintGreen : SkyBlue);
                Color32 fillCol = new Color32(barCol.r, barCol.g, barCol.b, 130);

                FillRect(px, w, h, xLeft, baseY, barW, barH, Blend(ChartBg, fillCol));
                DrawRectOutline(px, w, h, xLeft, baseY, barW, barH, barCol);

                float ty = baseY + Mathf.Clamp01(r.room_timer_sec / maxTimer) * plotH;
                timerPts[i] = new Vector2(cx, ty);

                float cy = baseY + Mathf.Clamp01(r.complexity_score / maxComp) * plotH;
                compPts[i] = new Vector2(cx, cy);
            }

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLine(px, w, h, compPts[i].x, compPts[i].y, compPts[i + 1].x, compPts[i + 1].y, 2.0f, AmberGold);
                DrawThickLine(px, w, h, timerPts[i].x, timerPts[i].y, timerPts[i + 1].x, timerPts[i + 1].y, 2.2f, CyanGlow);
            }
            for (int i = 0; i < n; i++)
            {
                DrawCircle(px, w, h, compPts[i].x, compPts[i].y, 4.0f, AmberGold);
                DrawCircle(px, w, h, timerPts[i].x, timerPts[i].y, 4.5f, CyanGlow);
            }

            Commit(tex, px);
        }

        public static void RenderPerInputAIChart(Texture2D tex, IReadOnlyList<PerInputAIEvent> eventsList)
        {
            if (tex == null) return;
            int w = tex.width, h = tex.height;
            var px = InitGridFrame(w, h);

            int padL = 36, padR = 16, padT = 16, padB = 26;
            int plotW = w - padL - padR;
            int plotH = h - padT - padB;
            int baseY = padB;

            if (eventsList == null || eventsList.Count == 0)
            {
                DrawBaselineHint(px, w, h, padL, baseY, plotW, plotH, AmberGold);
                Commit(tex, px);
                return;
            }

            int n = eventsList.Count;
            var ptsConf = new Vector2[n];
            var ptsPrec = new Vector2[n];
            var ptsImp  = new Vector2[n];
            var ptsPat  = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var e = eventsList[i];
                float x = (n == 1) ? (padL + plotW * 0.5f) : (padL + ((float)i / (n - 1)) * plotW);

                float confPct = Mathf.Clamp01(e.top_cluster_confidence);
                float precisionPct = Mathf.Clamp01(1f - (e.posterior_entropy / 5.2f) + Mathf.Max(0f, e.entropy_reduction) * 0.25f);
                float impPct = Mathf.Clamp01(e.drone_improvement_pct / 100f);
                float patPct = Mathf.Clamp01(e.pattern_adaptation_pct / 100f);

                ptsConf[i] = new Vector2(x, baseY + confPct * plotH);
                ptsPrec[i] = new Vector2(x, baseY + precisionPct * plotH);
                ptsImp[i]  = new Vector2(x, baseY + impPct * plotH);
                ptsPat[i]  = new Vector2(x, baseY + patPct * plotH);
            }

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLine(px, w, h, ptsPat[i].x,  ptsPat[i].y,  ptsPat[i + 1].x,  ptsPat[i + 1].y,  1.8f, CyanGlow);
                DrawThickLine(px, w, h, ptsImp[i].x,  ptsImp[i].y,  ptsImp[i + 1].x,  ptsImp[i + 1].y,  2.0f, MintGreen);
                DrawThickLine(px, w, h, ptsPrec[i].x, ptsPrec[i].y, ptsPrec[i + 1].x, ptsPrec[i + 1].y, 1.8f, AmberGold);
                DrawThickLine(px, w, h, ptsConf[i].x, ptsConf[i].y, ptsConf[i + 1].x, ptsConf[i + 1].y, 2.5f, SkyBlue);
            }

            // Highlight tactical evade/crate-attack events cleanly along curves
            int stepDot = Mathf.Max(1, n / 28);
            for (int i = 0; i < n; i++)
            {
                var e = eventsList[i];
                if (e.drone_action == "EvadeCrateDodge")
                {
                    DrawCircle(px, w, h, ptsImp[i].x, ptsImp[i].y, 4.2f, AmberGold);
                }
                else if (i % stepDot == 0 || i == n - 1)
                {
                    DrawCircle(px, w, h, ptsConf[i].x, ptsConf[i].y, 3.2f, SkyBlue);
                }
            }

            Commit(tex, px);
        }

        public static void RenderDeathAndImprovementChart(Texture2D tex, IReadOnlyList<SessionLevelRecord> rows)
        {
            if (tex == null) return;
            int w = tex.width, h = tex.height;
            var px = InitGridFrame(w, h);

            int padL = 36, padR = 16, padT = 16, padB = 26;
            int plotW = w - padL - padR;
            int plotH = h - padT - padB;
            int baseY = padB;

            if (rows == null || rows.Count == 0)
            {
                DrawBaselineHint(px, w, h, padL, baseY, plotW, plotH, DangerRed);
                Commit(tex, px);
                return;
            }

            int n = rows.Count;
            var ptsDeath = new Vector2[n];
            var ptsImp   = new Vector2[n];
            var ptsDodge = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                float x = (n == 1) ? (padL + plotW * 0.5f) : (padL + ((float)i / (n - 1)) * plotW);
                ptsDeath[i] = new Vector2(x, baseY + Mathf.Clamp01(r.cumulative_death_rate_pct / 100f) * plotH);
                ptsImp[i]   = new Vector2(x, baseY + Mathf.Clamp01(r.drone_improvement_pct / 100f) * plotH);
                ptsDodge[i] = new Vector2(x, baseY + Mathf.Clamp01(r.crate_dodge_skill) * plotH);
            }

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLine(px, w, h, ptsDodge[i].x, ptsDodge[i].y, ptsDodge[i + 1].x, ptsDodge[i + 1].y, 2.0f, MintGreen);
                DrawThickLine(px, w, h, ptsImp[i].x,   ptsImp[i].y,   ptsImp[i + 1].x,   ptsImp[i + 1].y,   2.5f, SkyBlue);
                DrawThickLine(px, w, h, ptsDeath[i].x, ptsDeath[i].y, ptsDeath[i + 1].x, ptsDeath[i + 1].y, 2.5f, DangerRed);
            }
            for (int i = 0; i < n; i++)
            {
                DrawCircle(px, w, h, ptsDodge[i].x, ptsDodge[i].y, 3.8f, MintGreen);
                DrawCircle(px, w, h, ptsImp[i].x,   ptsImp[i].y,   4.2f, SkyBlue);
                DrawCircle(px, w, h, ptsDeath[i].x, ptsDeath[i].y, 4.2f, DangerRed);
            }

            Commit(tex, px);
        }

        private static Color32[] InitGridFrame(int w, int h)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = ChartBg;

            int padL = 36, padR = 16, padT = 16, padB = 26;
            int plotW = w - padL - padR;
            int plotH = h - padT - padB;

            // Horizontal dashed gridlines at 0%, 25%, 50%, 75%, 100%
            for (int k = 0; k <= 4; k++)
            {
                int y = padB + Mathf.RoundToInt((k / 4f) * plotH);
                for (int x = padL; x <= padL + plotW; x++)
                {
                    if (k == 0 || ((x / 5) % 2 == 0))
                    {
                        SetPx(px, w, h, x, y, k == 0 ? ChartAxis : ChartGrid);
                    }
                }
            }

            // Left Y-axis
            for (int y = padB; y <= padB + plotH; y++)
            {
                SetPx(px, w, h, padL, y, ChartAxis);
            }

            return px;
        }

        private static void DrawBaselineHint(Color32[] px, int w, int h, int x0, int y0, int pw, int ph, Color32 accent)
        {
            int midY = y0 + ph / 2;
            for (int x = x0 + 8; x < x0 + pw - 8; x += 6)
            {
                SetPx(px, w, h, x, midY, SlateFill);
                SetPx(px, w, h, x + 1, midY, accent);
            }
        }

        private static void Commit(Texture2D tex, Color32[] px)
        {
            tex.SetPixels32(px);
            tex.Apply();
        }

        private static Color32 Blend(Color32 dst, Color32 src)
        {
            float a = src.a / 255f;
            return new Color32(
                (byte)Mathf.RoundToInt(dst.r * (1f - a) + src.r * a),
                (byte)Mathf.RoundToInt(dst.g * (1f - a) + src.g * a),
                (byte)Mathf.RoundToInt(dst.b * (1f - a) + src.b * a),
                255);
        }

        private static void FillRect(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 col)
        {
            int xMax = Mathf.Min(w - 1, x0 + rw);
            int yMax = Mathf.Min(h - 1, y0 + rh);
            for (int y = Mathf.Max(0, y0); y < yMax; y++)
                for (int x = Mathf.Max(0, x0); x < xMax; x++)
                    px[y * w + x] = col;
        }

        private static void DrawRectOutline(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 col)
        {
            for (int x = x0; x < x0 + rw; x++)
            {
                SetPx(px, w, h, x, y0, col);
                SetPx(px, w, h, x, y0 + rh - 1, col);
            }
            for (int y = y0; y < y0 + rh; y++)
            {
                SetPx(px, w, h, x0, y, col);
                SetPx(px, w, h, x0 + rw - 1, y, col);
            }
        }

        private static void DrawCircle(Color32[] px, int w, int h, float cx, float cy, float r, Color32 col)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - r));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - r));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r));
            float rSq = r * r;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= rSq) px[y * w + x] = col;
                }
        }

        private static void DrawThickLine(Color32[] px, int w, int h, float x0, float y0, float x1, float y1, float thickness, Color32 col)
        {
            float dist = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist * 1.5f));
            float rad = thickness * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                DrawCircle(px, w, h, Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), rad, col);
            }
        }

        private static void SetPx(Color32[] px, int w, int h, int x, int y, Color32 col)
        {
            if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = col;
        }
    }
}
