using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GravityDrift.Editor
{
    public static class ProgressionChartExporter
    {
        private struct LevelRow
        {
            public int Level;
            public int DroneCount;
            public int RoomWidth;
            public int RoomHeight;
            public float PosteriorPiPct;
            public float CrateDodgePct;
            public float AvgEscapeSec;
            public int SmartAttacks;
            public float ImprovementPct;
        }

        [MenuItem("GravityDrift/Run Benchmark && Export Progression Chart")]
        public static void RunAndExport()
        {
            var bench = UnityEngine.Object.FindAnyObjectByType<BenchmarkSystem>();
            if (bench == null)
            {
                var go = new GameObject("TempBenchmark");
                bench = go.AddComponent<BenchmarkSystem>();
                bench.ExecuteBenchmarkSynchronously(100);
                UnityEngine.Object.DestroyImmediate(go);
            }
            else
            {
                bench.ExecuteBenchmarkSynchronously(100);
            }

            ExportChartPNG();
        }

        [MenuItem("GravityDrift/Export Progression Chart PNG Only")]
        public static void ExportChartPNG()
        {
            string csvPath = Path.Combine(Application.dataPath, "../Telemetry/GravityDrift_Level_Progression.csv");
            string outPath = Path.Combine(Application.dataPath, "../Telemetry/GravityDrift_PerLevel_Improvement.png");

            if (!File.Exists(csvPath))
            {
                Debug.LogError($"CSV not found: {csvPath}");
                return;
            }

            var rows = ParseCsv(csvPath);
            if (rows.Count == 0)
            {
                Debug.LogError("Progression CSV has no data rows.");
                return;
            }

            const int W = 1920;
            const int H = 1080;
            var pixels = new Color32[W * H];

            Color32 bgCol      = new Color32(13, 17, 23, 255);
            Color32 cardCol    = new Color32(22, 27, 34, 255);
            Color32 borderCol  = new Color32(48, 54, 61, 255);
            Color32 gridCol    = new Color32(44, 52, 64, 255);
            Color32 axisCol    = new Color32(139, 148, 158, 255);
            Color32 txtPrimary = new Color32(240, 246, 252, 255);
            Color32 txtMuted   = new Color32(148, 160, 174, 255);

            Color32 cyanCol    = new Color32(0, 229, 255, 255);   // Drone Improvement %
            Color32 greenCol   = new Color32(0, 255, 136, 255);   // Crate-Dodge %
            Color32 goldCol    = new Color32(255, 209, 102, 255); // Posterior pi Confidence %
            Color32 coralCol   = new Color32(255, 107, 107, 255); // Smart Player Attacks
            Color32 coralFill  = new Color32(195, 72, 78, 255);
            Color32 violetCol  = new Color32(199, 125, 255, 255); // Avg Escape Time (s)

            // 1. Background
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bgCol;

            // 2. Cards (top-down layout converted cleanly to Texture2D bottom-up coordinates)
            DrawCard(pixels, W, H, 40, 28, 1840, 118, cardCol, borderCol);
            DrawCard(pixels, W, H, 40, 170, 910, 790, cardCol, borderCol);
            DrawCard(pixels, W, H, 970, 170, 910, 790, cardCol, borderCol);
            DrawCard(pixels, W, H, 40, 982, 1840, 64, cardCol, borderCol);

            // Header Text
            DrawText(pixels, W, H, 68, 42, "GRAVITY DRIFT - PER-LEVEL BAYESIAN DRONE AI & CRATE-DODGE PROGRESSION", 3, txtPrimary);
            DrawText(pixels, W, H, 68, 92, "POSTERIOR: PI_T(S_I) = [ P(O_T | S_I, A_PLAYER, A_DRONE) * SUM_J T(S_I | S_J, A_DRONE, GRAVITY_T) * PI_{T-1}(S_J) ] / Z_T", 2, cyanCol);

            // =========================================================================
            // LEFT PANEL: Drone Improvement %, Crate-Dodge %, Posterior pi Confidence %
            // =========================================================================
            DrawText(pixels, W, H, 68, 192, "1. BAYESIAN POSTERIOR PI CONFIDENCE, CRATE-DODGE & AI IMPROVEMENT (%)", 2, txtPrimary);

            // Legend Left
            FillRectTop(pixels, W, H, 68, 238, 20, 12, cyanCol);
            DrawText(pixels, W, H, 96, 236, "DRONE AI IMPROVEMENT (%)", 2, cyanCol);
            FillRectTop(pixels, W, H, 390, 238, 20, 12, greenCol);
            DrawText(pixels, W, H, 418, 236, "CRATE-DODGE SUCCESS (%)", 2, greenCol);
            FillRectTop(pixels, W, H, 705, 238, 20, 12, goldCol);
            DrawText(pixels, W, H, 733, 236, "POSTERIOR PI (%)", 2, goldCol);

            int lx0 = 135, ly0 = 860, lw = 770, lh = 560;
            for (int p = 0; p <= 100; p += 20)
            {
                int yy = ly0 - Mathf.RoundToInt((p / 100f) * lh);
                DrawDashedHLine(pixels, W, H, lx0, lx0 + lw, yy, gridCol);
                DrawText(pixels, W, H, lx0 - 58, yy - 7, $"{p}%", 2, txtMuted);
            }
            DrawThickLineTop(pixels, W, H, lx0, ly0, lx0 + lw, ly0, 2f, axisCol);
            DrawThickLineTop(pixels, W, H, lx0, ly0, lx0, ly0 - lh, 2f, axisCol);

            int n = rows.Count;
            var ptsImprov = new Vector2[n];
            var ptsDodge  = new Vector2[n];
            var ptsPi     = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                float x = lx0 + (i + 0.5f) * ((float)lw / n);
                ptsImprov[i] = new Vector2(x, ly0 - (r.ImprovementPct / 100f) * lh);
                ptsDodge[i]  = new Vector2(x, ly0 - (r.CrateDodgePct / 100f) * lh);
                ptsPi[i]     = new Vector2(x, ly0 - (r.PosteriorPiPct / 100f) * lh);

                DrawText(pixels, W, H, Mathf.RoundToInt(x) - 28, ly0 + 16, $"LVL {r.Level}", 2, txtPrimary);
                DrawText(pixels, W, H, Mathf.RoundToInt(x) - 42, ly0 + 40, $"{r.DroneCount}D {r.RoomWidth}X{r.RoomHeight}", 2, txtMuted);
            }

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLineTop(pixels, W, H, ptsPi[i].x, ptsPi[i].y, ptsPi[i + 1].x, ptsPi[i + 1].y, 3.5f, goldCol);
                DrawThickLineTop(pixels, W, H, ptsDodge[i].x, ptsDodge[i].y, ptsDodge[i + 1].x, ptsDodge[i + 1].y, 3.5f, greenCol);
                DrawThickLineTop(pixels, W, H, ptsImprov[i].x, ptsImprov[i].y, ptsImprov[i + 1].x, ptsImprov[i + 1].y, 4.0f, cyanCol);
            }

            for (int i = 0; i < n; i++)
            {
                DrawCircleTop(pixels, W, H, ptsPi[i].x, ptsPi[i].y, 7f, goldCol);
                DrawCircleTop(pixels, W, H, ptsDodge[i].x, ptsDodge[i].y, 7f, greenCol);
                DrawCircleTop(pixels, W, H, ptsImprov[i].x, ptsImprov[i].y, 7f, cyanCol);

                DrawText(pixels, W, H, Mathf.RoundToInt(ptsPi[i].x) - 28, Mathf.RoundToInt(ptsPi[i].y) - 26, $"{rows[i].PosteriorPiPct:F1}%", 2, goldCol);
                int dodgeDy = (rows[i].CrateDodgePct >= rows[i].ImprovementPct) ? -24 : 16;
                int impDy   = (rows[i].CrateDodgePct >= rows[i].ImprovementPct) ? 16 : -24;
                DrawText(pixels, W, H, Mathf.RoundToInt(ptsDodge[i].x) - 42, Mathf.RoundToInt(ptsDodge[i].y) + dodgeDy, $"{rows[i].CrateDodgePct:F0}%", 2, greenCol);
                DrawText(pixels, W, H, Mathf.RoundToInt(ptsImprov[i].x) + 4, Mathf.RoundToInt(ptsImprov[i].y) + impDy, $"{rows[i].ImprovementPct:F1}%", 2, cyanCol);
            }

            // =========================================================================
            // RIGHT PANEL: Smart Player Attacks (Bars) vs Avg Crate-Escape Time (sec)
            // =========================================================================
            DrawText(pixels, W, H, 998, 192, "2. SMART PLAYER COUNTERPLAY ATTACKS VS. CRATE-ESCAPE SPEED", 2, txtPrimary);

            // Legend Right
            FillRectTop(pixels, W, H, 1020, 238, 20, 12, coralCol);
            DrawText(pixels, W, H, 1048, 236, "SMART PLAYER ATTACKS (CRATE/BOOST/FLIP)", 2, coralCol);
            FillRectTop(pixels, W, H, 1525, 238, 20, 12, violetCol);
            DrawText(pixels, W, H, 1553, 236, "AVG CRATE-ESCAPE TIME (SEC)", 2, violetCol);

            int rx0 = 1060, ry0 = 860, rw = 720, rh = 560;
            float maxAtk = 22f;
            float maxEsc = 2.0f;

            for (int a = 0; a <= 20; a += 5)
            {
                int yy = ry0 - Mathf.RoundToInt((a / maxAtk) * rh);
                DrawDashedHLine(pixels, W, H, rx0, rx0 + rw, yy, gridCol);
                DrawText(pixels, W, H, rx0 - 44, yy - 7, $"{a}", 2, coralCol);

                float secVal = (a / 20f) * maxEsc;
                DrawText(pixels, W, H, rx0 + rw + 12, yy - 7, $"{secVal:F1}S", 2, violetCol);
            }
            DrawThickLineTop(pixels, W, H, rx0, ry0, rx0 + rw, ry0, 2f, axisCol);
            DrawThickLineTop(pixels, W, H, rx0, ry0, rx0, ry0 - rh, 2f, axisCol);
            DrawThickLineTop(pixels, W, H, rx0 + rw, ry0, rx0 + rw, ry0 - rh, 2f, axisCol);

            var ptsEsc = new Vector2[n];
            int barW = 54;
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                float cx = rx0 + (i + 0.5f) * ((float)rw / n);
                int barH = Mathf.RoundToInt((r.SmartAttacks / maxAtk) * rh);
                int bx = Mathf.RoundToInt(cx - barW * 0.5f);
                int by = ry0 - barH;

                FillRectTop(pixels, W, H, bx, by, barW, barH, coralFill);
                DrawRectOutlineTop(pixels, W, H, bx, by, barW, barH, coralCol);
                DrawText(pixels, W, H, Mathf.RoundToInt(cx) - 12, by + 10, $"{r.SmartAttacks}", 2, txtPrimary);

                ptsEsc[i] = new Vector2(cx, ry0 - (r.AvgEscapeSec / maxEsc) * rh);

                DrawText(pixels, W, H, Mathf.RoundToInt(cx) - 28, ry0 + 16, $"LVL {r.Level}", 2, txtPrimary);
                DrawText(pixels, W, H, Mathf.RoundToInt(cx) - 42, ry0 + 40, $"{r.DroneCount}D {r.RoomWidth}X{r.RoomHeight}", 2, txtMuted);
            }

            for (int i = 0; i < n - 1; i++)
            {
                DrawThickLineTop(pixels, W, H, ptsEsc[i].x, ptsEsc[i].y, ptsEsc[i + 1].x, ptsEsc[i + 1].y, 4.0f, violetCol);
            }
            for (int i = 0; i < n; i++)
            {
                DrawCircleTop(pixels, W, H, ptsEsc[i].x, ptsEsc[i].y, 7f, violetCol);
                DrawText(pixels, W, H, Mathf.RoundToInt(ptsEsc[i].x) - 24, Mathf.RoundToInt(ptsEsc[i].y) - 24, $"{rows[i].AvgEscapeSec:F2}S", 2, violetCol);
            }

            // Footer
            DrawText(pixels, W, H, 68, 1006, "PCG SCALING: 20X12 (LVL 1) -> 30X17 (LVL 6, <= 1.5X CAP) | CRATES ONLY AFFECTED BY GRAVITY | DATA: TELEMETRY/GRAVITYDRIFT_LEVEL_PROGRESSION.CSV", 2, txtMuted);

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, png);
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.Refresh();
            Debug.Log($"<color=#00ff88><b>[CHART EXPORTER]</b></color> Saved per-level improvement PNG: {outPath}");
        }

        private static List<LevelRow> ParseCsv(string path)
        {
            var list = new List<LevelRow>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] parts = lines[i].Split(',');
                if (parts.Length < 9) continue;

                list.Add(new LevelRow
                {
                    Level = int.Parse(parts[0]),
                    DroneCount = int.Parse(parts[1]),
                    RoomWidth = int.Parse(parts[2]),
                    RoomHeight = int.Parse(parts[3]),
                    PosteriorPiPct = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) * 100f,
                    CrateDodgePct = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                    AvgEscapeSec = float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture),
                    SmartAttacks = int.Parse(parts[7]),
                    ImprovementPct = float.Parse(parts[8], System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            return list;
        }

        // Drawing Primitives (Top-Left Origin -> Texture2D Bottom-Up Pixels)
        private static void SetPixelTop(Color32[] px, int w, int h, int x, int yTop, Color32 col)
        {
            if (x < 0 || x >= w || yTop < 0 || yTop >= h) return;
            int y = (h - 1) - yTop;
            px[y * w + x] = col;
        }

        private static void FillRectTop(Color32[] px, int w, int h, int rx, int ry, int rw, int rh, Color32 col)
        {
            for (int dy = 0; dy < rh; dy++)
                for (int dx = 0; dx < rw; dx++)
                    SetPixelTop(px, w, h, rx + dx, ry + dy, col);
        }

        private static void DrawRectOutlineTop(Color32[] px, int w, int h, int rx, int ry, int rw, int rh, Color32 col)
        {
            for (int dx = 0; dx < rw; dx++)
            {
                SetPixelTop(px, w, h, rx + dx, ry, col);
                SetPixelTop(px, w, h, rx + dx, ry + rh - 1, col);
            }
            for (int dy = 0; dy < rh; dy++)
            {
                SetPixelTop(px, w, h, rx, ry + dy, col);
                SetPixelTop(px, w, h, rx + rw - 1, ry + dy, col);
            }
        }

        private static void DrawCard(Color32[] px, int w, int h, int x, int y, int cw, int ch, Color32 fill, Color32 border)
        {
            FillRectTop(px, w, h, x, y, cw, ch, fill);
            DrawRectOutlineTop(px, w, h, x, y, cw, ch, border);
            DrawRectOutlineTop(px, w, h, x + 1, y + 1, cw - 2, ch - 2, border);
        }

        private static void DrawDashedHLine(Color32[] px, int w, int h, int x0, int x1, int y, Color32 col)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (((x - x0) / 8) % 2 == 0) SetPixelTop(px, w, h, x, y, col);
            }
        }

        private static void DrawThickLineTop(Color32[] px, int w, int h, float x0, float y0, float x1, float y1, float thickness, Color32 col)
        {
            float dist = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist * 2f));
            float r = thickness * 0.5f;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                float cx = Mathf.Lerp(x0, x1, t);
                float cy = Mathf.Lerp(y0, y1, t);
                DrawCircleTop(px, w, h, cx, cy, r, col);
            }
        }

        private static void DrawCircleTop(Color32[] px, int w, int h, float cx, float cy, float r, Color32 col)
        {
            int minX = Mathf.FloorToInt(cx - r - 1);
            int maxX = Mathf.CeilToInt(cx + r + 1);
            int minY = Mathf.FloorToInt(cy - r - 1);
            int maxY = Mathf.CeilToInt(cy + r + 1);
            float rSq = r * r;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    if (dx * dx + dy * dy <= rSq)
                    {
                        SetPixelTop(px, w, h, x, y, col);
                    }
                }
            }
        }

        // Minimal 5x7 crisp bitmap font for clean charts in Unity without external font dependencies
        private static void DrawText(Color32[] px, int w, int h, int x, int y, string text, int scale, Color32 col)
        {
            text = text.ToUpperInvariant();
            int cursorX = x;
            foreach (char c in text)
            {
                byte[] glyph = GetGlyph(c);
                for (int row = 0; row < 7; row++)
                {
                    byte bits = glyph[row];
                    for (int colIdx = 0; colIdx < 5; colIdx++)
                    {
                        if ((bits & (1 << (4 - colIdx))) != 0)
                        {
                            FillRectTop(px, w, h, cursorX + colIdx * scale, y + row * scale, scale, scale, col);
                        }
                    }
                }
                cursorX += 6 * scale;
            }
        }

        private static byte[] GetGlyph(char c)
        {
            switch (c)
            {
                case 'A': return new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
                case 'B': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E };
                case 'C': return new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E };
                case 'D': return new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C };
                case 'E': return new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F };
                case 'F': return new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 };
                case 'G': return new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F };
                case 'H': return new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
                case 'I': return new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E };
                case 'J': return new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C };
                case 'K': return new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 };
                case 'L': return new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F };
                case 'M': return new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 };
                case 'N': return new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 };
                case 'O': return new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
                case 'P': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 };
                case 'Q': return new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D };
                case 'R': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 };
                case 'S': return new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E };
                case 'T': return new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 };
                case 'U': return new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
                case 'V': return new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 };
                case 'W': return new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 };
                case 'X': return new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 };
                case 'Y': return new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 };
                case 'Z': return new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F };
                case '0': return new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E };
                case '1': return new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E };
                case '2': return new byte[] { 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F };
                case '3': return new byte[] { 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E };
                case '4': return new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 };
                case '5': return new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E };
                case '6': return new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E };
                case '7': return new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 };
                case '8': return new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E };
                case '9': return new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C };
                case '.': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C };
                case ',': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x04, 0x04, 0x08 };
                case ':': return new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 };
                case '-': return new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 };
                case '+': return new byte[] { 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00 };
                case '=': return new byte[] { 0x00, 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00 };
                case '%': return new byte[] { 0x19, 0x19, 0x02, 0x04, 0x08, 0x13, 0x13 };
                case '/': return new byte[] { 0x01, 0x02, 0x02, 0x04, 0x08, 0x08, 0x10 };
                case '(': return new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 };
                case ')': return new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 };
                case '[': return new byte[] { 0x0E, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0E };
                case ']': return new byte[] { 0x0E, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0E };
                case '{': return new byte[] { 0x02, 0x04, 0x04, 0x08, 0x04, 0x04, 0x02 };
                case '}': return new byte[] { 0x08, 0x04, 0x04, 0x02, 0x04, 0x04, 0x08 };
                case '|': return new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 };
                case '_': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F };
                case '&': return new byte[] { 0x0C, 0x12, 0x14, 0x08, 0x15, 0x12, 0x0D };
                case '*': return new byte[] { 0x00, 0x15, 0x0E, 0x1F, 0x0E, 0x15, 0x00 };
                case '<': return new byte[] { 0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02 };
                case '>': return new byte[] { 0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08 };
                default:  return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
        }
    }
}
