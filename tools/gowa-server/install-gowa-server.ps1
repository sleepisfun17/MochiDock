<#
.SYNOPSIS
  Runs ONE shared GOWA (WhatsApp) server on this PC so every MochiDock listens to it.

.DESCRIPTION
  Copies GOWA\windows-amd64.exe to a renamed gowa-server.exe (so MochiDock's local mode never touches it),
  registers a Task Scheduler task that starts it at boot and restarts it if it stops, and opens the port
  only for private networks (LAN and Tailscale). WhatsApp data (the linked-device session) lives in
  -DataDir and survives restarts. Never forward this port to the internet.

  Read receipts reach the docks through a small loopback relay (MochiDock-GOWA-Relay, tools\gowa-relay): GOWA is given ONE webhook address,
  127.0.0.1:47830, which always answers at once, and the relay forwards each receipt to every dock in parallel (3 s timeout, one retry).
  GOWA itself delivers to several addresses one after another and retries a dead one for a long time, so an offline dock used to hold up
  or lose the receipt for the others. The docks are listed in relay-targets.txt next to the server; -AddWebhookUrl / -RemoveWebhookUrl edit
  that list without reinstalling anything (the relay re-reads it by itself).

  Run from an elevated PowerShell. Nothing here starts MochiDock.

.EXAMPLE
  .\install-gowa-server.ps1 -User gowa -Password (Read-Host -AsSecureString "Server password")

.EXAMPLE
  .\install-gowa-server.ps1 -AddWebhookUrl "http://100.x.x.x:47823/webhook?token=..."

.EXAMPLE
  .\install-gowa-server.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$SourceExe = (Join-Path $PSScriptRoot '..\..\GOWA\windows-amd64.exe'),
    [string]$InstallDir = 'C:\ProgramData\MochiDockGowaServer',
    [int]$Port = 3099,
    [string]$User,
    [SecureString]$Password,
    [string[]]$WebhookUrls = @(),
    # The webhook relay (build it with tools\gowa-relay\publish.ps1 or scripts\build_and_publish.bat).
    [string]$RelayExe = (Join-Path $PSScriptRoot '..\gowa-relay\publish\MochiDock.GowaRelay.exe'),
    [int]$RelayPort = 47830,
    # Add or remove one dock's Read sync address in relay-targets.txt only (no reinstall, no restart).
    [string[]]$AddWebhookUrl = @(),
    [string[]]$RemoveWebhookUrl = @(),
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$taskName = 'MochiDock-GOWA-Server'
$ruleName = 'MochiDock GOWA Server'
$relayTaskName = 'MochiDock-GOWA-Relay'
$targetsFile = Join-Path $InstallDir 'relay-targets.txt'
$relayLog = Join-Path $InstallDir 'relay.log'
$targetPattern = '^http://[^\s",]+$'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated (Administrator) PowerShell.'
}

# Targets only: edit relay-targets.txt and stop. The relay notices the change by itself.
if ($AddWebhookUrl.Count -gt 0 -or $RemoveWebhookUrl.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $lines = @(if (Test-Path $targetsFile) { Get-Content -LiteralPath $targetsFile })
    foreach ($u in $RemoveWebhookUrl) {
        $lines = @($lines | Where-Object { $t = $_.Trim(); -not ($t -ieq $u.Trim() -or ($u -notmatch '\?' -and $t -like ($u.Trim() + '?*'))) })
    }
    foreach ($u in $AddWebhookUrl) {
        if ($u -notmatch $targetPattern) { throw "Invalid webhook URL: $u" }
        if (-not ($lines | Where-Object { $_.Trim() -ieq $u.Trim() })) { $lines += $u.Trim() }
    }
    Set-Content -LiteralPath $targetsFile -Value $lines
    $count = @($lines | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') }).Count
    Write-Host "relay-targets.txt now lists $count dock address(es). The relay picks the change up by itself; nothing was restarted."
    return
}

if ($Uninstall) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Stop-ScheduledTask -TaskName $relayTaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $relayTaskName -Confirm:$false -ErrorAction SilentlyContinue
    Get-Process -Name 'gowa-relay' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    Write-Host "Removed both tasks (server and relay) and the firewall rule. Your WhatsApp data in $InstallDir was kept; delete that folder yourself to unlink."
    return
}

if (-not $User -or -not $Password) {
    throw 'Pass -User and -Password. A WhatsApp server without a login lets anyone who can reach the port read your messages.'
}
if (-not (Test-Path $SourceExe)) { throw "GOWA binary not found: $SourceExe" }
if (-not (Test-Path $RelayExe)) { throw "Webhook relay not found: $RelayExe. Build it first with tools\gowa-relay\publish.ps1 (scripts\build_and_publish.bat does it too), or pass -RelayExe." }
foreach ($u in $WebhookUrls) { if ($u -notmatch $targetPattern) { throw "Invalid webhook URL: $u" } }

$plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
if ($plain.Length -lt 8) { throw 'Use a password of at least 8 characters.' }
if ($plain -match '[:,\s]') { throw 'The password may not contain colons, commas or spaces (GOWA --basic-auth syntax).' }
if ($User -match '[:,\s]') { throw 'The user name may not contain colons, commas or spaces.' }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$dataDir = Join-Path $InstallDir 'data'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$exe = Join-Path $InstallDir 'gowa-server.exe'

Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
# Stopping the task can leave the server process running (or it was started by hand), and Windows will not overwrite a running exe:
# end every gowa-server.exe that runs from the install folder and wait until the file is free.
Get-Process -Name 'gowa-server' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $exe) } | Stop-Process -Force -ErrorAction SilentlyContinue
for ($i = 0; $i -lt 20; $i++) {
    try { Copy-Item -LiteralPath $SourceExe -Destination $exe -Force -ErrorAction Stop; break }
    catch { if ($i -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
}

# Relay: stop it, replace the exe (Windows will not overwrite a running exe), write the dock list and register its startup task.
$relayInstalled = Join-Path $InstallDir 'gowa-relay.exe'
Stop-ScheduledTask -TaskName $relayTaskName -ErrorAction SilentlyContinue
Get-Process -Name 'gowa-relay' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $relayInstalled) } | Stop-Process -Force -ErrorAction SilentlyContinue
for ($i = 0; $i -lt 20; $i++) {
    try { Copy-Item -LiteralPath $RelayExe -Destination $relayInstalled -Force -ErrorAction Stop; break }
    catch { if ($i -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
}
if ($WebhookUrls.Count -gt 0) { Set-Content -LiteralPath $targetsFile -Value $WebhookUrls }
elseif (-not (Test-Path $targetsFile)) { Set-Content -LiteralPath $targetsFile -Value '# One dock Read sync address per line (Settings > WhatsApp > Read sync). Add or remove with -AddWebhookUrl / -RemoveWebhookUrl.' }
$relayAction = New-ScheduledTaskAction -Execute $relayInstalled -Argument "--port $RelayPort --targets `"$targetsFile`" --log `"$relayLog`"" -WorkingDirectory $InstallDir
$relaySettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 99 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
Register-ScheduledTask -TaskName $relayTaskName -Action $relayAction -Trigger (New-ScheduledTaskTrigger -AtStartup) -Settings $relaySettings `
    -Principal (New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest) -Force | Out-Null
Start-ScheduledTask -TaskName $relayTaskName

$arguments = "rest --host=0.0.0.0 --port=$Port --basic-auth=${User}:${plain} --auto-download-media=false"
# Read sync: GOWA gets ONE webhook address, the loopback relay, which fans each receipt out to every dock in parallel.
# Giving GOWA all the docks directly made it deliver to them one after another with long retries, so an offline dock delayed or lost the receipt for the others.
$arguments += " --webhook=`"http://127.0.0.1:$RelayPort/webhook`" --webhook-events=`"message.ack`""
$action = New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $dataDir
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 99 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
$runAs = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $runAs -Force | Out-Null

# Only private networks may reach it: LAN ranges and Tailscale (100.64.0.0/10).
Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow `
    -Profile Any -RemoteAddress '10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', '100.64.0.0/10' | Out-Null

Start-ScheduledTask -TaskName $taskName
Write-Host ''
Write-Host "GOWA server started on port $Port (data: $dataDir)."
Write-Host "1. On this PC open http://localhost:$Port, log in with the user above, and scan the WhatsApp QR once (WhatsApp > Linked devices)."
Write-Host "2. On every other PC: MochiDock > Settings > WhatsApp > Remote Server: http://<this PC's LAN or Tailscale IP>:$Port, same user and password."
Write-Host "3. Read sync: give the relay each PC's Read sync address (MochiDock > Settings > WhatsApp > Read sync): -AddWebhookUrl ""<address>"" (no restart needed). Now listed: $(if (Test-Path $targetsFile) { @(Get-Content $targetsFile | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') }).Count } else { 0 }). Log: $relayLog"
Write-Host 'Note: the task arguments contain the password; only administrators of this PC can read them.'
