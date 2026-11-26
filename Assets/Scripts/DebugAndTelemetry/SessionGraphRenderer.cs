using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityDrift
{
    /// <summary>
    /// Simple MATLAB-style Texture2D figure rasterizer for the in-game Session Report:
    /// Classic white figure paper, closed black axes frame, dotted light-grey gridlines,
    /// crisp bitmap tick labels, X-axis/Y-axis unit readouts, and sample count (n = ...).
    /// Plots:
    /// 1) Levels Played & Progression (Y: Room Time s & Complexity %, X: Attempt #, n = attempts)
    /// 2) AI Metrics Per Input (Y: 0-100%, X: Input Event #, n = inputs)
    /// 3) Death Rate & AI Improvement (Y: 0-100%, X: Level Attempt #, n = attempts)
    /// </summary>
    public static class SessionGraphRenderer
    {
        // Classic MATLAB figure colors on clean white paper
        private static readonly Color32 PaperWhite   = new Color32(255, 255, 255, 255);
        private static readonly Color32 AxisBlack    = new Color32(0, 0, 0, 255);
        private static readonly Color32 GridGrey     = new Color32(212, 212, 212, 255);
        private static readonly Color32 SubtleGrey   = new Color32(120, 120, 120, 255);
        private static readonly Color32 MatlabBlue   = new Color32(0, 114, 189, 255);
        private static readonly Color32 MatlabOrange = new Color32(217, 83, 25, 255);
        private static readonly Color32 MatlabGreen  = new Color32(44, 160, 44, 255);
        private static readonly Color32 MatlabRed    = new Color32(204, 37, 41, 255);
        private static readonly Color32 BarGreenFill = new Color32(198, 232, 198, 255);
        private static readonly Color32 BarRedFill   = new Color32(246, 202, 202, 255);
        private static readonly Color32 BarBlueFill  = new Color32(204, 227, 243, 255);

        private const int PadL = 58;
        private const int PadR = 16;
        private const int PadT = 24;
        private const int PadB = 34;

        public static void RenderLevelsChart(Texture2D tex, IReadOnlyList<SessionLevelRecord> rows)
        {
            if (tex == null) return;
            int w = tex.width, h = tex.height;
            int plotW = w - PadL - PadR;
            int plotH = h - PadT - PadB;
            int baseY = PadB;

            var px = InitMatlabAxes(w, h);
            int n = rows != null ? rows.Count : 0;

            float maxTimer = 10f;
            float maxComp = 100f;
            for (int i = 0; i < n; i++)
            {
                if (rows[i].room_timer_sec > maxTimer) maxTimer = rows[i].room_timer_sec;
                if (rows[i].complexity_score > maxComp) maxComp = rows[i].complexity_score;
            }
            maxTimer = Mathf.Ceil(maxTimer / 5f) * 5f;
            maxComp = Mathf.Max(100f, Mathf.Ceil(maxComp / 25f) * 25f);

            // Y-axis ticks (0..maxTimer s)
            for (int k = 0; k <= 4; k++)
            {
                int y = baseY + Mathf.RoundToInt((k / 4f) * plotH);
                float tickVal = (k / 4f) * maxTimer;
                string lbl = tickVal >= 10f ? $"{tickVal:F0}S" : $"{tickVal:F1}S";
                DrawTextRight(px, w, h, PadL - 6, y - 3, lbl, AxisBlack);
            }

            // Header badge inside top margin: sample count
            DrawText(px, w, h, PadL, h - 14, $"N = {n} ATTEMPTS", AxisBlack);
            DrawTextRight(px, w, h, w - PadR, h - 14, $"Y MAX = {maxTimer:F0}S", SubtleGrey);

            if (n == 0)
            {
                DrawCenteredEmptyNote(px, w, h, PadL, baseY, plotW, plotH, "NO SAMPLES RECORDED (N = 0)");
                DrawText(px, w, h, PadL + plotW / 2 - 42, 8, "ATTEMPT INDEX", AxisBlack);
                Commit(tex, px);
                return;
            }

            float slotW = (float)plotW / n;
            int barW = Mathf.Clamp(Mathf.RoundToInt(slotW * 0.48f), 8, 34);

            var timerPts = new Vector2[n];
            var compPts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                float cx = PadL + (i + 0.5f) * slotW;
                int xLeft = Mathf.RoundToInt(cx - barW * 0.5f);

                float frac = Mathf.Clamp(r.room_timer_sec / maxTimer, 0.12f, 1f);
                int barH = Mathf.Max(6, Mathf.RoundToInt(frac * plotH));

                bool isDeath = r.outcome == "Death_Tagged";
                bool isCleared = r.room_cleared || r.outcome == "Cleared";
                Color32 edgeCol = isDeath ? MatlabRed : (isCleared ? MatlabGreen : MatlabBlue);
                Color32 fillCol = isDeath ? BarRedFill : (isCleared ? BarGreenFill : BarBlueFill);

                FillRect(px, w, h, xLeft, baseY + 1, barW, barH, fillCol);
                DrawRectOutline(px, w, h, xLeft, baseY + 1, barW, barH, edgeCol);

                float ty = baseY + Mathf.Clamp01(r.room_timer_sec / maxTimer) * plotH;
                timerPts[i] = new Vector2(cx, ty);

                float cy = baseY + Mathf.Clamp01(r.complexity_score / maxComp) * plotH;
                compPts[i] = new Vector2(cx, cy);
            }

            DrawSeriesXAxisTicks(px, w, h, PadL, baseY, plotW, n, centerBars: true);
            DrawText(px, w, h, PadL + plotW / 2 - 48, 8, "ATTEMPT INDEX (N)", AxisBlack);

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLine(px, w, h, compPts[i].x, compPts[i].y, compPts[i + 1].x, compPts[i + 1].y, 2.0f, MatlabOrange);
                DrawThickLine(px, w, h, timerPts[i].x, timerPts[i].y, timerPts[i + 1].x, timerPts[i + 1].y, 2.2f, MatlabBlue);
            }
            for (int i = 0; i < n; i++)
            {
                DrawCircle(px, w, h, compPts[i].x, compPts[i].y, 3.6f, MatlabOrange);
                DrawCircle(px, w, h, timerPts[i].x, timerPts[i].y, 4.0f, MatlabBlue);
            }

            Commit(tex, px);
        }

        public static void RenderPerInputAIChart(Texture2D tex, IReadOnlyList<PerInputAIEvent> eventsList)
        {
            if (tex == null) return;
            int w = tex.width, h = tex.height;
            int plotW = w - PadL - PadR;
            int plotH = h - PadT - PadB;
            int baseY = PadB;

            var px = InitMatlabAxes(w, h);
            DrawPercentYTicks(px, w, h, baseY, plotH);

            int n = eventsList != null ? eventsList.Count : 0;
            DrawText(px, w, h, PadL, h - 14, $"N = {n} INPUTS", AxisBlack);
            DrawTextRight(px, w, h, w - PadR, h - 14, "SCALE: 0-100%", SubtleGrey);

            if (n == 0)
            {
                DrawCenteredEmptyNote(px, w, h, PadL, baseY, plotW, plotH, "NO SAMPLES RECORDED (N = 0)");
                DrawText(px, w, h, PadL + plotW / 2 - 48, 8, "INPUT EVENT INDEX", AxisBlack);
                Commit(tex, px);
                return;
            }

            var ptsConf = new Vector2[n];
            var ptsPrec = new Vector2[n];
            var ptsImp  = new Vector2[n];
            var ptsPat  = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var e = eventsList[i];
                float x = (n == 1) ? (PadL + plotW * 0.5f) : (PadL + ((float)i / (n - 1)) * plotW);

                float confPct = Mathf.Clamp01(e.top_cluster_confidence);
                float precisionPct = Mathf.Clamp01(1f - (e.posterior_entropy / 5.2f) + Mathf.Max(0f, e.entropy_reduction) * 0.25f);
                float impPct = Mathf.Clamp01(e.drone_improvement_pct / 100f);
                float patPct = Mathf.Clamp01(e.pattern_adaptation_pct / 100f);

                ptsConf[i] = new Vector2(x, baseY + confPct * plotH);
                ptsPrec[i] = new Vector2(x, baseY + precisionPct * plotH);
                ptsImp[i]  = new Vector2(x, baseY + impPct * plotH);
                ptsPat[i]  = new Vector2(x, baseY + patPct * plotH);
            }

            DrawSeriesXAxisTicks(px, w, h, PadL, baseY, plotW, n, centerBars: false);
            DrawText(px, w, h, PadL + plotW / 2 - 54, 8, "INPUT EVENT # (N)", AxisBlack);

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLine(px, w, h, ptsPat[i].x,  ptsPat[i].y,  ptsPat[i + 1].x,  ptsPat[i + 1].y,  1.8f, AxisBlack);
                DrawThickLine(px, w, h, ptsImp[i].x,  ptsImp[i].y,  ptsImp[i + 1].x,  ptsImp[i + 1].y,  2.0f, MatlabGreen);
                DrawThickLine(px, w, h, ptsPrec[i].x, ptsPrec[i].y, ptsPrec[i + 1].x, ptsPrec[i + 1].y, 1.8f, MatlabOrange);
                DrawThickLine(px, w, h, ptsConf[i].x, ptsConf[i].y, ptsConf[i + 1].x, ptsConf[i + 1].y, 2.2f, MatlabBlue);
            }

            int stepDot = Mathf.Max(1, n / 24);
            for (int i = 0; i < n; i++)
            {
                var e = eventsList[i];
                if (e.drone_action == "EvadeCrateDodge")
                {
                    DrawCircle(px, w, h, ptsImp[i].x, ptsImp[i].y, 4.0f, MatlabOrange);
                }
                else if (i % stepDot == 0 || i == n - 1)
                {
                    DrawCircle(px, w, h, ptsConf[i].x, ptsConf[i].y, 3.0f, MatlabBlue);
                }
            }

            Commit(tex, px);
        }

        public static void RenderDeathAndImprovementChart(Texture2D tex, IReadOnlyList<SessionLevelRecord> rows)
        {
            if (tex == null) return;
            int w = tex.width, h = tex.height;
            int plotW = w - PadL - PadR;
            int plotH = h - PadT - PadB;
            int baseY = PadB;

            var px = InitMatlabAxes(w, h);
            DrawPercentYTicks(px, w, h, baseY, plotH);

            int n = rows != null ? rows.Count : 0;
            DrawText(px, w, h, PadL, h - 14, $"N = {n} LEVELS", AxisBlack);
            DrawTextRight(px, w, h, w - PadR, h - 14, "SCALE: 0-100%", SubtleGrey);

            if (n == 0)
            {
                DrawCenteredEmptyNote(px, w, h, PadL, baseY, plotW, plotH, "NO SAMPLES RECORDED (N = 0)");
                DrawText(px, w, h, PadL + plotW / 2 - 42, 8, "LEVEL INDEX (N)", AxisBlack);
                Commit(tex, px);
                return;
            }

            var ptsDeath = new Vector2[n];
            var ptsImp   = new Vector2[n];
            var ptsDodge = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                float x = (n == 1) ? (PadL + plotW * 0.5f) : (PadL + ((float)i / (n - 1)) * plotW);
                ptsDeath[i] = new Vector2(x, baseY + Mathf.Clamp01(r.cumulative_death_rate_pct / 100f) * plotH);
                ptsImp[i]   = new Vector2(x, baseY + Mathf.Clamp01(r.drone_improvement_pct / 100f) * plotH);
                ptsDodge[i] = new Vector2(x, baseY + Mathf.Clamp01(r.crate_dodge_skill) * plotH);
            }

            DrawSeriesXAxisTicks(px, w, h, PadL, baseY, plotW, n, centerBars: false);
            DrawText(px, w, h, PadL + plotW / 2 - 48, 8, "LEVEL ATTEMPT (N)", AxisBlack);

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLine(px, w, h, ptsDodge[i].x, ptsDodge[i].y, ptsDodge[i + 1].x, ptsDodge[i + 1].y, 2.0f, MatlabGreen);
                DrawThickLine(px, w, h, ptsImp[i].x,   ptsImp[i].y,   ptsImp[i + 1].x,   ptsImp[i + 1].y,   2.2f, MatlabBlue);
                DrawThickLine(px, w, h, ptsDeath[i].x, ptsDeath[i].y, ptsDeath[i + 1].x, ptsDeath[i + 1].y, 2.2f, MatlabRed);
            }
            for (int i = 0; i < n; i++)
            {
                DrawCircle(px, w, h, ptsDodge[i].x, ptsDodge[i].y, 3.6f, MatlabGreen);
                DrawCircle(px, w, h, ptsImp[i].x,   ptsImp[i].y,   4.0f, MatlabBlue);
                DrawCircle(px, w, h, ptsDeath[i].x, ptsDeath[i].y, 4.0f, MatlabRed);
            }

            Commit(tex, px);
        }

        private static Color32[] InitMatlabAxes(int w, int h)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = PaperWhite;

            int plotW = w - PadL - PadR;
            int plotH = h - PadT - PadB;

            // Dotted horizontal gridlines + inward ticks at 0%, 25%, 50%, 75%, 100%
            for (int k = 0; k <= 4; k++)
            {
                int y = PadB + Mathf.RoundToInt((k / 4f) * plotH);
                if (k > 0 && k < 4)
                {
                    for (int x = PadL + 1; x < PadL + plotW; x++)
                    {
                        if ((x % 4) == 0) SetPx(px, w, h, x, y, GridGrey);
                    }
                }
                for (int t = 0; t < 4; t++)
                {
                    SetPx(px, w, h, PadL + t, y, AxisBlack);
                    SetPx(px, w, h, PadL + plotW - t, y, AxisBlack);
                }
            }

            // Closed MATLAB axes box (1px black)
            DrawRectOutline(px, w, h, PadL, PadB, plotW + 1, plotH + 1, AxisBlack);
            return px;
        }

        private static void DrawPercentYTicks(Color32[] px, int w, int h, int baseY, int plotH)
        {
            for (int k = 0; k <= 4; k++)
            {
                int y = baseY + Mathf.RoundToInt((k / 4f) * plotH);
                DrawTextRight(px, w, h, PadL - 6, y - 3, $"{k * 25}%", AxisBlack);
            }
        }

        private static void DrawSeriesXAxisTicks(Color32[] px, int w, int h, int padL, int baseY, int plotW, int n, bool centerBars)
        {
            if (n <= 0) return;

            int maxTicks = 6;
            int step = Mathf.Max(1, Mathf.CeilToInt((float)n / maxTicks));
            for (int i = 0; i < n; i++)
            {
                bool drawLabel = (i == 0) || (i == n - 1) || ((i + 1) % step == 0);
                if (!drawLabel) continue;

                float fx = centerBars
                    ? (padL + ((i + 0.5f) / n) * plotW)
                    : ((n == 1) ? (padL + plotW * 0.5f) : (padL + ((float)i / (n - 1)) * plotW));

                int x = Mathf.RoundToInt(fx);
                for (int ty = 0; ty < 4; ty++) SetPx(px, w, h, x, baseY + ty, AxisBlack);

                string num = (i + 1).ToString();
                DrawTextCentered(px, w, h, x, baseY - 12, num, AxisBlack);
            }
        }

        private static void DrawCenteredEmptyNote(Color32[] px, int w, int h, int x0, int y0, int pw, int ph, string msg)
        {
            DrawTextCentered(px, w, h, x0 + pw / 2, y0 + ph / 2 - 4, msg, SubtleGrey);
        }

        private static void Commit(Texture2D tex, Color32[] px)
        {
            tex.SetPixels32(px);
            tex.Apply();
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

        // =========================================================================
        // COMPACT 4x6 PIXEL FONT FOR CRISP MATLAB AXIS NUMBERS & LABELS
        // =========================================================================
        private static void DrawTextCentered(Color32[] px, int w, int h, int cx, int y, string text, Color32 col)
        {
            int totalW = string.IsNullOrEmpty(text) ? 0 : (text.Length * 5 - 1);
            DrawText(px, w, h, cx - totalW / 2, y, text, col);
        }

        private static void DrawTextRight(Color32[] px, int w, int h, int rightX, int y, string text, Color32 col)
        {
            int totalW = string.IsNullOrEmpty(text) ? 0 : (text.Length * 5 - 1);
            DrawText(px, w, h, rightX - totalW, y, text, col);
        }

        private static void DrawText(Color32[] px, int w, int h, int startX, int startY, string text, Color32 col)
        {
            if (string.IsNullOrEmpty(text)) return;
            string upper = text.ToUpperInvariant();
            int cursorX = startX;
            for (int i = 0; i < upper.Length; i++)
            {
                byte[] rows = GetGlyph4x6(upper[i]);
                if (rows != null)
                {
                    for (int r = 0; r < 6; r++)
                    {
                        byte mask = rows[5 - r]; // y=0 is bottom in Texture2D
                        for (int c = 0; c < 4; c++)
                        {
                            if ((mask & (1 << (3 - c))) != 0)
                            {
                                SetPx(px, w, h, cursorX + c, startY + r, col);
                            }
                        }
                    }
                }
                cursorX += 5;
            }
        }

        private static byte[] GetGlyph4x6(char ch)
        {
            switch (ch)
            {
                case '0': return new byte[] { 0x6, 0x9, 0x9, 0x9, 0x9, 0x6 };
                case '1': return new byte[] { 0x2, 0x6, 0x2, 0x2, 0x2, 0x7 };
                case '2': return new byte[] { 0x6, 0x9, 0x1, 0x2, 0x4, 0xF };
                case '3': return new byte[] { 0xE, 0x1, 0x6, 0x1, 0x1, 0xE };
                case '4': return new byte[] { 0x2, 0x6, 0xA, 0xF, 0x2, 0x2 };
                case '5': return new byte[] { 0xF, 0x8, 0xE, 0x1, 0x1, 0xE };
                case '6': return new byte[] { 0x6, 0x8, 0xE, 0x9, 0x9, 0x6 };
                case '7': return new byte[] { 0xF, 0x1, 0x2, 0x4, 0x4, 0x4 };
                case '8': return new byte[] { 0x6, 0x9, 0x6, 0x9, 0x9, 0x6 };
                case '9': return new byte[] { 0x6, 0x9, 0x9, 0x7, 0x1, 0x6 };
                case 'A': return new byte[] { 0x6, 0x9, 0x9, 0xF, 0x9, 0x9 };
                case 'B': return new byte[] { 0xE, 0x9, 0xE, 0x9, 0x9, 0xE };
                case 'C': return new byte[] { 0x7, 0x8, 0x8, 0x8, 0x8, 0x7 };
                case 'D': return new byte[] { 0xE, 0x9, 0x9, 0x9, 0x9, 0xE };
                case 'E': return new byte[] { 0xF, 0x8, 0xE, 0x8, 0x8, 0xF };
                case 'F': return new byte[] { 0xF, 0x8, 0xE, 0x8, 0x8, 0x8 };
                case 'G': return new byte[] { 0x7, 0x8, 0xB, 0x9, 0x9, 0x7 };
                case 'H': return new byte[] { 0x9, 0x9, 0xF, 0x9, 0x9, 0x9 };
                case 'I': return new byte[] { 0x7, 0x2, 0x2, 0x2, 0x2, 0x7 };
                case 'J': return new byte[] { 0x1, 0x1, 0x1, 0x1, 0x9, 0x6 };
                case 'K': return new byte[] { 0x9, 0xA, 0xC, 0xA, 0x9, 0x9 };
                case 'L': return new byte[] { 0x8, 0x8, 0x8, 0x8, 0x8, 0xF };
                case 'M': return new byte[] { 0x9, 0xF, 0xF, 0x9, 0x9, 0x9 };
                case 'N': return new byte[] { 0x9, 0xD, 0xB, 0x9, 0x9, 0x9 };
                case 'O': return new byte[] { 0x6, 0x9, 0x9, 0x9, 0x9, 0x6 };
                case 'P': return new byte[] { 0xE, 0x9, 0xE, 0x8, 0x8, 0x8 };
                case 'Q': return new byte[] { 0x6, 0x9, 0x9, 0xB, 0xA, 0x5 };
                case 'R': return new byte[] { 0xE, 0x9, 0xE, 0xC, 0xA, 0x9 };
                case 'S': return new byte[] { 0x7, 0x8, 0x6, 0x1, 0x1, 0xE };
                case 'T': return new byte[] { 0xF, 0x2, 0x2, 0x2, 0x2, 0x2 };
                case 'U': return new byte[] { 0x9, 0x9, 0x9, 0x9, 0x9, 0x6 };
                case 'V': return new byte[] { 0x9, 0x9, 0x9, 0x9, 0x6, 0x6 };
                case 'W': return new byte[] { 0x9, 0x9, 0x9, 0xF, 0xF, 0x9 };
                case 'X': return new byte[] { 0x9, 0x9, 0x6, 0x6, 0x9, 0x9 };
                case 'Y': return new byte[] { 0x9, 0x9, 0x6, 0x2, 0x2, 0x2 };
                case 'Z': return new byte[] { 0xF, 0x1, 0x2, 0x4, 0x8, 0xF };
                case '.': return new byte[] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x2 };
                case ',': return new byte[] { 0x0, 0x0, 0x0, 0x0, 0x2, 0x4 };
                case ':': return new byte[] { 0x0, 0x2, 0x0, 0x2, 0x0, 0x0 };
                case '-': return new byte[] { 0x0, 0x0, 0xE, 0x0, 0x0, 0x0 };
                case '+': return new byte[] { 0x0, 0x2, 0x7, 0x2, 0x0, 0x0 };
                case '=': return new byte[] { 0x0, 0xE, 0x0, 0xE, 0x0, 0x0 };
                case '/': return new byte[] { 0x1, 0x1, 0x2, 0x4, 0x8, 0x8 };
                case '%': return new byte[] { 0x9, 0x2, 0x2, 0x4, 0x4, 0x9 };
                case '(': return new byte[] { 0x2, 0x4, 0x4, 0x4, 0x4, 0x2 };
                case ')': return new byte[] { 0x4, 0x2, 0x2, 0x2, 0x2, 0x4 };
                case '#': return new byte[] { 0x5, 0xF, 0x5, 0x5, 0xF, 0x5 };
                default:  return null;
            }
        }
    }
}
