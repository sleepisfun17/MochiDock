param([switch]$Elevated, [string]$LogPath, [switch]$Installer)
$ErrorActionPreference = 'Stop'
# Repository root is the parent of this script folder, so the script works from any working directory.
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $LogPath) {
    $logDirectory = Join-Path $repoRoot 'MochiDock\obj\publish-logs'
    $null = New-Item -ItemType Directory -Path $logDirectory -Force
    $LogPath = Join-Path $logDirectory ("publish-{0}-{1}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($LogPath, '', [Text.UTF8Encoding]::new($false))
}
function Write-PublishMessage([string]$Message) {
    $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    [IO.File]::AppendAllText($LogPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

# Keep the original console visible while the elevated worker writes its log.
# ReadLine at EOF can be retried as the worker appends more output.
# Installer mode writes to Publish-Installer, never touches a running copy and needs no elevation.
if (-not $Installer -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $reader = $null
    try {
        Write-Host "Publish log: $LogPath"
        Write-Host 'Requesting Administrator access. Accept the Windows prompt to continue...'
        $helper = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated -LogPath `"$LogPath`""
        $stream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        while (-not $helper.HasExited) {
            while ($null -ne ($line = $reader.ReadLine())) { Write-Host $line }
            Start-Sleep -Milliseconds 200
            $helper.Refresh()
        }
        while ($null -ne ($line = $reader.ReadLine())) { Write-Host $line }
        $helper.WaitForExit()
        $code = $helper.ExitCode
    } catch {
        Write-Host "Could not start or monitor the publisher: $($_.Exception.Message)" -ForegroundColor Red
        $code = 1
    } finally {
        if ($reader) { $reader.Dispose() }
    }
    exit $code
}

$stage = 'initialization'
try {
    $repo = $repoRoot
    $destination = Join-Path $repo $(if ($Installer) { 'Publish-Installer' } else { 'Publish' })
    $executable = Join-Path $destination 'MochiDock.exe'
    Write-PublishMessage "Publish log: $LogPath"
    Write-PublishMessage "Destination: $destination"
    $stage = 'checking prerequisites'
    Write-PublishMessage '[1/5] Checking the .NET SDK and project...'
    $null = Get-Command dotnet -ErrorAction Stop
    if (-not (Test-Path -LiteralPath (Join-Path $repo 'MochiDock\MochiDock.csproj'))) { throw 'Application project not found.' }
    $stage = 'stopping the destination application'
    Write-PublishMessage '[2/5] Checking for a running copy in the Publish folder...'
    $matchingInstances = 0
    foreach ($instance in @(if ($Installer) { @() } else { Get-Process -Name MochiDock -ErrorAction SilentlyContinue })) {
        if ($instance.Path -ne $executable) { continue }
        $matchingInstances++
        Write-PublishMessage "Requesting normal exit from MochiDock PID $($instance.Id); waiting up to 10 seconds..."
        $null = $instance.CloseMainWindow()
        if (-not $instance.WaitForExit(10000)) {
            $instance.Refresh()
            if (-not $instance.HasExited -and $instance.Path -eq $executable) {
                Write-PublishMessage "WARNING: Normal exit timed out; forcibly stopping PID $($instance.Id)."
                $instance.Kill()
                if (-not $instance.WaitForExit(10000)) { throw "MochiDock did not stop after forced termination." }
            }
        }
    }
    if ($Installer) { Write-PublishMessage 'Installer mode: skipped (output goes to Publish-Installer).' }
    elseif ($matchingInstances -eq 0) { Write-PublishMessage 'No running destination copy found.' }
    $stage = 'Release build and publish'
    Write-PublishMessage '[3/5] Restoring packages, compiling Release and publishing files...'
    Write-PublishMessage 'Build output follows. Package restore can take time; this stage has no percentage estimate.'
    # Native stderr can be represented as ErrorRecord in Windows PowerShell.
    # Capture it as text; the native exit code remains the authoritative result.
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $publishArgs = @('-c','Release','-o',$destination,'--source','https://api.nuget.org/v3/index.json','--verbosity','minimal','--nologo')
        if ($Installer) { $publishArgs += @('-r','win-x64','--self-contained','true') }
        & dotnet publish (Join-Path $repo 'MochiDock\MochiDock.csproj') @publishArgs 2>&1 | ForEach-Object { Write-PublishMessage $_.ToString() }
        if ($LASTEXITCODE -eq 0 -and $Installer) {
            # The app project skips its framework-dependent helper copy when self-contained; publish the helper with the same runtime.
            Write-PublishMessage 'Publishing the self-contained Outlook helper...'
            & dotnet publish (Join-Path $repo 'MochiDock.OutlookHelper\MochiDock.OutlookHelper.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $destination 'OutlookHelper') --source https://api.nuget.org/v3/index.json --verbosity minimal --nologo 2>&1 | ForEach-Object { Write-PublishMessage $_.ToString() }
        }
        $publishCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousErrorPreference }
    if ($publishCode -ne 0) { throw "Publish failed with exit code $publishCode." }
    # The shared GOWA server's webhook relay (tools\gowa-relay) is published next to its installer, which copies it to the server PC.
    $stage = 'publishing the GOWA webhook relay'
    Write-PublishMessage 'Publishing the GOWA webhook relay (tools\gowa-relay\publish)...'
    & (Join-Path $repo 'tools\gowa-relay\publish.ps1') 2>&1 | ForEach-Object { Write-PublishMessage $_.ToString() }
    if ($LASTEXITCODE -ne 0) { throw "Relay publish failed with exit code $LASTEXITCODE." }
    $stage = 'validating published assets'
    Write-PublishMessage '[4/5] Validating required files and runtime configuration...'
    $required = @('MochiDock.exe','MochiDock.dll','MochiDock.deps.json','MochiDock.runtimeconfig.json','icon.ico','GOWA\windows-amd64.exe','GOWA\readme.md','install-startup.ps1','uninstall-startup.ps1','OutlookHelper\MochiDock.OutlookHelper.exe','OutlookHelper\MochiDock.OutlookHelper.dll','OutlookHelper\MochiDock.OutlookHelper.runtimeconfig.json')
    $required += @(Get-ChildItem -LiteralPath (Join-Path $repo 'MochiDock\Assets\Icons') -Filter '*.png' | ForEach-Object { 'Assets\Icons\' + $_.Name })
    foreach ($relative in $required) {
        $file = Get-Item -LiteralPath (Join-Path $destination $relative)
        if ($file.Length -eq 0) { throw "Empty published file: $relative" }
    }
    $null = Get-Content -LiteralPath (Join-Path $destination 'MochiDock.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($Installer) {
        foreach ($relative in @('hostfxr.dll','coreclr.dll','OutlookHelper\hostfxr.dll','OutlookHelper\coreclr.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $destination $relative))) { throw "Self-contained runtime file missing: $relative" }
        }
        foreach ($relative in @('MochiDock.runtimeconfig.json','OutlookHelper\MochiDock.OutlookHelper.runtimeconfig.json')) {
            $runtime = (Get-Content -LiteralPath (Join-Path $destination $relative) -Raw | ConvertFrom-Json).runtimeOptions
            if ($runtime.framework -or $runtime.frameworks) { throw "$relative still references a shared framework." }
        }
        Write-PublishMessage 'Confirmed the app and helper are self-contained (no shared framework reference).'
    }
    Write-PublishMessage "Validated $($required.Count) required files and runtime configuration."
    $stage = 'updating the Desktop shortcut'
    if ($Installer) {
        Write-PublishMessage '[5/5] Installer mode: Desktop shortcut not created.'
        Write-PublishMessage "SUCCESS: Self-contained installer payload in $destination"
        exit 0
    }
    Write-PublishMessage '[5/5] Updating the Desktop shortcut...'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'MochiDock.lnk'))
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $destination
    $shortcut.IconLocation = Join-Path $destination 'icon.ico'
    $shortcut.Save()
    Write-PublishMessage "SUCCESS: Published $executable"
    Write-PublishMessage 'Application remains closed. You can launch MochiDock manually.'
    exit 0
} catch {
    Write-PublishMessage "FAILED during ${stage}: $($_.Exception.Message)"
    Write-PublishMessage "Review the full output in $LogPath"
    exit 1
}
