$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated shell for your own administrator account.' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$name = "MochiDock-Startup-$sid"
try { $null = $folder.GetTask($name); $folder.DeleteTask($name,0) }
catch {
    $failure = $_.Exception
    while ($failure.InnerException) { $failure = $failure.InnerException }
    if ($failure.HResult -ne -2147024894) { throw }
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name MochiDock -ErrorAction SilentlyContinue
$ws = New-Object -ComObject WScript.Shell
# AuraIsland.lnk / AuraIsland.exe are legacy pre-rename names that may still exist on users' PCs.
foreach ($shortcutName in @('AuraIsland.lnk','MochiDock.lnk')) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) $shortcutName
    if (Test-Path -LiteralPath $shortcutPath) {
        $target = $ws.CreateShortcut($shortcutPath).TargetPath
        if ([IO.Path]::GetFileName($target) -in @('AuraIsland.exe','MochiDock.exe')) { Remove-Item -LiteralPath $shortcutPath }
    }
}
Write-Host 'MochiDock startup disabled. The running application was not stopped.'
