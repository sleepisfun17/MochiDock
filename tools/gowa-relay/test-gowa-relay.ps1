# Loopback fan-out test of the GOWA webhook relay without GOWA or MochiDock: two fake targets (one alive, one dead), one event.
# Checks that the relay answers GOWA at once, the live target gets the payload quickly although another target is down, the dead one is
# logged as failed and then dropped, and no token appears in the relay log.
param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe=Join-Path $repo "tools/gowa-relay/bin/$Configuration/net8.0/MochiDock.GowaRelay.exe"
if(-not (Test-Path $exe)){ & dotnet build (Join-Path $repo 'tools/gowa-relay/MochiDock.GowaRelay.csproj') -c $Configuration --nologo -v q | Out-Null }
$work=Join-Path ([IO.Path]::GetTempPath()) ('relay-test-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $work|Out-Null
$relayPort=47931;$livePort=47932;$deadPort=47933
$targets=Join-Path $work 'relay-targets.txt';$log=Join-Path $work 'relay.log'
"http://127.0.0.1:$deadPort/webhook?token=SECRETDEAD`nhttp://127.0.0.1:$livePort/webhook?token=SECRETLIVE" | Set-Content $targets
$live=[Net.HttpListener]::new();$live.Prefixes.Add("http://127.0.0.1:$livePort/");$live.Start()
$received=$null;$task=$live.GetContextAsync()
$relay=Start-Process $exe -ArgumentList @('--port',$relayPort,'--targets',"`"$targets`"",'--log',"`"$log`"") -PassThru -WindowStyle Hidden
try{
  for($i=0;$i -lt 50;$i++){ try{ $null=Invoke-WebRequest "http://127.0.0.1:$relayPort/health" -UseBasicParsing -TimeoutSec 1; break }catch{ Start-Sleep -Milliseconds 100 } }
  $payload='{"event":"message.ack","payload":{"receipt_type":"read-self","chat_id":"1@s.whatsapp.net"}}'
  $clock=[Diagnostics.Stopwatch]::StartNew()
  $reply=Invoke-WebRequest "http://127.0.0.1:$relayPort/webhook" -Method Post -Body $payload -ContentType 'application/json' -Headers @{'X-Test'='1'} -UseBasicParsing
  $replyMs=$clock.ElapsedMilliseconds
  if($reply.StatusCode -ne 200){ throw "relay answered $($reply.StatusCode)" }
  if(-not $task.Wait(3000)){ throw 'the live target did not receive the event within 3 s' }
  $ctx=$task.Result;$arrivedMs=$clock.ElapsedMilliseconds
  $body=(New-Object IO.StreamReader($ctx.Request.InputStream)).ReadToEnd();$ctx.Response.StatusCode=200;$ctx.Response.Close()
  if($body -ne $payload){ throw "payload changed in transit: $body" }
  if($ctx.Request.Headers['X-Test'] -ne '1'){ throw 'a custom header was not forwarded' }
  Start-Sleep -Seconds 10  # the dead target: attempt 1 fails (a refused loopback connect takes about 2 s on Windows), retry after 2 s, attempt 2 fails, dropped
  $text=Get-Content $log -Raw
  if($text -match 'SECRET'){ throw 'a token leaked into the relay log' }
  if($text -notmatch "127.0.0.1:$deadPort -> dropped after 2 attempts"){ throw "dead target was not reported as dropped:`n$text" }
  if($text -notmatch "127.0.0.1:$livePort -> 200"){ throw "live delivery not logged:`n$text" }
  "Relay answered in $replyMs ms; the live target received the event after $arrivedMs ms with the other target down; the dead target was logged as dropped; no token in the log."
} finally { if(-not $relay.HasExited){ Stop-Process -Id $relay.Id -Force }; $live.Close(); Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
