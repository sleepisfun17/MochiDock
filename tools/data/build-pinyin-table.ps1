<#
.SYNOPSIS
  Builds MochiDock/Assets/zh_pinyin.tsv.gz from the Unicode Unihan database.

.DESCRIPTION
  Reads the kMandarin field of Unihan_Readings.txt (from https://www.unicode.org/Public/<version>/ucd/Unihan.zip) and writes one
  "<character><TAB><pinyin with tone marks>" line per Basic Multilingual Plane character, gzip-compressed. kMandarin lists the most
  customary reading first; only that first reading is kept. Unihan is distributed under the Unicode License v3
  (https://www.unicode.org/license.txt); THIRD-PARTY-NOTICES.txt carries the notice.

.EXAMPLE
  ./tools/data/build-pinyin-table.ps1 -UnihanReadings C:\temp\unihan\Unihan_Readings.txt
#>
param(
    [Parameter(Mandatory)] [string] $UnihanReadings,
    [string] $Output = (Join-Path $PSScriptRoot '..\..\MochiDock\Assets\zh_pinyin.tsv.gz')
)

$ErrorActionPreference = 'Stop'
$builder = [System.Text.StringBuilder]::new()
$count = 0
foreach ($line in [System.IO.File]::ReadLines((Resolve-Path $UnihanReadings))) {
    if (-not $line.StartsWith('U+')) { continue }
    $fields = $line.Split("`t")
    if ($fields.Length -lt 3 -or $fields[1] -ne 'kMandarin') { continue }
    $code = [Convert]::ToInt32($fields[0].Substring(2), 16)
    if ($code -gt 0xFFFF) { continue }   # the romanizer works on UTF-16 chars; supplementary planes are rare in lyrics
    $reading = $fields[2].Split(' ')[0]
    [void]$builder.Append([char]$code).Append("`t").Append($reading).Append("`n")
    $count++
}

$bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($builder.ToString())
$file = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Output, (Get-Location).Path))
try {
    $gzip = [System.IO.Compression.GZipStream]::new($file, [System.IO.Compression.CompressionLevel]::SmallestSize)
    try { $gzip.Write($bytes, 0, $bytes.Length) } finally { $gzip.Dispose() }
} finally { $file.Dispose() }
Write-Host "Wrote $count readings to $Output"
