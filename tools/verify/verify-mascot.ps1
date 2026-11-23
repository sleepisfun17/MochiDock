param([string]$Configuration='Release')
# Renders the real MascotControl in every mood at four scales and checks that only that mood's layers are visible (no face leaks into
# the next), that the body colours return to normal for plain moods, and that the extras stay close to the 30 x 26 body. Writes a PNG
# strip of every face on a black and a cream background to MochiDock/obj/mascot for a human look. Never starts MochiDock.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$directory=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.Core.dll'))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock')){return $assembly} if($e.Name.StartsWith('MochiDock.Core')){return $core} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
$moodType=$core.GetType('MochiDock.Core.MascotMood')
$assembly.GetType('MochiDock.Controls.MascotControl').GetProperty('SpinEnabled').SetValue($null,$false)
$output=Join-Path $repo 'MochiDock/obj/mascot';$null=New-Item -ItemType Directory -Force $output

$extras=@('LeftDetermEye','RightDetermEye','LeftFlatEye','RightFlatEye','LeftDroopEye','RightDroopEye','LeftSpiralEye','RightSpiralEye','LeftWideEye','RightWideEye','LeftRoundEye','RightRoundEye','LeftLidEye','RightLidEye','SteamLeft','SteamRight','IcicleGroup','StarsGroup','TearDrop','TearDropRight','StartleLines','ConfettiGroup','StarEyes','CoinEyes','Sunglasses','BoltOrbit','RainGroup','ScarfGroup','PartyHat','MicBoom')
# Mochi has no mouth and no eyebrows: none of these layers may exist (they made the face busy).
$banned=@('BrowLeft','BrowRight','MouthFrown','MouthPant','MouthChatter','MouthWave','MouthFlat','MouthGrin','MouthYawn','MouthSmile')
$plain=@('Idle','Waiting','Bored','Music','Alert','Sleeping','Happy')
# What must be visible, besides the pill eyes, in each extra mood.
$expected=@{
 Angry=@('LeftDetermEye','RightDetermEye');
 Hot=@('LeftFlatEye','RightFlatEye','SteamLeft','SteamRight');
 Freezing=@('IcicleGroup');
 Dizzy=@('LeftSpiralEye','RightSpiralEye','StarsGroup');
 Drained=@('LeftDroopEye','RightDroopEye');
 Focused=@('LeftDetermEye','RightDetermEye');
 Victory=@();
 Defeat=@('LeftDroopEye','RightDroopEye','TearDrop','TearDropRight');
 Worried=@('LeftDroopEye','RightDroopEye');
 BreakTime=@();
 Excited=@('LeftWideEye','RightWideEye');
 Surprised=@('LeftRoundEye','RightRoundEye','StartleLines');
 Cheering=@('ConfettiGroup');
 Praying=@();
 Bored=@('LeftLidEye','RightLidEye');
 Waking=@('LeftLidEye','RightLidEye');
 Electric=@('StarEyes','BoltOrbit');
 Money=@('CoinEyes');
 MoneyDown=@('CoinEyes');
 Sunny=@('Sunglasses');
 Rainy=@('RainGroup');
 Snowy=@('ScarfGroup');
 Cloudy=@();
 Studying=@('LeftLidEye','RightLidEye');
 Coffee=@();
 Yawning=@('LeftLidEye','RightLidEye');
 Birthday=@('PartyHat');
 Meeting=@('MicBoom');
 Proud=@()
}
$pillEyes=@{Angry=$false;Hot=$false;Freezing=$true;Dizzy=$false;Drained=$false;Focused=$false;Victory=$false;Defeat=$false;Worried=$false;BreakTime=$true;Excited=$false;Surprised=$false;Cheering=$false;Praying=$false;Bored=$false;Waking=$false;Electric=$false;Money=$false;MoneyDown=$false;Sunny=$false;Rainy=$true;Snowy=$true;Cloudy=$true;Studying=$false;Coffee=$true;Yawning=$false;Birthday=$false;Meeting=$true;Proud=$false}
$sweat=@{Hot=$true;Worried=$true}
$order=@('Idle','Angry','Hot','Freezing','Dizzy','Drained','Music','Hot','Idle','Dizzy','Alert','Drained','Sleeping','Angry','Happy','Freezing','Focused','Waiting','Angry','Bored','Victory','Defeat','Angry','Victory','Idle','Defeat','Focused','Worried','BreakTime','Worried','Angry','BreakTime','Focused','Music','Worried','Music','Excited','Surprised','Cheering','Praying','Angry','Surprised','Idle','Praying','Idle','Bored','Waking','Bored','Angry','Waking','Idle','Electric','Money','Electric','MoneyDown','Sunny','Rainy','Snowy','Cloudy','Studying','Electric','Idle','Rainy','Dizzy','Sunny','Music','Studying','Focused','Money','Idle','Coffee','Bored','Coffee','Sleeping','Coffee','Electric','Coffee','Idle','Yawning','Birthday','Idle','Meeting','Proud','Coffee','Meeting','Music','Yawning','Proud','Birthday','Angry','Idle')

function Visible($mascot,$name){$e=$mascot.FindName($name);if($null -eq $e){throw "Layer $name not found"};return $e.Visibility -eq [Windows.Visibility]::Visible}
function New-Host($mascot,$scale,$background){
 $surface=[Windows.Controls.Border]::new();$surface.Padding=[Windows.Thickness]::new(10);$surface.Background=$background;$surface.Child=$mascot
 $parameters=[Windows.Interop.HwndSourceParameters]::new('Mascot');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$surface
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 return @($native,$surface)
}
$checks=0
# Freezes a Mochi part-way through a turn with the wind on (headless renders do not run animations).
function Show-Turn($m,$angle){
 $m.GetType().GetMethod('StartWind',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($m,@())|Out-Null
 foreach($n in 'WindBack','WindFront','DustPuffs'){$e=$m.FindName($n);$e.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$e.Opacity=1}
 $m.SetValue($m.GetType().GetField('SpinAngleProperty',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null),[double]$angle)
}
# The wind must stay inside the capsule's room around the body like the other extras (2.5 beside, 9.5 above, 2 below).
$probeWind=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'))
$probeWind.Width=30;$probeWind.Height=26;foreach($n in 'WindBack','WindFront'){$probeWind.FindName($n).Visibility=[Windows.Visibility]::Visible};$probeWind.Measure([Windows.Size]::new(60,60));$probeWind.Arrange([Windows.Rect]::new(0,0,30,26));$probeWind.UpdateLayout()
function Wind-Paths($element){
 if($element -is [Windows.Shapes.Path]){return ,$element}
 $found=@();if($element -is [Windows.Controls.Panel]){foreach($child in $element.Children){$found+=Wind-Paths $child}};return $found
}
$strokes=0;$spinAngle=$probeWind.GetType().GetField('SpinAngleProperty',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
foreach($canvas in 'WindBack','WindFront'){
 $root=$probeWind.FindName($canvas)
 foreach($path in (Wind-Paths $root)){
  if($path.Name -like 'Dust*'){continue}
  # Every point of the spin: the strokes are redrawn as they orbit. The wind rises 1.5 at most, which is the -1.5 on top.
  foreach($spin in 0,90,180,270,360,450,540,630,720,810,900,990,1080,1170,1260,1350,1440){
   $probeWind.SetValue($spinAngle,[double]$spin)
   if($null -eq $path.Data){continue}
   $shape=$path.Data.Clone();$shape.Transform=[Windows.Media.Transform]$path.TransformToAncestor($root);$b=$shape.Bounds
   if($b.X -lt -2.5 -or $b.Right -gt 32.5 -or ($b.Y-1.5) -lt -9.5 -or ($b.Bottom+0.8) -gt 28){throw "A wind stroke sticks out of the capsule room at orbit $spin`: $b"}
  }
  $strokes++
 }
}
if($strokes -lt 20){throw "The whirlwind has only $strokes strokes; expected the three rings (front and back), wisps and the curl"}
$checks++
# The click heart must be a vector shape: WPF text cannot draw colour emoji, so an emoji would be black and white.
$probe=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'))
foreach($gone in $banned){if($null -ne $probe.FindName($gone)){throw "$gone exists again: Mochi has no mouth and no eyebrows"}}
# The ear cups must stay inside the 30 unit body width: the capsule cuts everything wider, which showed as a cropped left cup.
foreach($cup in 'EarCupLeft','EarCupRight'){$e=$probe.FindName($cup);$left=[Windows.Controls.Canvas]::GetLeft($e);if($left -lt 0 -or ($left+$e.Width) -gt 30){throw "$cup sticks out of the 30 unit body ($left + $($e.Width))"};$checks++}
foreach($name in 'FloatingHeart'){
 $element=$probe.FindName($name)
 if($null -eq $element){throw "$name not found"}
 if($element -is [Windows.Controls.TextBlock]){throw "$name is text again; it must be a vector shape (colour emoji render black and white in WPF)"}
}
# A click on a dizzy Mochi is reported (the view model counts it) but only wobbles him: no heart, no purr, no happy squint.
$mascotType=$assembly.GetType('MochiDock.Controls.MascotControl')
$script:petted=0
$handler=[Action]{$script:petted++}
$mascotType.GetEvent('AnyMochiPetted').AddEventHandler($null,$handler)
try{
 $dizzy=[Activator]::CreateInstance($mascotType);$dizzy.Mood=[Enum]::Parse($moodType,'Dizzy')
 $dizzy.PetMochi()
 if($script:petted -ne 1){throw "A click was not reported to the view model (reported $($script:petted))"}
 if($dizzy.FindName('FloatingHeart').Opacity -ne 0 -or $dizzy.FindName('FloatingHeart').HasAnimatedProperties){throw 'A dizzy Mochi showed the petting heart'}
 if($dizzy.FindName('LeftHappyEye').Visibility -eq [Windows.Visibility]::Visible){throw 'A dizzy Mochi squinted happily when petted'}
 if($dizzy.FindName('LeftSpiralEye').Visibility -ne [Windows.Visibility]::Visible){throw 'A click changed the dizzy face'}
} finally { $mascotType.GetEvent('AnyMochiPetted').RemoveEventHandler($null,$handler) }
$checks++
$animations=$mascotType.GetField('_animationsOverride',[Reflection.BindingFlags]'NonPublic,Static')
if($true){
 $mascotType.GetProperty('SpinEnabled').SetValue($null,$true);$animations.SetValue($null,[Nullable[bool]]$true)
 $turner=[Activator]::CreateInstance($mascotType);$turner.Width=30;$turner.Height=26
 $native,$surface=New-Host $turner 1 ([Windows.Media.Brushes]::Black)
 try{
  $surface.Measure([Windows.Size]::new(200,200));$surface.Arrange([Windows.Rect]::new(0,0,60,60));$surface.UpdateLayout()
  $frame=[Windows.Threading.DispatcherFrame]::new();[void][Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke('Background',[Action]{$frame.Continue=$false});[Windows.Threading.Dispatcher]::PushFrame($frame)
  if(!$turner.IsLoaded -or !$turner.IsVisible){throw 'Spin check: the test Mochi is not loaded and visible'}
  $flip=$turner.FindName('FlipScaleTransform')
  $turner.Mood=[Enum]::Parse($moodType,'Idle');$turner.Mood=[Enum]::Parse($moodType,'Waiting')
  if((Visible $turner 'WindFront')){throw 'The idle blink (Idle to Waiting) turned Mochi'}
  $turner.Mood=[Enum]::Parse($moodType,'Electric')
  if(!(Visible $turner 'WindFront')){throw 'A change of face (Waiting to Electric) did not turn Mochi'}
  if($turner.FindName('BoltOrbit').Visibility -eq [Windows.Visibility]::Visible){throw 'The new face showed before Mochi came round'}
  if(!(Visible $turner 'WindFront') -or !(Visible $turner 'WindBack')){throw 'The turn started without its wind'}
  $narrowest=1.0;$deadline=[DateTime]::Now.AddSeconds(3)
  while([DateTime]::Now -lt $deadline -and ((Visible $turner 'WindFront') -or $turner.FindName('BoltOrbit').Visibility -ne [Windows.Visibility]::Visible)){
   $frame=[Windows.Threading.DispatcherFrame]::new();[void][Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke('Background',[Action]{$frame.Continue=$false});[Windows.Threading.Dispatcher]::PushFrame($frame)
   $narrowest=[Math]::Min($narrowest,$flip.ScaleX);Start-Sleep -Milliseconds 10}
  if($turner.FindName('BoltOrbit').Visibility -ne [Windows.Visibility]::Visible){throw 'The turn never put the Electric face on'}
  if($narrowest -lt 0.79){throw "Mochi went flat during the turn (width $narrowest): he should stay a rounded body"}
  if($narrowest -gt 0.97){throw "The turn never narrowed Mochi (width $narrowest): the 3D turn did not run"}
  if((Visible $turner 'WindFront')){throw 'The wind did not settle after the turn'}
  if($flip.ScaleX -ne 1 -or $turner.FindName('FaceLayer').Opacity -ne 1 -or $turner.FindName('FaceTurnShift').X -ne 0){throw 'Mochi did not come back to rest after the turn'}
  $checks++
 } finally { $native.Dispose();$mascotType.GetProperty('SpinEnabled').SetValue($null,$false);$animations.SetValue($null,$null) }
}
foreach($scale in 1,1.25,1.5,2){
 $mascot=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'));$mascot.Width=30;$mascot.Height=26
 $native,$surface=New-Host $mascot $scale ([Windows.Media.Brushes]::Black)
 try{
  $surface.Measure([Windows.Size]::new(200,200));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout()
  foreach($name in $order){
   $mascot.Mood=[Enum]::Parse($moodType,$name);$surface.UpdateLayout()
   $want=if($expected.ContainsKey($name)){$expected[$name]}elseif($name -eq 'Alert'){@('LeftWideEye','RightWideEye')}else{@()}
   foreach($layer in $extras){
    $shown=Visible $mascot $layer
    if($shown -and $want -notcontains $layer){throw "Layer $layer leaked into mood $name (scale $scale, after a previous mood)"}
    if(!$shown -and $want -contains $layer){throw "Layer $layer is missing in mood $name (scale $scale)"}
   }
   if($expected.ContainsKey($name)){
    if((Visible $mascot 'LeftPillEye') -ne $pillEyes[$name]){throw "Pill eyes wrong in mood $name"}
    if($name -eq 'Focused'){
     $top=$mascot.FindName('BodyGradStopTop').Color.ToString()
     if($top -ne '#FFE3E8F2'){throw "Serious gaming face lost its slate tint ($top)"}
     if(!(Visible $mascot 'HeadphonesLayer')){throw 'The gaming face lost its headset'}
     if(!(Visible $mascot 'HeadsetLedLeft') -or !(Visible $mascot 'HeadsetLedRight')){throw 'The gaming headset lost its red lights'}
    }
    if($name -eq 'Worried' -and (!(Visible $mascot 'HeadphonesLayer') -or !(Visible $mascot 'HeadsetLedLeft'))){throw 'Worried needs the headset with its red lights'}
    if($name -eq 'Meeting' -and ((Visible $mascot 'HeadsetLedLeft') -or !(Visible $mascot 'MicBoom'))){throw 'The meeting headset needs the microphone and no red lights'}
    if($mascot.FindName('FlipScaleTransform').ScaleX -ne 1){throw "Mochi was left turned in mood $name"}
    foreach($part in 'FaceLayer','FaceGrid','LeftCheek','FaceExtras','ExtrasLayer'){if($mascot.FindName($part).Opacity -ne 1){throw "The $part of mood $name stayed hidden (back of a turn)"}}
    if($mascot.FindName('FaceTurnScale').ScaleX -ne 1 -or $mascot.FindName('FaceTurnShift').X -ne 0){throw "The face of mood $name stayed turned"}
    foreach($wind in 'WindBack','WindFront'){if((Visible $mascot $wind)){throw "The turn's wind stayed on in mood $name"}}
    if($name -eq 'Electric' -and $mascot.FindName('BodyGradStopTop').Color.ToString() -eq '#FFEDEDEF' -and !$mascot.FindName('BodyGradStopTop').HasAnimatedProperties){throw 'Electric lost its charged tint'}
    if($name -eq 'Waking' -and !(Visible $mascot 'LeftLidEye')){throw 'Waking is missing its half-lidded eyes'}
    if($name -eq 'Cheering' -and !(Visible $mascot 'LeftHappyEye')){throw 'Cheering is missing its happy eyes'}
    if($name -eq 'Praying' -and !(Visible $mascot 'LeftSleepyEye')){throw 'Praying is missing its closed eyes'}
    if($name -eq 'Worried' -and !(Visible $mascot 'SweatDrop')){throw 'Worried is missing its sweat drop'}
    if($name -eq 'Victory' -and !(Visible $mascot 'LeftHappyEye')){throw 'Victory is missing its happy eyes'}
    if($name -eq 'Defeat' -and !(Visible $mascot 'TearDropRight')){throw 'Defeat is missing its second tear'}
    if((Visible $mascot 'SweatDrop') -ne [bool]$sweat[$name] -and !($name -eq 'Hot')){throw "Sweat drop wrong in mood $name"}
    if($name -eq 'Hot' -and !(Visible $mascot 'SweatDrop')){throw 'Sweat drop is missing in mood Hot'}
   }else{
    if(Visible $mascot 'SweatDrop'){throw "Sweat drop leaked into plain mood $name"}
    if($name -eq 'Music' -and ((Visible $mascot 'HeadsetLedLeft') -or $mascot.FindName('HeadbandArc').Stroke.Color.ToString() -ne '#FF3A3D4A')){throw 'The music headset did not return to the plain graphite one after a gaming face'}
    $top=$mascot.FindName('BodyGradStopTop').Color;$bottom=$mascot.FindName('BodyGradStopBottom').Color
    if($top.ToString() -ne '#FFEDEDEF' -or $bottom.ToString() -ne '#FFC4C5CA'){throw "Body colours did not return to normal in mood $name ($top / $bottom)"}
    $cheek=$mascot.FindName('LeftCheek').Fill.Color.ToString()
    if($cheek -ne '#73FF7896'){throw "Cheeks did not return to normal in mood $name ($cheek)"}
    if($mascot.FindName('BodyBounceTransform').X -ne 0){throw "Shake leaked into plain mood $name"}
   }
   if($expected.ContainsKey($name)){
    # The extras stay close to the body: at most 9 DIPs above it and 2 DIPs beside it.
    $layer=$mascot.FindName('ExtrasLayer')
    foreach($child in $layer.Children){
     if($child.Visibility -ne [Windows.Visibility]::Visible){continue}
     $bounds=$child.TransformToAncestor($layer).TransformBounds([Windows.Rect]::new(0,0,$child.RenderSize.Width,$child.RenderSize.Height))
     if($child.RenderSize.Width -le 0 -and $child.RenderSize.Height -le 0){continue}
     if($bounds.Y -lt -9.5 -or $bounds.X -lt -2.5 -or $bounds.Right -gt 32.5 -or $bounds.Bottom -gt 28){throw "Extras of $name stick out: $($child.Name) $bounds (scale $scale)"}
    }
   }
   $checks++
  }
 } finally { $native.Dispose() }
}

# A strip of every face, on the black capsule and on the cream capsule, at 6x for a human look.
$all=@('Turning','Idle','Music','Alert','Sleeping','Focused','Happy','Angry','Hot','Freezing','Dizzy','Drained','Victory','Defeat','Worried','BreakTime','Excited','Surprised','Cheering','Praying','Bored','Waking','Electric','Money','MoneyDown','Sunny','Rainy','Snowy','Cloudy','Studying','Petted')
foreach($style in @(@('black',[Windows.Media.Brushes]::Black),@('cream',[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xF3,0xEF,0xE0))))){
 $panel=[Windows.Controls.StackPanel]::new();$panel.Orientation=[Windows.Controls.Orientation]::Horizontal;$panel.Background=$style[1]
 foreach($name in $all){$m=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'));$m.Width=150;$m.Height=130;$m.Margin=[Windows.Thickness]::new(8,60,16,12);$mood=switch($name){'Petted'{'Idle'}'Turning'{'Idle'}default{$name}}
  $m.Mood=[Enum]::Parse($moodType,$mood)
  # Headless renders do not run animations, so show the heart at a mid-animation state by hand.
  if($name -eq 'Petted'){$heart=$m.FindName('FloatingHeart');$heart.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$heart.Opacity=1;$m.FindName('HeartTranslate').Y=-6}
  if($name -eq 'Turning'){Show-Turn $m 600}
  $panel.Children.Add($m)|Out-Null}
 $native,$surface=New-Host $panel 1 $style[1]
 try{
  $surface.Measure([Windows.Size]::new(8000,400));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$surface.ActualWidth,[int]$surface.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $path=Join-Path $output ("moods-"+$style[0]+".png");$stream=[IO.File]::Create($path);$encoder.Save($stream);$stream.Dispose()
 } finally { $native.Dispose() }
}

# ---- Alignment: render every face at 6x and measure the dark ink of the eyes. The eye pair must be centred on the face (x = 15 of 30)
# and the left and right eyes must mirror each other. Animations are not running in this headless render, so their start values
# (a shiver or a shake) are cleared first: only the drawn geometry is measured.
function Ink-Metrics($mood){
 $m=[Activator]::CreateInstance($mascotType);$m.Width=180;$m.Height=156
 $native,$surface=New-Host $m 1 ([Windows.Media.Brushes]::White)
 try{
  $m.Mood=[Enum]::Parse($moodType,$mood)
  $body=$m.FindName('BodyBounceTransform')
  $body.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$body.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null);$body.X=0;$body.Y=0
  $surface.Measure([Windows.Size]::new(400,400));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout()
  $width=[int]$surface.ActualWidth;$height=[int]$surface.ActualHeight
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $stride=$width*4;$pixels=[byte[]]::new($stride*$height);$bitmap.CopyPixels($pixels,$stride,0)
  $origin=10 # the host's padding, in pixels at 1x
  $sums=@{L=@(0.0,0.0,0);R=@(0.0,0.0,0)}
  for($py=[int](8.6*6+$origin);$py -le [int](16.6*6+$origin);$py++){
   for($px=[int](5.5*6+$origin);$px -le [int](24.5*6+$origin);$px++){
    $i=$py*$stride+$px*4;$b=$pixels[$i];$g=$pixels[$i+1];$r=$pixels[$i+2]
    if($r -lt 80 -and $g -lt 80 -and $b -lt 80){ # the dark ink of the eyes (brows are dark red, the dark headset cups sit outside this band)
     $dx=($px-$origin+.5)/6;$dy=($py-$origin+.5)/6
     $side=if($dx -lt 15){'L'}else{'R'}
     $sums[$side][0]+=$dx;$sums[$side][1]+=$dy;$sums[$side][2]++
    }
   }
  }
  return $sums
 } finally { $native.Dispose() }
}
# Where the eyes' vertical centre must lie (DIPs): the pill, wide, round, flat, droopy and spiral eyes are centred on 12.5, the half-lidded ones (Waking, Studying, Up late) lower; Bored looks aside on purpose, so it is not centred and is left out; the happy and sleepy
# arcs sit a little lower by design.
$band=@{Yawning=@(13.4,15.0);Birthday=@(12.6,14.6);Meeting=@(11.9,13.1);Proud=@(12.6,14.6);Idle=@(11.9,13.1);Music=@(12.6,14.6);Alert=@(11.9,13.1);Sleeping=@(12.4,14.2);Focused=@(12.0,13.4);Happy=@(12.6,14.6);Angry=@(11.9,13.1);Hot=@(11.9,13.1);Freezing=@(11.9,13.1);Dizzy=@(11.9,13.1);Drained=@(11.9,13.1);Victory=@(12.6,14.6);Defeat=@(11.9,13.1);Worried=@(11.9,13.1);BreakTime=@(11.9,13.1);Excited=@(11.9,13.1);Surprised=@(11.9,13.1);Cheering=@(12.6,14.6);Praying=@(12.4,14.2);Waking=@(13.4,15.0);Electric=@(11.9,13.1);Sunny=@(11.9,13.3);Rainy=@(11.9,13.1);Snowy=@(11.9,13.1);Cloudy=@(11.9,13.1);Studying=@(13.4,15.0);Coffee=@(11.9,13.1)}
foreach($mood in $band.Keys){
 $ink=Ink-Metrics $mood
 if($ink.L[2] -lt 20 -or $ink.R[2] -lt 20){throw "No eye ink found in mood $mood (left $($ink.L[2]) pixels, right $($ink.R[2]))"}
 $cxL=$ink.L[0]/$ink.L[2];$cxR=$ink.R[0]/$ink.R[2];$cyL=$ink.L[1]/$ink.L[2];$cyR=$ink.R[1]/$ink.R[2]
 $centre=($cxL+$cxR)/2
 if([Math]::Abs($centre-15) -gt .25){throw ("Eyes of mood $mood are off-centre: the pair's centre is x={0:N2} (should be 15.00 +- 0.25)" -f $centre)}
 if([Math]::Abs((15-$cxL)-($cxR-15)) -gt .3){throw ("Eyes of mood $mood do not mirror: left {0:N2} from the centre, right {1:N2}" -f (15-$cxL),($cxR-15))}
 if([Math]::Abs($cyL-$cyR) -gt .3){throw ("Eyes of mood $mood are at different heights: {0:N2} and {1:N2}" -f $cyL,$cyR)}
 $cy=($cyL+$cyR)/2
 if($cy -lt $band[$mood][0] -or $cy -gt $band[$mood][1]){throw ("Eyes of mood $mood are at height {0:N2}, outside {1}..{2}" -f $cy,$band[$mood][0],$band[$mood][1])}
 $checks++
}

# ---- Stroke visibility at the real sizes: Mochi is 22 x 19 DIPs in the capsule and 32 x 28 in the cards, a 30-DIP drawing scaled down.
# A line thinner than 0.8 pixel at 22 x 19 and 100% is almost invisible, so every face feature needs at least 1.09 DIPs of stroke.
$minimumPixels=0.8
$probe=[Activator]::CreateInstance($mascotType)
$features=@('LeftHappyEye','RightHappyEye','LeftSleepyEye','RightSleepyEye','LeftFlatEye','RightFlatEye','LeftDroopEye','RightDroopEye')
$thin=@()
foreach($name in $features){$e=$probe.FindName($name);if($e.StrokeThickness*22/30 -lt $minimumPixels){$thin+=("{0} {1} DIP" -f $name,$e.StrokeThickness)}}
foreach($side in 'Left','Right'){foreach($ring in $probe.FindName("${side}SpiralEye").Children){if($null -ne $ring.Stroke -and $ring.StrokeThickness*22/30 -lt $minimumPixels){$thin+=("{0}SpiralEye ring {1} DIP" -f $side,$ring.StrokeThickness)}}}
if($thin.Count -gt 0){throw "Too thin for the 22 x 19 capsule size (under $minimumPixels px at 100%): $($thin -join ', ')"}
$checks++

# ---- The real sizes, side by side, for a human look: 22 x 19 and 32 x 28 at 100% and 200%, on both capsule backgrounds.
foreach($style in @(@('black',[Windows.Media.Brushes]::Black),@('cream',[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xF3,0xEF,0xE0))))){
 foreach($dpi in 1,2){
  $panel=[Windows.Controls.StackPanel]::new();$panel.Orientation=[Windows.Controls.Orientation]::Horizontal;$panel.Background=$style[1]
  foreach($size in @(@(22,19),@(32,28))){
   foreach($name in 'Idle','Music','Sleeping','Happy','Focused','Worried','BreakTime','Excited','Surprised','Cheering','Praying','Bored','Waking','Victory','Defeat','Angry','Hot','Freezing','Dizzy','Drained'){
    $m=[Activator]::CreateInstance($mascotType);$m.Width=$size[0];$m.Height=$size[1];$m.Margin=[Windows.Thickness]::new(6,12,6,6);$m.Mood=[Enum]::Parse($moodType,$name)
    $body=$m.FindName('BodyBounceTransform');$body.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$body.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null);$body.X=0;$body.Y=0
    $panel.Children.Add($m)|Out-Null
   }
  }
  $panel.Measure([Windows.Size]::new(4000,200));$panel.Arrange([Windows.Rect]::new(0,0,$panel.DesiredSize.Width,$panel.DesiredSize.Height));$panel.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($panel.ActualWidth*$dpi),[int]($panel.ActualHeight*$dpi),96*$dpi,96*$dpi,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($panel)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $path=Join-Path $output ("moods-small-"+$style[0]+"-"+$dpi+"x.png");$stream=[IO.File]::Create($path);$encoder.Save($stream);$stream.Dispose()
 }
}
# ---- Matcha: the body stays green through every mood change (it used to fall back to grey on the first one), a Mochi created after the
# theme was applied starts green, and the neutral faces have their own Matcha tint instead of the grey ones. Strip: moods-matcha.png.
function Body-Top($m){$c=$m.FindName('BodyGradStopTop').Color;return ('#{0:X2}{1:X2}{2:X2}' -f $c.R,$c.G,$c.B)}
$mascotClass=$assembly.GetType('MochiDock.Controls.MascotControl')
$matcha=[Activator]::CreateInstance($mascotClass);$matcha.ApplyTheme($true)
foreach($name in 'Idle','Waiting','Music','Alert','Sleeping','Happy','Money','Snowy','Cloudy','Studying','Coffee','Yawning','Birthday','Meeting','Proud'){
 $matcha.Mood=[Enum]::Parse($moodType,'Angry');$matcha.Mood=[Enum]::Parse($moodType,$name)
 if((Body-Top $matcha) -ne '#85B957'){throw "Matcha: $name should return to the green body but is $(Body-Top $matcha)"}
 $checks++
}
$greys=@('#EDEDEF','#E3E8F2','#EBEBEE','#E4E4E8','#F0EAF7','#EAE6F7','#E2EFFB','#E1E7F0','#E8EEFF','#FFF6E0','#FCEFC4','#FFE4EC','#FFF3DC','#FBEBD3')
foreach($name in 'Bored','Drained','Worried','Focused','Waking','Dizzy','Freezing','Defeat','Excited','Surprised','Victory','Cheering','Praying','BreakTime','Electric','MoneyDown','Sunny','Rainy'){
 $matcha.Mood=[Enum]::Parse($moodType,$name)
 if($greys -contains (Body-Top $matcha)){throw "Matcha: $name still has its grey body"}
 $checks++
}
$mascotClass.GetProperty('CurrentIsMatcha').SetValue($null,$true)
$late=[Activator]::CreateInstance($mascotClass)
if((Body-Top $late) -ne '#85B957'){throw "Matcha: a Mochi created after the theme starts $(Body-Top $late), not green"}
$late.ApplyTheme($false)
if((Body-Top $late) -ne '#EDEDEF'){throw "Default: switching back should restore the grey body but is $(Body-Top $late)"}
$checks++
$mascotClass.GetProperty('CurrentIsMatcha').SetValue($null,$false)
# ---- Hojicha: neutral faces wear the kinako-tan body; coloured faces (Angry, Hot, Freezing...) keep their own colour.
$themeType=$mascotClass.GetNestedType('MascotTheme')
$hojichaTheme=[Enum]::Parse($themeType,'Hojicha')
$hojicha=[Activator]::CreateInstance($mascotClass);$hojicha.ApplyTheme($hojichaTheme)
foreach($name in 'Idle','Waiting','Music','Alert','Sleeping','Happy','Money','Snowy','Cloudy','Studying','Coffee','Yawning','Birthday','Meeting','Proud'){
 $hojicha.Mood=[Enum]::Parse($moodType,'Angry');$hojicha.Mood=[Enum]::Parse($moodType,$name)
 if((Body-Top $hojicha) -ne '#E9D3B4'){throw "Hojicha: $name should wear the tan body but is $(Body-Top $hojicha)"}
 $checks++
}
$hojicha.Mood=[Enum]::Parse($moodType,'Angry')
if((Body-Top $hojicha) -eq '#E9D3B4'){throw 'Hojicha: Angry lost its own colour'}
$mascotClass.GetProperty('CurrentTheme').SetValue($null,$hojichaTheme)
$lateTan=[Activator]::CreateInstance($mascotClass)
if((Body-Top $lateTan) -ne '#E9D3B4'){throw "Hojicha: a Mochi created after the theme starts $(Body-Top $lateTan), not tan"}
$lateTan.ApplyTheme('Default')
if((Body-Top $lateTan) -ne '#EDEDEF'){throw "Default: switching back from Hojicha should restore the grey body but is $(Body-Top $lateTan)"}
$checks+=2
$mascotClass.GetProperty('CurrentIsMatcha').SetValue($null,$false)
$panel=[Windows.Controls.StackPanel]::new();$panel.Orientation=[Windows.Controls.Orientation]::Horizontal;$panel.Background=[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xE4,0xEC,0xD8))
foreach($name in 'Idle','Music','Sleeping','Happy','Angry','Hot','Freezing','Dizzy','Drained','Worried','Focused','Bored','Waking','Defeat','Victory','Excited','Surprised','Cheering','Praying','Electric','Money','MoneyDown','Sunny','Rainy','Snowy','Cloudy','Studying','Turning'){
 $m=[Activator]::CreateInstance($mascotClass);$m.Width=150;$m.Height=130;$m.Margin=[Windows.Thickness]::new(8,60,16,12);$m.ApplyTheme($true);$m.Mood=[Enum]::Parse($moodType,$(if($name -eq 'Turning'){'Idle'}else{$name}));if($name -eq 'Turning'){Show-Turn $m 600};$panel.Children.Add($m)|Out-Null}
$parts=New-Host $panel 1 ([Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xE4,0xEC,0xD8)))
$sheet=$parts[1];$sheet.Measure([Windows.Size]::new(8000,400));$sheet.Arrange([Windows.Rect]::new(0,0,$sheet.DesiredSize.Width,$sheet.DesiredSize.Height));$sheet.UpdateLayout()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($sheet.ActualWidth*2),[int]($sheet.ActualHeight*2),192,192,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($sheet)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream=[IO.File]::Create((Join-Path $output 'moods-matcha.png'));$encoder.Save($stream);$stream.Dispose()

# ---- README mood sheets: every face with its name, 8 per row, the same list on both themes, plus one frame of the face-change spin.
$sheetFaces=[ordered]@{Idle='Idle';Waiting='Thinking';Music='Music';Alert='Notice';Excited='New message';Sleeping='Sleeping';Waking='Waking up';Bored='Bored';
 Coffee='Coffee';Happy='Happy';Cheering='Cheering';Surprised='Surprised';Praying='Prayer time';Focused='Gaming';Worried='Game going badly';BreakTime='Break reminder';
 Victory='Victory';Defeat='Defeat';Hot='Hot';Freezing='Freezing';Angry='Overdue task';Dizzy='Dizzy';Drained='Low battery';Electric='Electric';
 Money='Money';MoneyDown='Money (rate down)';Sunny='Sunny';Rainy='Rainy';Snowy='Snowy';Cloudy='Cloudy';Studying='Studying';Yawning='Up late';Birthday='Birthday';Meeting='In a meeting';Proud='All tasks done';Turning='Changing face'}
foreach($sheet in @(@('default',$false,'#000000','#F2F2F5'),@('matcha',$true,'#EFE9D8','#283322'))){
 $grid=[Windows.Controls.Primitives.UniformGrid]::new();$grid.Columns=8;$grid.Background=[Windows.Media.BrushConverter]::new().ConvertFromString($sheet[2])
 foreach($key in $sheetFaces.Keys){
  $cell=[Windows.Controls.StackPanel]::new();$cell.Margin=[Windows.Thickness]::new(6,30,6,14)
  $m=[Activator]::CreateInstance($mascotClass);$m.Width=96;$m.Height=83;if($sheet[1]){$m.ApplyTheme($true)}
  $m.Mood=[Enum]::Parse($moodType,$(if($key -eq 'Turning'){'Idle'}else{$key}));if($key -eq 'Turning'){Show-Turn $m 400}
  $label=[Windows.Controls.TextBlock]::new();$label.Text=$sheetFaces[$key];$label.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI Semibold');$label.FontSize=12.5
  $label.Foreground=[Windows.Media.BrushConverter]::new().ConvertFromString($sheet[3]);$label.HorizontalAlignment='Center';$label.Margin=[Windows.Thickness]::new(0,10,0,0)
  [void]$cell.Children.Add($m);[void]$cell.Children.Add($label);[void]$grid.Children.Add($cell)
 }
 $parts=New-Host $grid 1 $grid.Background;$host2=$parts[1];$host2.Measure([Windows.Size]::new(1200,2000));$host2.Arrange([Windows.Rect]::new(0,0,$host2.DesiredSize.Width,$host2.DesiredSize.Height));$host2.UpdateLayout()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($host2.ActualWidth*2),[int]($host2.ActualHeight*2),192,192,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($host2)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=[IO.File]::Create((Join-Path $output "moods-grid-$($sheet[0]).png"));$encoder.Save($stream);$stream.Dispose();$parts[0].Dispose()
}
$checks++

$sheetFacesKuro=$sheetFaces
# ---- KuroNight: Mochi becomes Kuro, a black electric cat. The body is charcoal for every face (never yellow); a face's own colour goes
# into the rim and the glow, the neutral faces glow electric yellow; the ears and the tail show only in this theme; the face ink is light.
$kuroNightTheme=[Enum]::Parse($themeType,'KuroNight')
$kuroGround=[Windows.Media.Color]::FromRgb(0x0C,0x0D,0x18)
function Hex-Of($c){return ('#{0:X2}{1:X2}{2:X2}' -f $c.R,$c.G,$c.B)}
function Brush-Hex($b){return (Hex-Of $b.Color)}
$kuro=[Activator]::CreateInstance($mascotClass);$kuro.ApplyTheme($kuroNightTheme)
foreach($part in 'KuroEars','KuroTail'){if(!(Visible $kuro $part)){throw "KuroNight: $part should be visible"}}
$allMoods=[Enum]::GetNames($moodType)
foreach($name in $allMoods){
 $kuro.Mood=[Enum]::Parse($moodType,'Angry');$kuro.Mood=[Enum]::Parse($moodType,$name)
 if((Body-Top $kuro) -ne '#3B3D52'){throw "KuroNight: $name should keep the charcoal body but is $(Body-Top $kuro)"}
 $bottom=$kuro.FindName('BodyGradStopBottom').Color;if((Hex-Of $bottom) -ne '#23253A'){throw "KuroNight: $name bottom is $(Hex-Of $bottom), not charcoal"}
 if($kuro.FindName('LeftCheek').Fill.Color.A -ne 0){throw "KuroNight: $name has cheek circles (Kuro has none)"}
 if(!(Visible $kuro 'KuroEars') -or !(Visible $kuro 'KuroTail')){throw "KuroNight: $name lost the ears or the tail"}
 if((Brush-Hex $kuro.FindResource('MascotEyeBrush')) -ne '#FFD43B'){throw "KuroNight: the eye ink is not electric yellow in $name"}
 $glow=$kuro.FindName('BodyGlowFx');if(!(Visible $kuro 'BodyGlow') -or $glow.ShadowDepth -ne 0 -or $glow.Color.ToString() -eq '#FF000000' -or $kuro.FindName('BodyShadow').Opacity -ne 0){throw "KuroNight: $name has the black drop shadow instead of a glow"}
 if($kuro.FindName('TailShape').Stroke.Color.ToString() -ne $kuro.FindName('MochiBodyBorder').BorderBrush.Color.ToString()){throw "KuroNight: the tail rim of $name does not match the body rim"}
 $checks++
}
$kuro.Mood=[Enum]::Parse($moodType,'Idle')
if((Brush-Hex $kuro.FindName('MochiBodyBorder').BorderBrush) -ne '#FFD43B' -or (Hex-Of $kuro.FindName('BodyGlowFx').Color) -ne '#FFD43B'){throw 'KuroNight: the neutral Idle face should have an electric-yellow rim and glow'}
$kuro.Mood=[Enum]::Parse($moodType,'Angry')
if((Hex-Of $kuro.FindName('BodyGlowFx').Color) -eq '#FFD43B' -or (Hex-Of $kuro.FindName('BodyGlowFx').Color) -ne '#C0392B'){throw "KuroNight: Angry should glow red, not $(Hex-Of $kuro.FindName('BodyGlowFx').Color)"}
$kuro.Mood=[Enum]::Parse($moodType,'Electric')
if((Body-Top $kuro) -ne '#3B3D52'){throw 'KuroNight: the Electric face turned Kuro yellow'}
if($kuro.FindName('BodyGlowFx').Opacity -le 0.55){throw 'KuroNight: the Electric glow should be brighter'}
if(!(Visible $kuro 'StarEyes') -or !(Visible $kuro 'BoltOrbit')){throw 'KuroNight: the Electric face lost its star eyes or its bolts'}
# The ears may poke about 3 DIPs above the box; the tail is checked below.
$kuro.Width=30;$kuro.Height=26;$kuro.Measure([Windows.Size]::new(60,60));$kuro.Arrange([Windows.Rect]::new(0,0,30,26));$kuro.UpdateLayout()
# The tail is one filled shape behind the body: on the left half only (away from the clock), poking out at most 4.5 units on the left and never
# above or below the box or past the middle, so the capsule balance does not change.
$tailPath=$kuro.FindName('TailShape');$pen=[Windows.Media.Pen]::new([Windows.Media.Brushes]::Black,$tailPath.StrokeThickness);$pen.LineJoin='Round'
$r=$tailPath.Data.GetRenderBounds($pen)
if($r.X -lt -4.5 -or $r.Y -lt 0 -or $r.Bottom -gt 26 -or $r.Right -gt 15){throw "KuroNight: TailShape is out of its room (X >= -4.5, Y >= 0, Bottom <= 26, Right <= 15): $r"}
$rootGrid=$kuro.FindName('RootGrid')
if($rootGrid.Children.IndexOf($kuro.FindName('KuroTail')) -ge $rootGrid.Children.IndexOf($kuro.FindName('MochiBodyBorder'))){throw 'KuroNight: the tail must sit behind the body'}
if(!$tailPath.Effect -or $kuro.FindName('TailGlow') -eq $null -or $kuro.FindName('TailSwayRotate') -eq $null){throw 'KuroNight: the tail lost its glow or its sway transform'}
# The tail pivots on its base and is at rest (no running animation) after the face is drawn.
if($kuro.FindName('TailSwayRotate').HasAnimatedProperties){throw 'KuroNight: the tail is animating at rest (it must only move in one-shot sways and flicks)'}
$checks+=3
foreach($ear in 'EarLeft','EarRight'){
 $p=$kuro.FindName($ear);$pen=[Windows.Media.Pen]::new([Windows.Media.Brushes]::Black,$p.StrokeThickness);$r=$p.Data.GetRenderBounds($pen)
 if($r.Y -lt -3.6 -or $r.X -lt 0 -or $r.Right -gt 30){throw "KuroNight: $ear pokes out too far ($r)"}
}
$checks+=2
# Ears and tail, the light ink and the black shadow belong to KuroNight only.
foreach($other in 'Default','Hojicha','Matcha'){
 $o=[Activator]::CreateInstance($mascotClass);$o.ApplyTheme($kuroNightTheme);$o.ApplyTheme($other)
 foreach($part in 'KuroEars','KuroTail'){if(Visible $o $part){throw "$other shows $part"}}
 if(Visible $o 'BodyGlow'){throw "$other shows the Kuro glow"}
 if((Brush-Hex $o.FindResource('MascotEyeBrush')) -ne '#1A1412' -or (Brush-Hex $o.FindResource('MascotInkBrush')) -ne '#1A1412'){throw "$other lost its dark face ink after KuroNight"}
 if((Brush-Hex $o.FindResource('MascotSleepyBrush')) -ne '#2C2422'){throw "$other lost its sleepy ink after KuroNight"}
 $s=$o.FindName('BodyShadow');if($s.Color.ToString() -ne '#FF000000' -or $s.ShadowDepth -ne 1 -or $s.BlurRadius -ne 4 -or [math]::Abs($s.Opacity-0.25) -gt 1e-9){throw "$other did not get the black shadow back ($($s.Color) $($s.ShadowDepth) $($s.BlurRadius) $($s.Opacity))"}
 if($o.FindName('BodySheen').Opacity -ne 1){throw "$other lost its body highlight"}
 if((Body-Top $o) -eq '#3B3D52'){throw "$other kept the charcoal body"}
 $checks++
}
# A Kuro created after the theme switch starts as a cat; switching back restores Mochi.
$mascotClass.GetProperty('CurrentTheme').SetValue($null,$kuroNightTheme)
$lateKuro=[Activator]::CreateInstance($mascotClass)
if((Body-Top $lateKuro) -ne '#3B3D52' -or !(Visible $lateKuro 'KuroEars') -or !(Visible $lateKuro 'KuroTail')){throw 'KuroNight: a Mochi created after the theme should start as Kuro'}
$lateKuro.ApplyTheme('Default')
if((Body-Top $lateKuro) -ne '#EDEDEF' -or (Visible $lateKuro 'KuroEars')){throw 'Default: switching back from KuroNight should restore Mochi'}
$lateKuro.ApplyTheme('kuro night')
if(!(Visible $lateKuro 'KuroEars')){throw 'ApplyTheme("kuro night") should make Kuro'}
$checks+=3
$mascotClass.GetProperty('CurrentIsMatcha').SetValue($null,$false)

# ---- No mascot may vanish on any capsule: Mochi, Matcha Mochi, Hojicha Mochi, Kuro and Shiro (Auto is the theme's own one of these) rendered at the capsule size (22 x 19) on the ground of every theme at 100, 125,
# 150 and 200%. The eyes stay at least 4.5:1 against the body and the body, rim and glow ring at least 3:1 against the ground (WCAG non-text contrast).
$kindType=$core.GetType('MochiDock.Core.MascotKind')
$headsetType=$core.GetType('MochiDock.Core.MascotHeadset')
function Kind($n){return [Enum]::Parse($kindType,$n)}
function Theme-Of($n){return [Enum]::Parse($themeType,$n)}
function New-Mascot($theme,$kind){$m=[Activator]::CreateInstance($mascotClass);$m.ApplyTheme((Theme-Of $theme),(Kind $kind));return $m}
function Lum($r,$g,$b){$f={param($v)$c=$v/255.0;if($c -le 0.03928){$c/12.92}else{[math]::Pow(($c+0.055)/1.055,2.4)}};return 0.2126*(& $f $r)+0.7152*(& $f $g)+0.0722*(& $f $b)}
function Contrast($l1,$l2){$a=[math]::Max($l1,$l2);$b=[math]::Min($l1,$l2);return ($a+0.05)/($b+0.05)}
function Render-Pixels($m,$scale,$ground){
 $native,$surface=New-Host $m $scale ([Windows.Media.SolidColorBrush]::new($ground))
 try{
  $surface.Measure([Windows.Size]::new(200,200));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout()
  $w=[int][math]::Ceiling($surface.ActualWidth*$scale);$h=[int][math]::Ceiling($surface.ActualHeight*$scale)
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($w,$h,96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $stride=$w*4;$pixels=[byte[]]::new($stride*$h);$bitmap.CopyPixels($pixels,$stride,0)
  return @{Pixels=$pixels;Stride=$stride;Width=$w;Height=$h;Bitmap=$bitmap}
 } finally { $native.Dispose() }
}
$grounds=[ordered]@{Default='#000000';Hojicha='#14100D';KuroNight='#0C0D18';Matcha='#EFE6CF'}
$catMetrics=@();$catFailures=@()
foreach($groundName in $grounds.Keys){
 $groundColor=[Windows.Media.ColorConverter]::ConvertFromString($grounds[$groundName])
 $groundLum=Lum $groundColor.R $groundColor.G $groundColor.B
 foreach($kindName in 'Mochi','MatchaMochi','HojichaMochi','Kuro','Shiro'){
  foreach($face in 'Idle','Angry','Freezing','Sleeping','Electric'){
   foreach($scale in 1,1.25,1.5,2){
    $m=New-Mascot $groundName $kindName;$m.Width=22;$m.Height=19;$m.Mood=[Enum]::Parse($moodType,$face)
    foreach($n in 'BoltA','BoltB','BoltC'){$e=$m.FindName($n);$e.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$e.Opacity=1}
    $bounce=$m.FindName('BodyBounceTransform');$bounce.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$bounce.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null);$bounce.X=0;$bounce.Y=0   # headless renders do not run the shake: measure the drawn geometry
    $img=Render-Pixels $m $scale $groundColor
    $px=$img.Pixels;$stride=$img.Stride
    $unit=22.0/30*$scale   # pixels per drawing unit
    $origin=10*$scale      # the host's padding, in pixels
    $bodyX=[int][math]::Floor($origin+4.5*$unit);$bodyY=[int][math]::Floor($origin+15*$unit);$bi=$bodyY*$stride+$bodyX*4
    $bodyLum=Lum $px[$bi+2] $px[$bi+1] $px[$bi]
    # the eyes: the pixel in the eye band that differs most from the body next to it
    $eyeContrast=1.0
    for($y=[int]($origin+8.5*$unit);$y -le [int]($origin+17*$unit);$y++){for($x=[int]($origin+6*$unit);$x -le [int]($origin+24*$unit);$x++){
     $i=$y*$stride+$x*4;$c=Contrast (Lum $px[$i+2] $px[$i+1] $px[$i]) $bodyLum;if($c -gt $eyeContrast){$eyeContrast=$c}}}
    # the body, rim and glow ring: the pixel that differs most from the ground in a band across the right edge (6 units inside to 3.5 outside)
    $ringContrast=1.0
    for($y=[int]($origin+9*$unit);$y -le [int]($origin+17*$unit);$y++){for($x=[int]($origin+24*$unit);$x -le [int]($origin+33.5*$unit);$x++){
     $i=$y*$stride+$x*4;$c=Contrast (Lum $px[$i+2] $px[$i+1] $px[$i]) $groundLum;if($c -gt $ringContrast){$ringContrast=$c}}}
    $catMetrics+=("{0} on {1} ground, {2} {3}%: eyes {4:N1}:1, body/rim/glow {5:N1}:1" -f $kindName,$groundName,$face,($scale*100),$eyeContrast,$ringContrast)
    if($face -ne 'Electric' -and $face -ne 'Sleeping' -and $eyeContrast -lt 4.5){$catFailures+=("{0} {1} on the {2} ground at {3}%: the eyes are only {4:N2}:1 against the body" -f $kindName,$face,$groundName,($scale*100),$eyeContrast)}
    if($ringContrast -lt 3){$catFailures+=("{0} {1} on the {2} ground at {3}%: the body, rim and glow ring is only {4:N2}:1 against the ground" -f $kindName,$face,$groundName,($scale*100),$ringContrast)}
    if($face -eq 'Idle' -and ($scale -eq 1 -or $scale -eq 2)){
     $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($img.Bitmap))
     $file="{0}-22x19-{1}-{2}.png" -f $kindName.ToLower(),$groundName.ToLower(),([int]($scale*100))
     $stream=[IO.File]::Create((Join-Path $output $file));$encoder.Save($stream);$stream.Dispose()
    }
    $checks++
   }
  }
 }
}
$catMetrics|Set-Content (Join-Path $output 'mascot-contrast.txt')
if($catFailures.Count -gt 0){throw ($catFailures -join "; ")}

# ---- A contact sheet of every face on the KuroNight ground: moods-kuro.png (big) and moods-kuro-capsule.png (22 x 19, 4x zoom of the real size).
$grid=[Windows.Controls.Primitives.UniformGrid]::new();$grid.Columns=8;$grid.Background=[Windows.Media.SolidColorBrush]::new($kuroGround)
foreach($key in $sheetFacesKuro.Keys){
 $cell=[Windows.Controls.StackPanel]::new();$cell.Margin=[Windows.Thickness]::new(6,30,6,14)
 $m=[Activator]::CreateInstance($mascotClass);$m.Width=96;$m.Height=83;$m.ApplyTheme($kuroNightTheme)
 $m.Mood=[Enum]::Parse($moodType,$(if($key -eq 'Turning'){'Idle'}else{$key}));if($key -eq 'Turning'){Show-Turn $m 400}
 $label=[Windows.Controls.TextBlock]::new();$label.Text=$sheetFacesKuro[$key];$label.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI Semibold');$label.FontSize=12.5
 $label.Foreground=[Windows.Media.BrushConverter]::new().ConvertFromString('#F2F3F8');$label.HorizontalAlignment='Center';$label.Margin=[Windows.Thickness]::new(0,10,0,0)
 [void]$cell.Children.Add($m);[void]$cell.Children.Add($label);[void]$grid.Children.Add($cell)
}
$native,$host2=New-Host $grid 1 $grid.Background
try{
 $host2.Measure([Windows.Size]::new(1200,2000));$host2.Arrange([Windows.Rect]::new(0,0,$host2.DesiredSize.Width,$host2.DesiredSize.Height));$host2.UpdateLayout()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($host2.ActualWidth*2),[int]($host2.ActualHeight*2),192,192,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($host2)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=[IO.File]::Create((Join-Path $output 'moods-kuro.png'));$encoder.Save($stream);$stream.Dispose()
} finally { $native.Dispose() }
$checks++

# ---- Shiro, the white cat: Mochi's normal face tints on every theme with a white body for the neutral faces (never Matcha green), dark ink,
# pink cheeks and inner ears, an ice-blue tail spark, the black shadow and no glow; the ears and the tail take the body's colours.
# A colour counts as yellow when its hue is from 35 to 70 degrees with some colour in it: no cat may wear a yellow face's body.
function Is-Yellow($hex){$c=[Windows.Media.ColorConverter]::ConvertFromString($hex);$r=$c.R;$g=$c.G;$b=$c.B;$max=[math]::Max($r,[math]::Max($g,$b));$min=[math]::Min($r,[math]::Min($g,$b));$d=$max-$min
 if($max -eq 0 -or $d -eq 0 -or ($d/$max) -lt 0.08){return $false}
 $h=if($max -eq $r){60*((($g-$b)/$d+6)%6)}elseif($max -eq $g){60*(($b-$r)/$d+2)}else{60*(($r-$g)/$d+4)}
 return ($h -ge 35 -and $h -le 70)}
$shiroNeutral=@('Idle','Waiting','Music','Alert','Sleeping','Happy','Money','Snowy','Cloudy','Studying','Coffee','Yawning','Birthday','Meeting','Proud','Electric')
foreach($themeName in 'Default','Hojicha','KuroNight','Matcha'){
 $s=New-Mascot $themeName 'Shiro'
 foreach($part in 'KuroEars','KuroTail'){if(!(Visible $s $part)){throw "Shiro on ${themeName}: $part should be visible"}}
 if(Visible $s 'BodyGlow'){throw "Shiro on ${themeName} has a glow"}
 foreach($name in $allMoods){
  $s.Mood=[Enum]::Parse($moodType,'Angry');$s.Mood=[Enum]::Parse($moodType,$name)
  $top=Body-Top $s
  if($top -eq '#85B957'){throw "Shiro on ${themeName}: $name turned Matcha green"}
  # A coloured body is judged by its edge, as ShiroTint does (Break reminder's peach has an orange edge); a yellow edge must come with the white body.
  $faceEdge=Hex-Of $s.FindName('MochiBodyBorder').BorderBrush.Color
  if($top -ne '#F4F4F8' -and (Is-Yellow $faceEdge)){throw "Shiro on ${themeName}: $name turned yellow ($top, edge $faceEdge)"}
  if($shiroNeutral -contains $name -and $top -ne '#F4F4F8'){throw "Shiro on ${themeName}: $name should keep the white body but is $top"}
  if($name -eq 'Angry' -and $top -eq '#F4F4F8'){throw "Shiro on ${themeName}: Angry lost its own colour"}
  $edge=Hex-Of $s.FindName('MochiBodyBorder').BorderBrush.Color
  foreach($ear in 'EarLeft','EarRight'){
   if((Hex-Of $s.FindName($ear).Fill.Color) -ne $top){throw "Shiro on ${themeName}: the $ear of $name is not the body colour ($top)"}
   if((Hex-Of $s.FindName($ear).Stroke.Color) -ne $edge){throw "Shiro on ${themeName}: the $ear rim of $name is not the body edge ($edge)"}
  }
  if((Hex-Of $s.FindName('TailShape').Fill.Color) -ne $top -or (Hex-Of $s.FindName('TailShape').Stroke.Color) -ne $edge){throw "Shiro on ${themeName}: the tail of $name does not match the body"}
  if((Brush-Hex $s.FindResource('MascotEyeBrush')) -ne '#1A1412'){throw "Shiro on ${themeName}: the eye ink is not dark in $name"}
  if($s.FindName('LeftCheek').Fill.Color.A -eq 0){throw "Shiro on ${themeName}: $name lost the pink cheeks"}
  if((Hex-Of $s.FindName('TailSpark').Fill.Color) -ne '#8FD3FF'){throw "Shiro on ${themeName}: the tail spark is not ice blue"}
  if((Hex-Of $s.FindName('InnerEarLeft').Fill.Color) -ne '#F4B6C2'){throw "Shiro on ${themeName}: the inner ears are not pink"}
  $sh=$s.FindName('BodyShadow');if($sh.Color.ToString() -ne '#FF000000' -or $sh.ShadowDepth -ne 1 -or [math]::Abs($sh.Opacity-0.25) -gt 1e-9){throw "Shiro on ${themeName}: the black shadow is gone in $name"}
  $checks++
 }
 $s.Mood=[Enum]::Parse($moodType,'Idle')
 $wantRim=if($themeName -eq 'Matcha'){'#70727E'}else{'#B8B9C0'}
 if((Hex-Of $s.FindName('MochiBodyBorder').BorderBrush.Color) -ne $wantRim){throw "Shiro on ${themeName}: the neutral rim should be $wantRim"}
 $s.Mood=[Enum]::Parse($moodType,'Victory')
 $wantGold=if($themeName -eq 'Matcha'){'#8F6B0E'}else{'#D9A21C'}
 if((Body-Top $s) -ne '#F4F4F8' -or (Hex-Of $s.FindName('MochiBodyBorder').BorderBrush.Color) -ne $wantGold){throw "Shiro on ${themeName}: Victory should keep the white body with a $wantGold rim"}
 $s.Mood=[Enum]::Parse($moodType,'Electric')
 if(!(Visible $s 'StarEyes') -or !(Visible $s 'BoltOrbit') -or (Body-Top $s) -ne '#F4F4F8'){throw "Shiro on ${themeName}: the Electric face should keep the white body with star eyes and bolts"}
 $checks+=2
}
# The three Mochi colours come from the mascot, not the theme: grey, green or tan on every ground, no ears, tail or glow, dark ink; on the Matcha
# ground the rim is darker and thicker (and only there).
$mochiBodies=@{Mochi='#EDEDEF';MatchaMochi='#85B957';HojichaMochi='#E9D3B4'}
foreach($kindName in $mochiBodies.Keys){
 foreach($themeName in 'Default','Hojicha','KuroNight','Matcha'){
  $o=New-Mascot $themeName $kindName
  foreach($part in 'KuroEars','KuroTail','BodyGlow'){if(Visible $o $part){throw "$kindName on ${themeName} shows $part"}}
  if((Body-Top $o) -ne $mochiBodies[$kindName]){throw "$kindName on ${themeName} has the body $(Body-Top $o), not $($mochiBodies[$kindName])"}
  if((Brush-Hex $o.FindResource('MascotEyeBrush')) -ne '#1A1412'){throw "$kindName on ${themeName} lost the dark ink"}
  $thick=$o.FindName('MochiBodyBorder').BorderThickness.Left;$wantThick=if($themeName -eq 'Matcha'){1.5}else{0.8}
  if([math]::Abs($thick-$wantThick) -gt 1e-9){throw "$kindName on ${themeName}: the rim is $thick thick, expected $wantThick"}
  $o.Mood=[Enum]::Parse($moodType,'Angry');$o.Mood=[Enum]::Parse($moodType,'Idle')
  if((Body-Top $o) -ne $mochiBodies[$kindName]){throw "$kindName on ${themeName} lost its body after a face change"}
  $checks+=2
 }
}
# Auto is the theme's own mascot, with exactly the old look: grey on Default, tan on Hojicha, green on Matcha.
foreach($pair in @(@('Default','#EDEDEF'),@('Hojicha','#E9D3B4'),@('Matcha','#85B957'))){
 $o=[Activator]::CreateInstance($mascotClass);$o.ApplyTheme((Theme-Of $pair[0]))
 if((Body-Top $o) -ne $pair[1]){throw "Auto on $($pair[0]) should be $($pair[1]) but is $(Body-Top $o)"}
 $checks++
}
# Switching the kind: Shiro back to Mochi gets his body, ink and shadow back; Auto picks Kuro only in KuroNight.
$sw=New-Mascot 'Matcha' 'Shiro';$sw.ApplyTheme((Theme-Of 'Matcha'),(Kind 'MatchaMochi'))
if((Body-Top $sw) -ne '#85B957' -or (Visible $sw 'KuroEars')){throw 'Matcha Mochi did not come back after Shiro'}
$sw.ApplyTheme((Theme-Of 'Default'),(Kind 'Kuro'));$sw.ApplyTheme((Theme-Of 'Default'),(Kind 'Shiro'))
if((Brush-Hex $sw.FindResource('MascotEyeBrush')) -ne '#1A1412' -or (Body-Top $sw) -ne '#F4F4F8'){throw 'Shiro after Kuro kept the light ink or the charcoal body'}
$mascotClass.GetProperty('CurrentTheme').SetValue($null,(Theme-Of 'Matcha'))
$mascotClass.GetProperty('CurrentKind').SetValue($null,(Kind 'Shiro'))
$lateShiro=[Activator]::CreateInstance($mascotClass)
if((Body-Top $lateShiro) -ne '#F4F4F8' -or !(Visible $lateShiro 'KuroEars')){throw 'A Mochi created after the theme and the choice should start as Shiro'}
$mascotClass.GetProperty('CurrentTheme').SetValue($null,(Theme-Of 'Default'))   # clears the explicit kind back to Auto
$checks+=3

# ---- The headset matches the mascot (MascotHeadset): band and cups in the mascot's colours, a rimmed band, a ring on the cups; the cats wear the
# band behind their ears; gaming and worried show red lights, a meeting the microphone in the ring colour.
$headsetCombos=@();foreach($k in 'Mochi','MatchaMochi','HojichaMochi','Kuro','Shiro'){foreach($th in 'Default','Hojicha','Matcha','KuroNight'){$headsetCombos+=,@($k,$th)}}
foreach($combo in $headsetCombos){
 $kindName=$combo[0];$themeName=$combo[1];$cat=$kindName -eq 'Kuro' -or $kindName -eq 'Shiro'
 $look=$headsetType.GetMethod('For').Invoke($null,@((Kind $kindName),$themeName))
 $m=New-Mascot $themeName $kindName
 foreach($mood in 'Music','Focused','Worried','Meeting','Idle','Music'){
  $m.Mood=[Enum]::Parse($moodType,$mood)
  $label="$kindName on $themeName, $mood"
  if($mood -eq 'Idle'){
   if((Visible $m 'HeadphonesLayer') -or (Visible $m 'HeadbandBackLayer')){throw "${label}: the headset stayed on after the face changed (layer $(Visible $m 'HeadphonesLayer'), back $(Visible $m 'HeadbandBackLayer'))"}
   continue
  }
  if(!(Visible $m 'HeadphonesLayer')){throw "${label}: the headset is missing"}
  if((Hex-Of $m.FindName('HeadbandArc').Stroke.Color) -ne $look.Band -or (Hex-Of $m.FindName('HeadbandRim').Stroke.Color) -ne $look.BandRim){throw "${label}: the band is not $($look.Band) with a $($look.BandRim) rim"}
  if((Hex-Of $m.FindName('HeadbandBack').Stroke.Color) -ne $look.Band -or (Hex-Of $m.FindName('HeadbandBackRim').Stroke.Color) -ne $look.BandRim){throw "${label}: the back band is not in the mascot's colours"}
  foreach($cup in 'EarCupLeft','EarCupRight'){
   $c=$m.FindName($cup)
   if((Hex-Of $c.Background.Color) -ne $look.Cup -or (Hex-Of $c.BorderBrush.Color) -ne $look.CupRing){throw "${label}: $cup is not $($look.Cup) with a $($look.CupRing) ring"}
  }
  if($cat){
   if(!(Visible $m 'HeadbandBackLayer') -or $m.FindName('HeadbandBackLayer').Opacity -ne 1){throw "${label}: a cat wears the band behind the ears"}
   if(Visible $m 'HeadbandArc'){throw "${label}: a cat must not wear the front band across the ears"}
   $rootGrid=$m.FindName('RootGrid')
   if($rootGrid.Children.IndexOf($m.FindName('HeadbandBackLayer')) -ge $rootGrid.Children.IndexOf($m.FindName('KuroEars'))){throw "${label}: the back band must be behind the ears"}
  }else{
   if(!(Visible $m 'HeadbandArc') -or $m.FindName('HeadbandBackLayer').Opacity -ne 0){throw "${label}: Mochi wears the band in front"}
  }
  $gaming=$mood -eq 'Focused' -or $mood -eq 'Worried'
  if((Visible $m 'HeadsetLedLeft') -ne $gaming -or (Visible $m 'HeadsetLedRight') -ne $gaming){throw "${label}: the red lights should be $(if($gaming){'on'}else{'off'})"}
  if($gaming -and (Hex-Of $m.FindName('HeadsetLedLeft').Fill.Color) -ne '#E5484D'){throw "${label}: the lights are not red"}
  if((Visible $m 'MicBoom') -ne ($mood -eq 'Meeting')){throw "${label}: the microphone should be $(if($mood -eq 'Meeting'){'shown'}else{'hidden'})"}
  if($mood -eq 'Meeting' -and ((Hex-Of $m.FindName('MicBoomArm').Stroke.Color) -ne $look.CupRing -or (Hex-Of $m.FindName('MicBoomTip').Fill.Color) -ne $look.CupRing)){throw "${label}: the microphone is not in the ring colour"}
  $checks++
 }
}

# ---- Contact sheets: moods-shiro.png (Shiro on the Default ground), moods-shiro-matcha.png (on the Matcha ground) and headsets.png
# (Music, Gaming, Worried and Meeting for Mochi on four palettes, Kuro and Shiro).
function Save-Sheet($element,$name,$scale){
 $native,$host2=New-Host $element 1 $element.Background
 try{
  $host2.Measure([Windows.Size]::new(1600,2400));$host2.Arrange([Windows.Rect]::new(0,0,$host2.DesiredSize.Width,$host2.DesiredSize.Height));$host2.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($host2.ActualWidth*$scale),[int]($host2.ActualHeight*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($host2)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.File]::Create((Join-Path $output $name));$encoder.Save($stream);$stream.Dispose()
 } finally { $native.Dispose() }
}
function Label-Brush($ground){$c=[Windows.Media.ColorConverter]::ConvertFromString($ground);if((Lum $c.R $c.G $c.B) -gt 0.4){return [Windows.Media.Brushes]::Black}else{return [Windows.Media.BrushConverter]::new().ConvertFromString('#F2F3F8')}}
foreach($sheetInfo in @(@('moods-shiro.png','Default'),@('moods-shiro-matcha.png','Matcha'))){
 $groundHex=$grounds[$sheetInfo[1]]
 $grid=[Windows.Controls.Primitives.UniformGrid]::new();$grid.Columns=8;$grid.Background=[Windows.Media.BrushConverter]::new().ConvertFromString($groundHex)
 foreach($key in $sheetFaces.Keys){
  $cell=[Windows.Controls.StackPanel]::new();$cell.Margin=[Windows.Thickness]::new(6,30,6,14)
  $m=New-Mascot $sheetInfo[1] 'Shiro';$m.Width=96;$m.Height=83
  $m.Mood=[Enum]::Parse($moodType,$(if($key -eq 'Turning'){'Idle'}else{$key}));if($key -eq 'Turning'){Show-Turn $m 400}
  $label=[Windows.Controls.TextBlock]::new();$label.Text=$sheetFaces[$key];$label.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI Semibold');$label.FontSize=12.5
  $label.Foreground=Label-Brush $groundHex;$label.HorizontalAlignment='Center';$label.Margin=[Windows.Thickness]::new(0,10,0,0)
  [void]$cell.Children.Add($m);[void]$cell.Children.Add($label);[void]$grid.Children.Add($cell)
 }
 Save-Sheet $grid $sheetInfo[0] 2
}
$rows=@(@('Mochi','Default'),@('HojichaMochi','Hojicha'),@('MatchaMochi','Matcha'),@('Mochi','KuroNight'),@('Kuro','KuroNight'),@('Shiro','Default'),@('Shiro','Matcha'),@('Mochi','Matcha'),@('HojichaMochi','Matcha'))
$stack=[Windows.Controls.StackPanel]::new();$stack.Background=[Windows.Media.Brushes]::Black
foreach($row in $rows){
 $line=[Windows.Controls.StackPanel]::new();$line.Orientation='Horizontal';$line.Background=[Windows.Media.BrushConverter]::new().ConvertFromString($grounds[$row[1]])
 $title=[Windows.Controls.TextBlock]::new();$title.Text="$($row[0])`n$($row[1])";$title.Width=80;$title.VerticalAlignment='Center';$title.Margin=[Windows.Thickness]::new(8,0,0,0);$title.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI Semibold');$title.FontSize=12.5;$title.Foreground=Label-Brush $grounds[$row[1]]
 [void]$line.Children.Add($title)
 foreach($mood in 'Music','Focused','Worried','Meeting'){
  $m=New-Mascot $row[1] $row[0];$m.Width=96;$m.Height=83;$m.Margin=[Windows.Thickness]::new(14,22,14,14);$m.Mood=[Enum]::Parse($moodType,$mood)
  [void]$line.Children.Add($m)
 }
 [void]$stack.Children.Add($line)
}
Save-Sheet $stack 'headsets.png' 2
# mascots-on-themes.png: every mascot (rows) on every theme's ground (columns), with the Idle, Music and Angry faces.
$matrix=[Windows.Controls.StackPanel]::new();$matrix.Background=[Windows.Media.Brushes]::Black
foreach($kindName in 'Mochi','MatchaMochi','HojichaMochi','Kuro','Shiro'){
 $line=[Windows.Controls.StackPanel]::new();$line.Orientation='Horizontal'
 $title=[Windows.Controls.TextBlock]::new();$title.Text=$kindName;$title.Width=110;$title.VerticalAlignment='Center';$title.Margin=[Windows.Thickness]::new(8,0,0,0);$title.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI Semibold');$title.FontSize=12.5;$title.Foreground=[Windows.Media.Brushes]::White
 [void]$line.Children.Add($title)
 foreach($groundName in $grounds.Keys){
  $cellBox=[Windows.Controls.StackPanel]::new();$cellBox.Orientation='Horizontal';$cellBox.Background=[Windows.Media.BrushConverter]::new().ConvertFromString($grounds[$groundName])
  foreach($mood in 'Idle','Music','Angry'){
   $m=New-Mascot $groundName $kindName;$m.Width=64;$m.Height=56;$m.Margin=[Windows.Thickness]::new(10,16,10,10);$m.Mood=[Enum]::Parse($moodType,$mood)
   [void]$cellBox.Children.Add($m)
  }
  [void]$line.Children.Add($cellBox)
 }
 [void]$matrix.Children.Add($line)
}
Save-Sheet $matrix 'mascots-on-themes.png' 2
$checks++

# ---- The status badge (MascotBadge): the meaning that used to float above the head as a thin prop sits in one disc in the corner. For every
# mascot and face the badge must exist exactly when the table says, carry the table's colours, stay in its corner (at most 0.6 unit right
# of the box, 2.1 above it), stay clear of the eyes; and at the capsule size (22 x 19) on every ground at 100/125/150/200% it must be at least
# 7 px across and stand out from what it covers by at least 3:1 (WCAG non-text contrast).
$badgeType=$core.GetType('MochiDock.Core.MascotBadge')
function Badge-For($moodName){return $badgeType.GetMethod('For').Invoke($null,@([Enum]::Parse($moodType,$moodName)))}
$badgeMoods=@($allMoods|Where-Object{$null -ne (Badge-For $_)})
if($badgeMoods.Count -ne 18){throw "Expected 18 faces with a badge, found $($badgeMoods.Count): $($badgeMoods -join ', ')"}
$eyeNames='LeftPillEye','RightPillEye','LeftHappyEye','RightHappyEye','LeftSleepyEye','RightSleepyEye','LeftDetermEye','RightDetermEye','LeftFlatEye','RightFlatEye','LeftDroopEye','RightDroopEye','LeftSpiralEye','RightSpiralEye','LeftWideEye','RightWideEye','LeftRoundEye','RightRoundEye','LeftLidEye','RightLidEye','StarEyes','CoinEyes','Sunglasses'
function Part-Bounds($element,$root){
 # shapes: the bounds of what they draw (a Path's ActualWidth reaches back to the origin); panels: their size
 if($element -is [Windows.Shapes.Shape]){return $element.TransformToAncestor($root).TransformBounds($element.RenderedGeometry.Bounds)}
 return $element.TransformToAncestor($root).TransformBounds([Windows.Rect]::new(0,0,$element.ActualWidth,$element.ActualHeight))
}
function Eye-Bounds($m,$root,$name){
 $e=$m.FindName($name)
 if($e -is [Windows.Controls.Canvas]){
  $union=[Windows.Rect]::Empty
  foreach($c in $e.Children){$union=[Windows.Rect]::Union($union,(Part-Bounds $c $root))}
  return $union
 }
 return Part-Bounds $e $root
}foreach($kindName in 'Mochi','MatchaMochi','HojichaMochi','Kuro','Shiro'){
 $m=New-Mascot 'Default' $kindName;$m.Width=30;$m.Height=26;$m.Measure([Windows.Size]::new(60,60));$m.Arrange([Windows.Rect]::new(0,0,30,26));$m.UpdateLayout()
 $root=$m.FindName('RootGrid')
 foreach($mood in $allMoods){
  $m.Mood=[Enum]::Parse($moodType,$mood);$m.UpdateLayout()
  $face=$m.FindName('FaceTransform');$face.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$face.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null)
  $spec=Badge-For $mood
  if((Visible $m 'StatusBadge') -ne ($null -ne $spec)){throw "${kindName}: the badge should be $(if($spec){'shown'}else{'hidden'}) in $mood"}
  if($null -eq $spec){continue}
  $pos=$m.FindName('BadgePos');$cx=$pos.X;$cy=$pos.Y;$outer=4.9
  if($cx+$outer -gt 30.6 -or $cy-$outer -lt -2.1 -or $cx-$outer -lt 17){throw ("{0}: the {1} badge is out of its corner: centre {2},{3}" -f $kindName,$mood,$cx,$cy)}
  if((Hex-Of $m.FindName('BadgeDisc').Fill.Color) -ne $spec.Fill -or (Hex-Of $m.FindName('BadgeGlyph').Fill.Color) -ne $spec.Ink){throw "${kindName}: the $mood badge has the wrong colours"}
  if((Hex-Of $m.FindName('BadgeRing').Fill.Color) -ne $badgeType.GetMethod('RingFor',[type[]]@([string],[string])).Invoke($null,@('Default',$spec.Fill))){throw "${kindName}: the $mood badge ring is not the theme's ring"}
  $glyph=$m.FindName('BadgeGlyph').Data
  if($glyph.IsEmpty() -or $glyph.Bounds.Width -lt 1.5 -or $glyph.Bounds.Right -gt 3.6 -or $glyph.Bounds.Left -lt -3.6 -or $glyph.Bounds.Top -lt -3.6 -or $glyph.Bounds.Bottom -gt 3.6){throw "${kindName}: the $mood glyph is missing or does not fit the disc ($($glyph.Bounds))"}
  # the badge must not cover an eye: its outer circle stays clear of every eye shape the face shows
  foreach($eye in $eyeNames){
   if(!(Visible $m $eye)){continue}
   $b=Eye-Bounds $m $root $eye
   if($b.IsEmpty -or $b.Width -le 0){continue}
   $dx=[Math]::Max([Math]::Max($b.Left-$cx,0),$cx-$b.Right);$dy=[Math]::Max([Math]::Max($b.Top-$cy,0),$cy-$b.Bottom)
   if([Math]::Sqrt($dx*$dx+$dy*$dy) -lt $outer-0.3){throw ("{0}: the {1} badge covers the {2} (distance {3:N2} < {4})" -f $kindName,$mood,$eye,[Math]::Sqrt($dx*$dx+$dy*$dy),($outer-0.3))}
  }
  $checks++
 }
}
# a face without a badge never shows one, on any mascot (checked above); now the pixels at the capsule size
$badgeMetrics=@()
foreach($groundName in $grounds.Keys){
 $groundColor=[Windows.Media.ColorConverter]::ConvertFromString($grounds[$groundName])
 foreach($kindName in 'Mochi','MatchaMochi','HojichaMochi','Kuro','Shiro'){
  foreach($scale in 1,1.25,1.5,2){
   $m=New-Mascot $groundName $kindName;$m.Width=22;$m.Height=19
   $native,$surface=New-Host $m $scale ([Windows.Media.SolidColorBrush]::new($groundColor))
   try{
    $surface.Measure([Windows.Size]::new(200,200));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout()
    $w=[int][math]::Ceiling($surface.ActualWidth*$scale);$h=[int][math]::Ceiling($surface.ActualHeight*$scale);$stride=$w*4
    $bubble=$m.FindName('BodyBounceTransform');$badge=$m.FindName('StatusBadge')
    foreach($mood in $badgeMoods){
     $m.Mood=[Enum]::Parse($moodType,$mood)
     $bubble.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$bubble.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null);$bubble.X=0;$bubble.Y=0
     $face=$m.FindName('FaceTransform');$face.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$face.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null)
     $surface.UpdateLayout()
     $with=[Windows.Media.Imaging.RenderTargetBitmap]::new($w,$h,96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$with.Render($surface)
     $badge.Visibility=[Windows.Visibility]::Collapsed;$surface.UpdateLayout()
     $without=[Windows.Media.Imaging.RenderTargetBitmap]::new($w,$h,96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$without.Render($surface)
     $badge.Visibility=[Windows.Visibility]::Visible
     $a=[byte[]]::new($stride*$h);$b=[byte[]]::new($stride*$h);$with.CopyPixels($a,$stride,0);$without.CopyPixels($b,$stride,0)
     $minX=$w;$maxX=-1;$minY=$h;$maxY=-1;$best=1.0
     for($y=0;$y -lt $h;$y++){for($x=0;$x -lt $w;$x++){
      $i=$y*$stride+$x*4
      if([Math]::Abs($a[$i]-$b[$i])+[Math]::Abs($a[$i+1]-$b[$i+1])+[Math]::Abs($a[$i+2]-$b[$i+2]) -gt 30){
       if($x -lt $minX){$minX=$x};if($x -gt $maxX){$maxX=$x};if($y -lt $minY){$minY=$y};if($y -gt $maxY){$maxY=$y}
       $c=Contrast (Lum $a[$i+2] $a[$i+1] $a[$i]) (Lum $b[$i+2] $b[$i+1] $b[$i]);if($c -gt $best){$best=$c}
      }}}
     $across=$maxX-$minX+1
     $badgeMetrics+=("{0} on {1} ground, {2} {3}%: {4} px across, {5:N1}:1 against what it covers" -f $kindName,$groundName,$mood,($scale*100),$across,$best)
     if($across -lt 6.9*$scale){throw ("{0} on the {1} ground: the {2} badge is only {3} px across at {4}% (needs {5:N1})" -f $kindName,$groundName,$mood,$across,($scale*100),(6.9*$scale))}
     if($best -lt 3){throw ("{0} on the {1} ground: the {2} badge is only {3:N2}:1 against what it covers at {4}%" -f $kindName,$groundName,$mood,$best,($scale*100))}
     $checks++
    }
   } finally { $native.Dispose() }
  }
 }
}
$badgeMetrics|Set-Content (Join-Path $output 'badge-contrast.txt')

# ---- Look-alike faces: with the badges hidden, the faces that used to share their eyes (Idle, Thinking, Notice, Bored and Sleeping, Waking,
# Prayer) must still differ at the capsule size. Rendered at 22 x 19 and 200% on the Default ground; each pair needs at least 12 differing
# pixels (3 at 100%).
function Face-Pixels($mood){
 $m=New-Mascot 'Default' 'Mochi';$m.Width=22;$m.Height=19;$m.Mood=[Enum]::Parse($moodType,$mood)
 $m.FindName('StatusBadge').Visibility=[Windows.Visibility]::Collapsed
 foreach($t in 'BodyBounceTransform','FaceTransform'){$tr=$m.FindName($t);$tr.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$tr.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null)}
 $m.FindName('BodyBounceTransform').X=0;$m.FindName('BodyBounceTransform').Y=0
 $img=Render-Pixels $m 2 ([Windows.Media.Color]::FromRgb(0,0,0))
 return $img
}
$faceCache=@{}
$lookAlikes=@()
foreach($pair in @(@('Idle','Waiting'),@('Idle','Alert'),@('Idle','Bored'),@('Waiting','Alert'),@('Waiting','Bored'),@('Alert','Bored'),@('Sleeping','Waking'),@('Sleeping','Praying'),@('Waking','Praying'))){
 foreach($n in $pair){if(!$faceCache.ContainsKey($n)){$faceCache[$n]=Face-Pixels $n}}
 $pa=$faceCache[$pair[0]];$pb=$faceCache[$pair[1]];$diff=0
 for($i=0;$i -lt $pa.Pixels.Length;$i+=4){if([Math]::Abs($pa.Pixels[$i]-$pb.Pixels[$i])+[Math]::Abs($pa.Pixels[$i+1]-$pb.Pixels[$i+1])+[Math]::Abs($pa.Pixels[$i+2]-$pb.Pixels[$i+2]) -gt 60){$diff++}}
 $lookAlikes+=("{0} vs {1}: {2} px differ" -f $pair[0],$pair[1],$diff)
 if($diff -lt 12){throw ("{0} and {1} look alike at the capsule size (only {2} pixels differ at 200%)" -f $pair[0],$pair[1],$diff)}
 $checks++
}
$lookAlikes|Set-Content (Join-Path $output 'lookalike.txt')

# ---- moods-capsule-size.png: every face at the real capsule size (22 x 19 at 100%), one row per mascot and ground, magnified x4 with
# nearest-neighbour so the pixels can be judged by eye. The faces run in the order of the README sheet (left to right, then the rows).
$sizeRows=@(@('Mochi','Default'),@('HojichaMochi','Hojicha'),@('MatchaMochi','Matcha'),@('Kuro','KuroNight'),@('Shiro','Default'),@('Shiro','Matcha'),@('Mochi','Matcha'),@('HojichaMochi','Matcha'),@('Kuro','Default'))
$sizeStack=[Windows.Controls.StackPanel]::new();$sizeStack.Background=[Windows.Media.Brushes]::Black
foreach($row in $sizeRows){
 $line=[Windows.Controls.StackPanel]::new();$line.Orientation='Horizontal';$line.Background=[Windows.Media.BrushConverter]::new().ConvertFromString($grounds[$row[1]])
 foreach($key in $sheetFaces.Keys){
  if($key -eq 'Turning'){continue}
  $m=New-Mascot $row[1] $row[0];$m.Width=22;$m.Height=19;$m.Margin=[Windows.Thickness]::new(5,7,5,5);$m.Mood=[Enum]::Parse($moodType,$key)
  foreach($t in 'BodyBounceTransform','FaceTransform'){$tr=$m.FindName($t);$tr.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$tr.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null)}
  $m.FindName('BodyBounceTransform').X=0;$m.FindName('BodyBounceTransform').Y=0
  [void]$line.Children.Add($m)
 }
 [void]$sizeStack.Children.Add($line)
}
$native,$sizeHost=New-Host $sizeStack 1 $sizeStack.Background
try{
 $sizeHost.Measure([Windows.Size]::new(4000,2000));$sizeHost.Arrange([Windows.Rect]::new(0,0,$sizeHost.DesiredSize.Width,$sizeHost.DesiredSize.Height));$sizeHost.UpdateLayout()
 $small=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$sizeHost.ActualWidth,[int]$sizeHost.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32);$small.Render($sizeHost)
} finally { $native.Dispose() }
$image=[Windows.Controls.Image]::new();$image.Source=$small;$image.Width=$small.PixelWidth*4;$image.Height=$small.PixelHeight*4;$image.Stretch='Fill'
[Windows.Media.RenderOptions]::SetBitmapScalingMode($image,[Windows.Media.BitmapScalingMode]::NearestNeighbor)
$image.Measure([Windows.Size]::new($image.Width,$image.Height));$image.Arrange([Windows.Rect]::new(0,0,$image.Width,$image.Height));$image.UpdateLayout()
$big=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$image.Width,[int]$image.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$big.Render($image)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($big))
$stream=[IO.File]::Create((Join-Path $output 'moods-capsule-size.png'));$encoder.Save($stream);$stream.Dispose()
$checks++

# ---- Winking closes one eye only, and the Music face sways sideways while it bounces.
$wink=[Activator]::CreateInstance($mascotType)
$wink.PerformWink()
if(!$wink.FindName('RightEyeBlinkTransform').HasAnimatedProperties){throw 'A wink did not move the right eye'}
if($wink.FindName('LeftEyeBlinkTransform').HasAnimatedProperties){throw 'A wink moved the left eye as well'}
$dance=[Activator]::CreateInstance($mascotType);$dance.Mood=[Enum]::Parse($moodType,'Music')
if(!$dance.FindName('BodyBounceTransform').HasAnimatedProperties){throw 'The Music face does not move'}
$checks++
Write-Output "Verified $checks mascot checks: layers per mood at four scales, plain moods restore the colours, extras stay near the body, the eyes of every face are centred and mirrored (measured from pixels), face strokes stay visible at 22 x 19, Kuro keeps the charcoal body with light ink, Shiro the white body, every mascot (Mochi, Matcha Mochi, Hojicha Mochi, Kuro, Shiro) stays visible on every theme ground, and the headset matches each mascot. Strips: $output"
