# Reads wireless mouse/keyboard batteries through the production scanner. Does not start MochiDock. Run after a Release build.
param([string]$Configuration='Release',[switch]$Dump,[switch]$Raw)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin 'MochiDock.Core.dll'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'MochiDock.dll'))
if($Dump){
 $hid=$assembly.GetType('MochiDock.Services.DeviceBattery.HidNative')
 foreach($c in $hid.GetMethod('Enumerate').Invoke($null,@())|Sort-Object Vid,Pid,Interface){
  '{0:X4}:{1:X4} MI_{2:X2} page={3:X4} usage={4:X4} in={5} feat={6} valueCaps(in/feat)={7}/{8} product="{9}"' -f $c.Vid,$c.Pid,$c.Interface,$c.UsagePage,$c.Usage,$c.InputLength,$c.FeatureLength,$c.InputValueCaps,$c.FeatureValueCaps,$c.Product
 }
 '--'
}

if($Raw){
 # Sends the read-only 2.4G keyboard status request (FF60:0061, 33-byte frame) to every dongle whose name says 2.4G and prints whatever
 # comes back, byte for byte, so an unsupported reply layout can be studied. Nothing else is sent.
 $hid=$assembly.GetType('MochiDock.Services.DeviceBattery.HidNative')
 $core=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'MochiDock.Core.dll'))
 $request=$core.GetType('MochiDock.Core.SonixProtocol').GetMethod('StatusRequest').Invoke($null,@())
 $any=[Func[byte[],bool]]{param($f) $true}
 foreach($c in $hid.GetMethod('Enumerate').Invoke($null,@())|Where-Object{$_.UsagePage -eq 0xFF60 -and $_.Usage -eq 0x61 -and $_.Product -match '2\.4G'}){
  'RAW {0:X4}:{1:X4} "{2}" request: {3}' -f $c.Vid,$c.Pid,$c.Product,(($request|ForEach-Object{$_.ToString('X2')}) -join ' ')
  foreach($try in 1..3){
   $reply=$hid.GetMethod('ExchangeOutput').Invoke($null,@($c.Path,$request,[int]$c.InputLength,$any,1500))
   if($null -eq $reply){"  try ${try}: no reply within 1.5 s"}else{"  try ${try}: "+(($reply|ForEach-Object{$_.ToString('X2')}) -join ' ')}
  }
 }
 '--'
}
$scanner=$assembly.GetType('MochiDock.Services.DeviceBattery.DeviceBatteryScanner')
$readings=@($scanner.GetMethod('PollOnce',[Type[]]@()).Invoke($null,@()))
foreach($r in $readings){
 $pct=if($null -eq $r.Percent){'?'}else{"$($r.Percent)%"}
 '{0,-9} {1,-28} {2,5} charging={3} connected={4} via={5} key={6}' -f $r.Kind,$r.Name,$pct,$r.Charging,$r.Connected,$r.Via,$r.Key
}
"Readings: $($readings.Count)"
