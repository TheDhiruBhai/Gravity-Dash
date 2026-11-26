param(
    [string]$CsvPath = "Telemetry/GravityDrift_Level_Progression.csv",
    [string]$OutPath = "Telemetry/GravityDrift_PerLevel_Improvement.png"
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $CsvPath)) {
    Write-Error "CSV file not found: $CsvPath"
    exit 1
}

$rows = Import-Csv $CsvPath
if ($rows.Count -eq 0) {
    Write-Error "CSV file is empty."
    exit 1
}

$width = 1920
$height = 1080
$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# Color Palette (Modern Aerospace / Dark Presentation Theme)
$bgColor       = [System.Drawing.Color]::FromArgb(255, 13, 17, 23)
$cardColor     = [System.Drawing.Color]::FromArgb(255, 22, 27, 34)
$borderColor   = [System.Drawing.Color]::FromArgb(255, 48, 54, 61)
$gridColor     = [System.Drawing.Color]::FromArgb(80, 110, 120, 135)
$textPrimary   = [System.Drawing.Color]::FromArgb(255, 240, 246, 252)
$textMuted     = [System.Drawing.Color]::FromArgb(255, 139, 148, 158)

$cyanCol       = [System.Drawing.Color]::FromArgb(255, 0, 229, 255)   # Drone Improvement %
$greenCol      = [System.Drawing.Color]::FromArgb(255, 0, 255, 136)   # Crate Dodge %
$goldCol       = [System.Drawing.Color]::FromArgb(255, 255, 209, 102) # Posterior pi Confidence %
$coralCol      = [System.Drawing.Color]::FromArgb(255, 255, 107, 107) # Smart Player Attacks (bars)
$coralFill     = [System.Drawing.Color]::FromArgb(165, 255, 107, 107)
$violetCol     = [System.Drawing.Color]::FromArgb(255, 199, 125, 255) # Avg Crate Escape Time (s)

$g.Clear($bgColor)

# Fonts
$titleFont    = New-Object System.Drawing.Font("Segoe UI", 26, [System.Drawing.FontStyle]::Bold)
$subFont      = New-Object System.Drawing.Font("Consolas", 13, [System.Drawing.FontStyle]::Regular)
$panelTitle   = New-Object System.Drawing.Font("Segoe UI", 17, [System.Drawing.FontStyle]::Bold)
$axisFont     = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$tickFont     = New-Object System.Drawing.Font("Consolas", 11, [System.Drawing.FontStyle]::Regular)
$valFont      = New-Object System.Drawing.Font("Consolas", 11, [System.Drawing.FontStyle]::Bold)
$legendFont   = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$footerFont   = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Regular)

# Brushes & Pens
$textBrush    = New-Object System.Drawing.SolidBrush($textPrimary)
$mutedBrush   = New-Object System.Drawing.SolidBrush($textMuted)
$cyanBrush    = New-Object System.Drawing.SolidBrush($cyanCol)
$greenBrush   = New-Object System.Drawing.SolidBrush($greenCol)
$goldBrush    = New-Object System.Drawing.SolidBrush($goldCol)
$coralBrush   = New-Object System.Drawing.SolidBrush($coralFill)
$coralText    = New-Object System.Drawing.SolidBrush($coralCol)
$violetBrush  = New-Object System.Drawing.SolidBrush($violetCol)
$cardBrush    = New-Object System.Drawing.SolidBrush($cardColor)
$borderPen    = New-Object System.Drawing.Pen($borderColor, 2)
$gridPen      = New-Object System.Drawing.Pen($gridColor, 1)
$gridPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash

# Header Banner
$headerRect = New-Object System.Drawing.Rectangle(40, 28, 1840, 118)
$g.FillRectangle($cardBrush, $headerRect)
$g.DrawRectangle($borderPen, $headerRect)
$g.DrawString("GRAVITY DRIFT - PER-LEVEL BAYESIAN DRONE AI & CRATE-DODGE PROGRESSION", $titleFont, $textBrush, 68, 42)
$g.DrawString("Posterior: pi_t(s_i) = [ P(o_t | s_i, a_player, a_drone) * Sum_j T(s_i | s_j, a_drone, gravity_t) * pi_{t-1}(s_j) ] / Z_t", $subFont, $cyanBrush, 68, 96)

# Helper function to draw a rounded/bordered card
$leftPanel  = New-Object System.Drawing.Rectangle(40, 170, 910, 790)
$rightPanel = New-Object System.Drawing.Rectangle(970, 170, 910, 790)
$g.FillRectangle($cardBrush, $leftPanel)
$g.DrawRectangle($borderPen, $leftPanel)
$g.FillRectangle($cardBrush, $rightPanel)
$g.DrawRectangle($borderPen, $rightPanel)

# ==========================================================
# LEFT CHART: AI Improvement %, Crate-Dodge %, Posterior pi %
# ==========================================================
$g.DrawString("1. Bayesian Posterior Confidence (pi), Crate-Dodge & Improvement (%)", $panelTitle, $textBrush, 68, 192)

$lx0 = 130; $ly0 = 860; $lw = 770; $lh = 560
$axisPen = New-Object System.Drawing.Pen($textMuted, 2)

# Gridlines 0..100%
for ($p = 0; $p -le 100; $p += 20) {
    $yy = [int]($ly0 - ($p / 100.0) * $lh)
    $g.DrawLine($gridPen, $lx0, $yy, $lx0 + $lw, $yy)
    $g.DrawString("$p%", $tickFont, $mutedBrush, $lx0 - 52, $yy - 9)
}
$g.DrawLine($axisPen, $lx0, $ly0, $lx0 + $lw, $ly0)
$g.DrawLine($axisPen, $lx0, $ly0, $lx0, $ly0 - $lh)
$g.DrawString("Performance / Confidence (%)", $axisFont, $mutedBrush, 68, 250)

$n = $rows.Count
$ptsImprov = New-Object System.Collections.Generic.List[System.Drawing.PointF]
$ptsDodge  = New-Object System.Collections.Generic.List[System.Drawing.PointF]
$ptsPi     = New-Object System.Collections.Generic.List[System.Drawing.PointF]

for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]
    $x = [float]($lx0 + ($i + 0.5) * ($lw / $n))
    $improv = [double]$r.DroneImprovementPct
    $dodge  = [double]$r.CrateDodgeSuccessPct
    $piPct  = ([double]$r.PosteriorPiConfidence) * 100.0

    $ptsImprov.Add((New-Object System.Drawing.PointF($x, [float]($ly0 - ($improv / 100.0) * $lh))))
    $ptsDodge.Add((New-Object System.Drawing.PointF($x, [float]($ly0 - ($dodge / 100.0) * $lh))))
    $ptsPi.Add((New-Object System.Drawing.PointF($x, [float]($ly0 - ($piPct / 100.0) * $lh))))

    $lvlLabel = "Lvl $($r.Level)`n($($r.DroneCount)D|$($r.RoomWidth)x$($r.RoomHeight))"
    $g.DrawString("Lvl $($r.Level)", $tickFont, $textBrush, $x - 22, $ly0 + 12)
    $g.DrawString("$($r.DroneCount)D $($r.RoomWidth)x$($r.RoomHeight)", $footerFont, $mutedBrush, $x - 38, $ly0 + 32)
}

$penCyan  = New-Object System.Drawing.Pen($cyanCol, 4)
$penGreen = New-Object System.Drawing.Pen($greenCol, 4)
$penGold  = New-Object System.Drawing.Pen($goldCol, 4)

$g.DrawLines($penGold,  $ptsPi.ToArray())
$g.DrawLines($penGreen, $ptsDodge.ToArray())
$g.DrawLines($penCyan,  $ptsImprov.ToArray())

for ($i = 0; $i -lt $n; $i++) {
    $p1 = $ptsImprov[$i]; $p2 = $ptsDodge[$i]; $p3 = $ptsPi[$i]
    $g.FillEllipse($cyanBrush,  $p1.X - 7, $p1.Y - 7, 14, 14)
    $g.FillEllipse($greenBrush, $p2.X - 7, $p2.Y - 7, 14, 14)
    $g.FillEllipse($goldBrush,  $p3.X - 7, $p3.Y - 7, 14, 14)

    $g.DrawString("{0:F1}%" -f [double]$rows[$i].DroneImprovementPct, $valFont, $cyanBrush, $p1.X - 24, $p1.Y + 12)
    $g.DrawString("{0:F0}%" -f [double]$rows[$i].CrateDodgeSuccessPct, $valFont, $greenBrush, $p2.X - 18, $p2.Y - 24)
    $g.DrawString("{0:F1}%" -f (([double]$rows[$i].PosteriorPiConfidence)*100), $valFont, $goldBrush, $p3.X - 24, $p3.Y - 24)
}

# Legend (Left Panel)
$g.FillRectangle($cyanBrush, 140, 240, 18, 12); $g.DrawString("Drone AI Improvement (%)", $legendFont, $cyanBrush, 165, 234)
$g.FillRectangle($greenBrush, 415, 240, 18, 12); $g.DrawString("Crate-Dodge Success (%)", $legendFont, $greenBrush, 440, 234)
$g.FillRectangle($goldBrush, 680, 240, 18, 12); $g.DrawString("Posterior pi Confidence (%)", $legendFont, $goldBrush, 705, 234)

# ==========================================================
# RIGHT CHART: Smart Player Attacks (Bars) vs Avg Crate Escape Time (sec)
# ==========================================================
$g.DrawString("2. Smart Player Counterplay Attacks vs. Crate-Escape Speed", $panelTitle, $textBrush, 998, 192)

$rx0 = 1060; $ry0 = 860; $rw = 740; $rh = 560
$maxAttacks = 22.0
$maxEscape  = 2.0

for ($a = 0; $a -le 20; $a += 5) {
    $yy = [int]($ry0 - ($a / $maxAttacks) * $rh)
    $g.DrawLine($gridPen, $rx0, $yy, $rx0 + $rw, $yy)
    $g.DrawString("$a", $tickFont, $coralText, $rx0 - 38, $yy - 9)

    $secVal = ($a / 20.0) * $maxEscape
    $g.DrawString("{0:F1}s" -f $secVal, $tickFont, $violetBrush, $rx0 + $rw + 10, $yy - 9)
}
$g.DrawLine($axisPen, $rx0, $ry0, $rx0 + $rw, $ry0)
$g.DrawLine($axisPen, $rx0, $ry0, $rx0, $ry0 - $rh)
$g.DrawLine($axisPen, $rx0 + $rw, $ry0, $rx0 + $rw, $ry0 - $rh)
$g.DrawString("Smart Attacks (Count)", $axisFont, $coralText, 998, 250)
$g.DrawString("Escape Time (s)", $axisFont, $violetBrush, 1735, 250)

$ptsEscape = New-Object System.Collections.Generic.List[System.Drawing.PointF]
$barW = 54

for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]
    $cx = [float]($rx0 + ($i + 0.5) * ($rw / $n))
    $atk = [double]$r.SmartPlayerAttacks
    $esc = [double]$r.AvgCrateEscapeSec

    $barH = [int](($atk / $maxAttacks) * $rh)
    $barRect = New-Object System.Drawing.Rectangle([int]($cx - $barW/2), [int]($ry0 - $barH), $barW, $barH)
    $g.FillRectangle($coralBrush, $barRect)
    $penBar = New-Object System.Drawing.Pen($coralCol, 2)
    $g.DrawRectangle($penBar, $barRect)

    $g.DrawString("$($r.SmartPlayerAttacks) atk", $valFont, $coralText, $cx - 26, $ry0 - $barH - 22)

    $escPt = New-Object System.Drawing.PointF($cx, [float]($ry0 - ($esc / $maxEscape) * $rh))
    $ptsEscape.Add($escPt)

    $g.DrawString("Lvl $($r.Level)", $tickFont, $textBrush, $cx - 22, $ry0 + 12)
    $g.DrawString("$($r.DroneCount)D $($r.RoomWidth)x$($r.RoomHeight)", $footerFont, $mutedBrush, $cx - 38, $ry0 + 32)
}

$penViolet = New-Object System.Drawing.Pen($violetCol, 4)
$g.DrawLines($penViolet, $ptsEscape.ToArray())

for ($i = 0; $i -lt $n; $i++) {
    $pe = $ptsEscape[$i]
    $g.FillEllipse($violetBrush, $pe.X - 7, $pe.Y - 7, 14, 14)
    $g.DrawString("{0:F2}s" -f [double]$rows[$i].AvgCrateEscapeSec, $valFont, $violetBrush, $pe.X - 22, $pe.Y - 26)
}

# Legend (Right Panel)
$g.FillRectangle($coralBrush, 1210, 240, 18, 12); $g.DrawString("Smart Player Attacks (Crate/Boost/Flip)", $legendFont, $coralText, 1235, 234)
$g.FillRectangle($violetBrush, 1570, 240, 18, 12); $g.DrawString("Avg Crate-Escape Time (s)", $legendFont, $violetBrush, 1595, 234)

# Footer Strip
$footerRect = New-Object System.Drawing.Rectangle(40, 982, 1840, 64)
$g.FillRectangle($cardBrush, $footerRect)
$g.DrawRectangle($borderPen, $footerRect)
$g.DrawString("PCG Scaling: 20x12 (Lvl 1) -> 30x17 (Lvl 6, capped <= 1.5x) | Crate Cap: 2-3 per Arena | Gravity drives Crates ONLY (Player & Drones Hover-Neutral) | Source: Telemetry/GravityDrift_Level_Progression.csv", $footerFont, $mutedBrush, 64, 1004)

$outDir = Split-Path -Parent $OutPath
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Output "Generated high-resolution progression chart: $OutPath ($width x $height)"
