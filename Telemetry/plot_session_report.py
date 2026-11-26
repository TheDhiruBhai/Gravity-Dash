"""
Export publication-ready PNG report figures from a Gravity Drift live session
(*_levels.csv and *_per_input_ai.csv in Telemetry/Sessions/).

Usage:
    python Telemetry/plot_session_report.py
    python Telemetry/plot_session_report.py --session Telemetry/Sessions/GD_20261125_052000_101
    python Telemetry/plot_session_report.py --levels path/to/levels.csv --inputs path/to/inputs.csv --out out.png
"""

import argparse
import csv
import glob
import os
import struct
import zlib
from typing import Dict, List, Optional, Tuple


BG_DARK = "#0B0E14"
PANEL_DARK = "#141923"
CONTROL_DARK = "#1E2633"
BORDER_SLATE = "#2A3444"
TEXT_PRIMARY = "#E2E8F0"
TEXT_MUTED = "#8A99AD"
SKY_BLUE = "#38BDF8"
CYAN_GLOW = "#00E5FF"
MINT_GREEN = "#22C55E"
DANGER_RED = "#EF4444"
AMBER_GOLD = "#F59E0B"


def find_latest_session_prefix(search_dirs: List[str]) -> Optional[str]:
    candidates: List[str] = []
    for d in search_dirs:
        if os.path.isdir(d):
            candidates.extend(glob.glob(os.path.join(d, "*_levels.csv")))
    if not candidates:
        return None
    latest = max(candidates, key=os.path.getmtime)
    return latest[:-len("_levels.csv")]


def read_csv_rows(path: str) -> List[Dict[str, str]]:
    if not os.path.isfile(path):
        return []
    with open(path, "r", newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def plot_with_matplotlib(
    level_rows: List[Dict[str, str]],
    input_rows: List[Dict[str, str]],
    session_name: str,
    out_path: str,
) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    plt.rcParams.update({
        "figure.facecolor": BG_DARK,
        "axes.facecolor": PANEL_DARK,
        "axes.edgecolor": BORDER_SLATE,
        "axes.labelcolor": TEXT_PRIMARY,
        "xtick.color": TEXT_MUTED,
        "ytick.color": TEXT_MUTED,
        "text.color": TEXT_PRIMARY,
        "font.size": 10,
    })

    fig, axes = plt.subplots(1, 3, figsize=(19.2, 6.8), dpi=150)
    fig.patch.set_facecolor(BG_DARK)
    fig.suptitle(
        f"GRAVITY DRIFT — LIVE SESSION TELEMETRY & AI REPORT ({session_name})",
        color=SKY_BLUE,
        fontsize=15,
        fontweight="bold",
        y=0.97,
    )

    # 1. Levels Played & Progression
    ax1 = axes[0]
    ax1.set_title("1. Levels Played & Progression", color=SKY_BLUE, fontweight="bold", pad=10)
    if level_rows:
        x_idx = list(range(len(level_rows)))
        labels = [f"L{r['level_index']}\n#{r['attempt_index']}" for r in level_rows]
        timers = [float(r["room_timer_sec"]) for r in level_rows]
        colors = [
            DANGER_RED if r["outcome"] == "Death_Tagged"
            else (MINT_GREEN if r["room_cleared"].lower() == "true" or r["outcome"] == "Cleared" else SKY_BLUE)
            for r in level_rows
        ]
        ax1.bar(x_idx, timers, color=colors, alpha=0.78, edgecolor=colors, linewidth=1.2, label="Attempt Duration (s)")
        ax1.plot(x_idx, timers, color=CYAN_GLOW, marker="o", linewidth=1.8, markersize=5, label="Time Trend")
        if "complexity_score" in level_rows[0]:
            comps = [float(r["complexity_score"]) for r in level_rows]
            ax1b = ax1.twinx()
            ax1b.plot(x_idx, comps, color=AMBER_GOLD, marker="D", linewidth=1.8, markersize=4.5, label="Complexity Score")
            ax1b.set_ylabel("Complexity Score", color=AMBER_GOLD)
            ax1b.tick_params(axis="y", colors=AMBER_GOLD)
        ax1.set_xticks(x_idx)
        ax1.set_xticklabels(labels)
    ax1.set_ylabel("Room Timer (sec)")
    ax1.grid(True, linestyle="--", alpha=0.25, color=TEXT_MUTED)
    ax1.legend(facecolor=CONTROL_DARK, edgecolor=BORDER_SLATE, fontsize=8)

    # 2. AI Improvement Per Input
    ax2 = axes[1]
    ax2.set_title("2. AI Improvement Per Input", color=SKY_BLUE, fontweight="bold", pad=10)
    if input_rows:
        inp_x = [int(r["input_index"]) for r in input_rows]
        pi_conf = [float(r["top_cluster_confidence"]) * 100.0 for r in input_rows]
        precision = [
            max(0.0, min(100.0, (1.0 - float(r["posterior_entropy"]) / 5.2) * 100.0 + float(r["entropy_reduction"]) * 25.0))
            for r in input_rows
        ]
        ai_imp = [float(r["drone_improvement_pct"]) for r in input_rows]
        ax2.plot(inp_x, pi_conf, color=SKY_BLUE, linewidth=2.0, label="Posterior pi Cluster (%)")
        ax2.plot(inp_x, precision, color=AMBER_GOLD, linewidth=1.7, label="Entropy Precision (%)")
        ax2.plot(inp_x, ai_imp, color=MINT_GREEN, linewidth=1.8, label="Drone AI Imp (%)")
        if "pattern_adaptation_pct" in input_rows[0]:
            pat_adapt = [float(r["pattern_adaptation_pct"]) for r in input_rows]
            ax2.plot(inp_x, pat_adapt, color=CYAN_GLOW, linestyle="--", linewidth=1.6, label="Pattern Adaptation (%)")
    ax2.set_xlabel("Input / Action Index")
    ax2.set_ylabel("Percentage (%)")
    ax2.set_ylim(0, 105)
    ax2.grid(True, linestyle="--", alpha=0.25, color=TEXT_MUTED)
    ax2.legend(facecolor=CONTROL_DARK, edgecolor=BORDER_SLATE, fontsize=8)

    # 3. Death Rate & Improvement
    ax3 = axes[2]
    ax3.set_title("3. Death Rate & AI Improvement", color=SKY_BLUE, fontweight="bold", pad=10)
    if level_rows:
        x3 = list(range(len(level_rows)))
        labels3 = [f"L{r['level_index']} #{r['attempt_index']}" for r in level_rows]
        death_rate = [float(r["cumulative_death_rate_pct"]) for r in level_rows]
        imp_pct = [float(r["drone_improvement_pct"]) for r in level_rows]
        dodge_pct = [float(r["crate_dodge_skill"]) * 100.0 for r in level_rows]
        ax3.plot(x3, death_rate, color=DANGER_RED, marker="o", linewidth=2.2, label="Cumulative Death Rate (%)")
        ax3.plot(x3, imp_pct, color=SKY_BLUE, marker="s", linewidth=2.2, label="Drone AI Improvement (%)")
        ax3.plot(x3, dodge_pct, color=MINT_GREEN, marker="^", linewidth=1.8, label="Crate-Dodge Skill (%)")
        ax3.set_xticks(x3)
        ax3.set_xticklabels(labels3)
    ax3.set_ylabel("Rate / Improvement (%)")
    ax3.set_ylim(0, 105)
    ax3.grid(True, linestyle="--", alpha=0.25, color=TEXT_MUTED)
    ax3.legend(facecolor=CONTROL_DARK, edgecolor=BORDER_SLATE, fontsize=8)

    plt.tight_layout(rect=[0.01, 0.02, 0.99, 0.92])
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    fig.savefig(out_path, dpi=150, facecolor=BG_DARK)
    plt.close(fig)


# Pure-stdlib PNG fallback (zero external dependencies required)
def _hex_rgb(h: str) -> Tuple[int, int, int]:
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def _set_px(buf: bytearray, w: int, h: int, x: int, y: int, col: Tuple[int, int, int]) -> None:
    if 0 <= x < w and 0 <= y < h:
        idx = (y * w + x) * 3
        buf[idx], buf[idx + 1], buf[idx + 2] = col


def _fill_rect(buf: bytearray, w: int, h: int, x0: int, y0: int, rw: int, rh: int, col: Tuple[int, int, int]) -> None:
    for y in range(max(0, y0), min(h, y0 + rh)):
        for x in range(max(0, x0), min(w, x0 + rw)):
            idx = (y * w + x) * 3
            buf[idx], buf[idx + 1], buf[idx + 2] = col


def _draw_circle(buf: bytearray, w: int, h: int, cx: float, cy: float, r: float, col: Tuple[int, int, int]) -> None:
    r2 = r * r
    for y in range(max(0, int(cy - r)), min(h, int(cy + r) + 1)):
        for x in range(max(0, int(cx - r)), min(w, int(cx + r) + 1)):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r2:
                _set_px(buf, w, h, x, y, col)


def _draw_line(buf: bytearray, w: int, h: int, x0: float, y0: float, x1: float, y1: float, r: float, col: Tuple[int, int, int]) -> None:
    dist = ((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5
    steps = max(1, int(dist * 1.5))
    for i in range(steps + 1):
        t = i / steps
        _draw_circle(buf, w, h, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r, col)


def _write_png_rgb(path: str, w: int, h: int, rgb: bytearray) -> None:
    raw = bytearray()
    stride = w * 3
    for y in range(h):
        raw.append(0)
        start = y * stride
        raw.extend(rgb[start:start + stride])

    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(bytes(raw), 6))
        + chunk(b"IEND", b"")
    )
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


def plot_stdlib_png(
    level_rows: List[Dict[str, str]],
    input_rows: List[Dict[str, str]],
    out_path: str,
) -> None:
    W, H = 1920, 680
    bg = _hex_rgb(BG_DARK)
    panel = _hex_rgb(PANEL_DARK)
    border = _hex_rgb(BORDER_SLATE)
    grid = (34, 44, 58)
    sky = _hex_rgb(SKY_BLUE)
    cyan = _hex_rgb(CYAN_GLOW)
    mint = _hex_rgb(MINT_GREEN)
    red = _hex_rgb(DANGER_RED)
    amber = _hex_rgb(AMBER_GOLD)

    buf = bytearray(bg * (W * H))
    cards = [(30, 50, 590, 590), (665, 50, 590, 590), (1300, 50, 590, 590)]
    for cx, cy, cw, ch in cards:
        _fill_rect(buf, W, H, cx, cy, cw, ch, panel)
        _draw_line(buf, W, H, cx, cy, cx + cw, cy, 1.0, border)
        _draw_line(buf, W, H, cx, cy + ch, cx + cw, cy + ch, 1.0, border)
        _draw_line(buf, W, H, cx, cy, cx, cy + ch, 1.0, border)
        _draw_line(buf, W, H, cx + cw, cy, cx + cw, cy + ch, 1.0, border)
        px0, py0, pw, ph = cx + 45, cy + 530, cw - 75, 450
        for g in range(5):
            gy = int(py0 - (g / 4.0) * ph)
            for gx in range(px0, px0 + pw, 8):
                _set_px(buf, W, H, gx, gy, grid)
                _set_px(buf, W, H, gx + 1, gy, grid)
        _draw_line(buf, W, H, px0, py0, px0 + pw, py0, 1.0, border)
        _draw_line(buf, W, H, px0, py0, px0, py0 - ph, 1.0, border)

    # Panel 1: Levels
    if level_rows:
        px0, py0, pw, ph = cards[0][0] + 45, cards[0][1] + 530, cards[0][2] - 75, 450
        max_t = max([float(r["room_timer_sec"]) for r in level_rows] + [5.0])
        max_c = max([float(r.get("complexity_score", 0.0) or 0.0) for r in level_rows] + [120.0])
        n = len(level_rows)
        slot = pw / max(1, n)
        pts = []
        pts_c = []
        for i, r in enumerate(level_rows):
            cx = px0 + (i + 0.5) * slot
            t = float(r["room_timer_sec"])
            c = float(r.get("complexity_score", 0.0) or 0.0)
            bh = max(12, int((t / max_t) * ph))
            bw = max(12, min(42, int(slot * 0.45)))
            col = red if r["outcome"] == "Death_Tagged" else (mint if r["room_cleared"].lower() == "true" or r["outcome"] == "Cleared" else sky)
            _fill_rect(buf, W, H, int(cx - bw / 2), py0 - bh, bw, bh, col)
            pts.append((cx, py0 - (t / max_t) * ph))
            pts_c.append((cx, py0 - min(1.0, c / max_c) * ph))
        for i in range(len(pts) - 1):
            _draw_line(buf, W, H, pts_c[i][0], pts_c[i][1], pts_c[i + 1][0], pts_c[i + 1][1], 1.5, amber)
            _draw_line(buf, W, H, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], 1.5, cyan)
        for p in pts_c:
            _draw_circle(buf, W, H, p[0], p[1], 4.0, amber)
        for p in pts:
            _draw_circle(buf, W, H, p[0], p[1], 4.5, cyan)

    # Panel 2: AI per input
    if input_rows:
        px0, py0, pw, ph = cards[1][0] + 45, cards[1][1] + 530, cards[1][2] - 75, 450
        n = len(input_rows)
        pts_c, pts_p, pts_i = [], [], []
        for i, r in enumerate(input_rows):
            x = px0 + (0.5 * pw if n == 1 else (i / (n - 1)) * pw)
            conf = max(0.0, min(1.0, float(r["top_cluster_confidence"])))
            prec = max(0.0, min(1.0, 1.0 - float(r["posterior_entropy"]) / 5.2 + float(r["entropy_reduction"]) * 0.25))
            imp = max(0.0, min(1.0, float(r["drone_improvement_pct"]) / 100.0))
            pts_c.append((x, py0 - conf * ph))
            pts_p.append((x, py0 - prec * ph))
            pts_i.append((x, py0 - imp * ph))
        for i in range(n - 1):
            _draw_line(buf, W, H, pts_i[i][0], pts_i[i][1], pts_i[i + 1][0], pts_i[i + 1][1], 1.4, mint)
            _draw_line(buf, W, H, pts_p[i][0], pts_p[i][1], pts_p[i + 1][0], pts_p[i + 1][1], 1.4, amber)
            _draw_line(buf, W, H, pts_c[i][0], pts_c[i][1], pts_c[i + 1][0], pts_c[i + 1][1], 1.8, sky)

    # Panel 3: Death Rate & Improvement
    if level_rows:
        px0, py0, pw, ph = cards[2][0] + 45, cards[2][1] + 530, cards[2][2] - 75, 450
        n = len(level_rows)
        pts_d, pts_i, pts_s = [], [], []
        for i, r in enumerate(level_rows):
            x = px0 + (0.5 * pw if n == 1 else (i / (n - 1)) * pw)
            dr = max(0.0, min(1.0, float(r["cumulative_death_rate_pct"]) / 100.0))
            imp = max(0.0, min(1.0, float(r["drone_improvement_pct"]) / 100.0))
            sk = max(0.0, min(1.0, float(r["crate_dodge_skill"])))
            pts_d.append((x, py0 - dr * ph))
            pts_i.append((x, py0 - imp * ph))
            pts_s.append((x, py0 - sk * ph))
        for i in range(n - 1):
            _draw_line(buf, W, H, pts_s[i][0], pts_s[i][1], pts_s[i + 1][0], pts_s[i + 1][1], 1.4, mint)
            _draw_line(buf, W, H, pts_i[i][0], pts_i[i][1], pts_i[i + 1][0], pts_i[i + 1][1], 1.8, sky)
            _draw_line(buf, W, H, pts_d[i][0], pts_d[i][1], pts_d[i + 1][0], pts_d[i + 1][1], 1.8, red)
        for i in range(n):
            _draw_circle(buf, W, H, pts_i[i][0], pts_i[i][1], 4.5, sky)
            _draw_circle(buf, W, H, pts_d[i][0], pts_d[i][1], 4.5, red)

    _write_png_rgb(out_path, W, H, buf)


def main() -> int:
    parser = argparse.ArgumentParser(description="Export Gravity Drift session report PNG figures.")
    parser.add_argument("--session", help="Session prefix (e.g. Telemetry/Sessions/GD_20261125_052000_101)")
    parser.add_argument("--levels", help="Explicit *_levels.csv path")
    parser.add_argument("--inputs", help="Explicit *_per_input_ai.csv path")
    parser.add_argument("--out", help="Output PNG path")
    args = parser.parse_args()

    prefix = args.session
    if not prefix and not args.levels:
        prefix = find_latest_session_prefix(["Telemetry/Sessions", "Telemetry"])
        if not prefix:
            print("No *_levels.csv session files found in Telemetry/Sessions.")
            return 1

    levels_csv = args.levels or f"{prefix}_levels.csv"
    inputs_csv = args.inputs or f"{prefix}_per_input_ai.csv"
    session_name = os.path.basename(prefix) if prefix else os.path.splitext(os.path.basename(levels_csv))[0]
    out_path = args.out or (f"{prefix}_report.png" if prefix else os.path.join("Telemetry/Sessions", f"{session_name}_report.png"))

    level_rows = read_csv_rows(levels_csv)
    input_rows = read_csv_rows(inputs_csv)

    try:
        plot_with_matplotlib(level_rows, input_rows, session_name, out_path)
        print(f"Saved Matplotlib session report PNG: {out_path} ({len(level_rows)} level rows, {len(input_rows)} input events)")
    except ImportError:
        plot_stdlib_png(level_rows, input_rows, out_path)
        print(f"matplotlib not installed; saved stdlib PNG fallback: {out_path} ({len(level_rows)} level rows, {len(input_rows)} input events)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
