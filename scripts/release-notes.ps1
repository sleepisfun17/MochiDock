# Writes the release notes for one version: its CHANGELOG.md section, update and install notes, and the setup's SHA-256.
# Used by .github/workflows/release.yml; run it locally to preview: pwsh scripts/release-notes.ps1 -Version 0.1.1-beta
param([Parameter(Mandatory)][string]$Version, [string]$Setup)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lines = Get-Content (Join-Path $repo 'CHANGELOG.md')
$start = [Array]::FindIndex($lines, [Predicate[string]]{ param($l) $l -eq "## [$Version]" })
if ($start -lt 0) { throw "CHANGELOG.md has no '## [$Version]' section." }
$end = [Array]::FindIndex($lines, $start + 1, [Predicate[string]]{ param($l) $l -like '## *' })
if ($end -lt 0) { $end = $lines.Count }
$section = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
$name = "MochiDock-Setup-$Version.exe"
$out = @(
    $section
    ''
    "**Updating:** if MochiDock was installed with the setup, **Update now** (Settings > Data & diagnostics, or the tray) installs this release and restarts MochiDock, keeping your settings. Otherwise run ``$name``."
    ''
    '**Unsigned beta.** Windows SmartScreen may warn on first run: choose "More info", then "Run anyway". Requires Windows 10 version 2004 or later, or Windows 11, 64-bit; no .NET runtime needed.'
    ''
    'See the [changelog](https://github.com/sleepisfun17/MochiDock/blob/main/CHANGELOG.md) for earlier releases and known limitations.'
)
if ($Setup) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Setup).Hash.ToLowerInvariant()
    $out += @('', "SHA-256 of ``$name``: ``$hash``")
}
$out -join "`n"
