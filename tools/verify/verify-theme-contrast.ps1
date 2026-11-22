# Checks WCAG contrast of ThemeService text/accent brushes against the card surface, without launching MochiDock.
# Parses MochiDock/Services/ThemeService.cs (Matcha block = "if (isMatcha)", Dark block = "else", Hojicha = Dark + ApplyHojichaPalette).
$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Get-Content (Join-Path $repo "MochiDock/Services/ThemeService.cs") -Raw
$matchaStart = $source.IndexOf("if (isMatcha)")
$darkStart = $source.IndexOf("else", $matchaStart)
if ($matchaStart -lt 0 -or $darkStart -lt 0) { throw "Could not locate theme blocks in ThemeService.cs" }
$hojichaStart = $source.IndexOf("private static void ApplyHojichaPalette")
if ($hojichaStart -lt 0) { throw "Could not locate ApplyHojichaPalette in ThemeService.cs" }
$hojichaEnd = $source.IndexOf("private static void SetBrushes", $hojichaStart)
$darkEnd = $source.IndexOf("if (isHojicha) ApplyHojichaPalette", $darkStart)
$blocks = @{ Matcha = $source.Substring($matchaStart, $darkStart - $matchaStart); Dark = $source.Substring($darkStart, $darkEnd - $darkStart)
             Hojicha = $source.Substring($hojichaStart, $hojichaEnd - $hojichaStart) }

function Read-Palette([string]$text) {
    $palette = @{}
    foreach ($m in [regex]::Matches($text, 'SetBrushes\(app,\s*"(#[0-9A-Fa-f]{6,8})"\s*,([^)]*)\)')) {
        foreach ($n in [regex]::Matches($m.Groups[2].Value, '"(\w+)"')) { $palette[$n.Groups[1].Value] = $m.Groups[1].Value }
    }
    foreach ($m in [regex]::Matches($text, 'Resources\["(\w+)"\]\s*=\s*(?:new SolidColorBrush|FrozenBrush)\(MediaColor\.(FromRgb|FromArgb)\(([^)]*)\)')) {
        $v = $m.Groups[3].Value -split ',' | ForEach-Object { [int]$_.Trim() }
        if ($m.Groups[2].Value -eq "FromArgb") { $palette[$m.Groups[1].Value] = "#{0:X2}{1:X2}{2:X2}{3:X2}" -f $v[0], $v[1], $v[2], $v[3] }
        else { $palette[$m.Groups[1].Value] = "#{0:X2}{1:X2}{2:X2}" -f $v[0], $v[1], $v[2] }
    }
    $palette
}
function Get-Luminance([string]$hex) {
    $h = $hex.TrimStart('#'); if ($h.Length -eq 8) { $h = $h.Substring(2) }
    $c = 0..2 | ForEach-Object { [Convert]::ToInt32($h.Substring($_ * 2, 2), 16) / 255.0 }
    $l = $c | ForEach-Object { if ($_ -le 0.03928) { $_ / 12.92 } else { [Math]::Pow(($_ + 0.055) / 1.055, 2.4) } }
    0.2126 * $l[0] + 0.7152 * $l[1] + 0.0722 * $l[2]
}
function Get-Contrast([string]$a, [string]$b) {
    $x = Get-Luminance $a; $y = Get-Luminance $b
    if ($x -lt $y) { $x, $y = $y, $x }
    ($x + 0.05) / ($y + 0.05)
}

$textBrushes = "IslandInkBrush", "IslandSecondaryInkBrush", "IslandMutedInkBrush", "IslandAccentBrush", "IslandHighlightBrush",
    "IslandPositiveBrush", "IslandNegativeBrush", "IslandWarningBrush", "IslandGreenBrush", "IslandOrangeBrush", "IslandRedBrush",
    "IslandBlueBrush", "IslandInfoBrush", "IslandWhatsAppBrush", "IslandSaturdayBrush"
$failures = @()
foreach ($theme in "Dark", "Matcha", "Hojicha") {
    $p = Read-Palette $blocks[$theme]
    if ($theme -eq "Hojicha") { $layered = Read-Palette $blocks["Dark"]; foreach ($k in $p.Keys) { $layered[$k] = $p[$k] }; $p = $layered }
    $surface = $p["IslandSubCardBackgroundBrush"]; if (-not $surface) { throw "$theme card surface not found" }
    foreach ($name in $textBrushes) {
        if (-not $p.ContainsKey($name)) { $failures += "$theme $name is not defined"; continue }
        $ratio = Get-Contrast $p[$name] $surface
        if ($ratio -lt 4.5) { $failures += ("{0} {1} {2} on card {3} = {4:N2} (< 4.5)" -f $theme, $name, $p[$name], $surface, $ratio) }
    }
    # Filled buttons: ink must be readable on the accent fill and on the WhatsApp fill.
    foreach ($pair in @(@("IslandAccentButtonInkBrush", "IslandAccentFillBrush"), @("IslandWhatsAppInkBrush", "IslandWhatsAppBrush"))) {
        if ($p.ContainsKey($pair[0]) -and $p.ContainsKey($pair[1])) {
            $ratio = Get-Contrast $p[$pair[0]] $p[$pair[1]]
            if ($ratio -lt 4.5) { $failures += ("{0} {1} on {2} = {3:N2} (< 4.5)" -f $theme, $pair[0], $pair[1], $ratio) }
        }
    }
    # Panel edges only need to be perceptible (informational floor of 1.3 against the card surface).
    $edge = Get-Contrast $p["IslandSubCardBorderBrush"] $surface
    if ($edge -lt 1.3) { $failures += ("{0} sub-card border vs card = {1:N2} (< 1.3)" -f $theme, $edge) }
    Write-Output ("{0}: checked {1} text brushes, card edge {2:N2}:1" -f $theme, $textBrushes.Count, $edge)
}
if ($failures.Count) { $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }; throw "Theme contrast check failed ($($failures.Count))" }
Write-Output "Theme contrast checks passed."
