param([string]$ExecutablePath = '')
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated shell for your own administrator account.' }
if (-not $ExecutablePath) {
    $ExecutablePath = Join-Path $PSScriptRoot 'MochiDock.exe'
    if (-not (Test-Path -LiteralPath $ExecutablePath)) { $ExecutablePath = Join-Path $PSScriptRoot 'Publish\MochiDock.exe' }
}
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($ExecutablePath) -ne 'MochiDock.exe') { throw 'Expected MochiDock.exe.' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$definition = $scheduler.NewTask(0)
$definition.RegistrationInfo.Description = 'Start MochiDock for this user at logon.'
$definition.Principal.UserId = $sid
$definition.Principal.LogonType = 3
$definition.Principal.RunLevel = 1
$definition.Settings.Enabled = $true
$definition.Settings.DisallowStartIfOnBatteries = $false
$definition.Settings.StopIfGoingOnBatteries = $false
$definition.Settings.ExecutionTimeLimit = 'PT0S'
$definition.Settings.MultipleInstances = 2
$trigger = $definition.Triggers.Create(9)
$trigger.UserId = $sid
$trigger.Delay = 'PT10S'
$action = $definition.Actions.Create(0)
$action.Path = $ExecutablePath
$action.WorkingDirectory = Split-Path -LiteralPath $ExecutablePath
$name = "MochiDock-Startup-$sid"
$null = $folder.RegisterTaskDefinition($name,$definition,6,$sid,$null,3)
$registered = $folder.GetTask($name)
if (-not $registered.Enabled -or $registered.Definition.Actions.Item(1).Path -ne $ExecutablePath) { throw 'Startup verification failed.' }
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
Write-Host "Registered $name for $ExecutablePath. The application was not started."
