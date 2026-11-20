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

$extras=@('LeftDetermEye','RightDetermEye','LeftFlatEye','RightFlatEye','LeftDroopEye','RightDroopEye','LeftSpiralEye','RightSpiralEye','SteamLeft','SteamRight','HeatLines','IcicleGroup','StarsGroup','BatteryGroup','TearDrop','TearDropRight','AngerMark','StartleLines','SparkleGroup','CoffeeGroup','ExclaimGroup','ConfettiGroup','SighGroup','WorryDrop','StarEyes','CoinEyes','Sunglasses','BoltOrbit','CoinFloat','UmbrellaGroup','RainGroup','ScarfGroup','SnowGroup','CloudGroup','SunGroup','TimerBadge')
# Mochi has no mouth and no eyebrows: none of these layers may exist (they made the face busy).
$banned=@('BrowLeft','BrowRight','MouthFrown','MouthPant','MouthChatter','MouthWave','MouthFlat','MouthGrin','MouthYawn','MouthSmile')
$plain=@('Idle','Waiting','Bored','Music','Alert','Sleeping','Happy')
# What must be visible, besides the pill eyes, in each extra mood.
$expected=@{
 Angry=@('LeftDetermEye','RightDetermEye','AngerMark','SteamLeft','SteamRight');
 Hot=@('LeftFlatEye','RightFlatEye','HeatLines');
 Freezing=@('IcicleGroup');
 Dizzy=@('LeftSpiralEye','RightSpiralEye','StarsGroup');
 Drained=@('LeftDroopEye','RightDroopEye','BatteryGroup');
 Focused=@('LeftDetermEye','RightDetermEye');
 Victory=@('SparkleGroup');
 Defeat=@('LeftDroopEye','RightDroopEye','TearDrop','TearDropRight');
 Worried=@('WorryDrop');
 BreakTime=@('CoffeeGroup');
 Excited=@('ExclaimGroup');
 Surprised=@('StartleLines');
 Cheering=@('ConfettiGroup');
 Praying=@();
 Bored=@('SighGroup');
 Waking=@();
 Electric=@('StarEyes','BoltOrbit');
 Money=@('CoinEyes','CoinFloat');
 MoneyDown=@('CoinEyes','CoinFloat');
 Sunny=@('Sunglasses','SunGroup');
 Rainy=@('UmbrellaGroup','RainGroup');
 Snowy=@('ScarfGroup','SnowGroup');
 Cloudy=@('CloudGroup');
 Studying=@('LeftDetermEye','RightDetermEye','TimerBadge')
}
$pillEyes=@{Angry=$false;Hot=$false;Freezing=$true;Dizzy=$false;Drained=$false;Focused=$false;Victory=$false;Defeat=$false;Worried=$true;BreakTime=$false;Excited=$true;Surprised=$true;Cheering=$false;Praying=$false;Bored=$true;Waking=$false;Electric=$false;Money=$false;MoneyDown=$false;Sunny=$false;Rainy=$true;Snowy=$true;Cloudy=$true;Studying=$false}
$sweat=@{Hot=$true}
$order=@('Idle','Angry','Hot','Freezing','Dizzy','Drained','Music','Hot','Idle','Dizzy','Alert','Drained','Sleeping','Angry','Happy','Freezing','Focused','Waiting','Angry','Bored','Victory','Defeat','Angry','Victory','Idle','Defeat','Focused','Worried','BreakTime','Worried','Angry','BreakTime','Focused','Music','Worried','Music','Excited','Surprised','Cheering','Praying','Angry','Surprised','Idle','Praying','Idle','Bored','Waking','Bored','Angry','Waking','Idle','Electric','Money','Electric','MoneyDown','Sunny','Rainy','Snowy','Cloudy','Studying','Electric','Idle','Rainy','Dizzy','Sunny','Music','Studying','Focused','Money','Idle')

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
# The click heart and the sleeping Zzz must be vector shapes: WPF text cannot draw colour emoji, so an emoji would be black and white.
$probe=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'))
foreach($gone in $banned){if($null -ne $probe.FindName($gone)){throw "$gone exists again: Mochi has no mouth and no eyebrows"}}
# The ear cups must stay inside the 30 unit body width: the capsule cuts everything wider, which showed as a cropped left cup.
foreach($cup in 'EarCupLeft','EarCupRight'){$e=$probe.FindName($cup);$left=[Windows.Controls.Canvas]::GetLeft($e);if($left -lt 0 -or ($left+$e.Width) -gt 30){throw "$cup sticks out of the 30 unit body ($left + $($e.Width))"};$checks++}
foreach($name in 'FloatingHeart','SleepyParticle'){
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
   $want=if($expected.ContainsKey($name)){$expected[$name]}else{@()}
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
     if($mascot.FindName('HeadbandArc').Stroke.Color.ToString() -ne '#FFE5484D'){throw 'The gaming headset is not red'}
    }
    if($name -eq 'Worried' -and !(Visible $mascot 'HeadphonesLayer')){throw 'Worried needs the headset'}
    if($mascot.FindName('FlipScaleTransform').ScaleX -ne 1){throw "Mochi was left turned in mood $name"}
    foreach($part in 'FaceLayer','FaceGrid','LeftCheek','FaceExtras','ExtrasLayer'){if($mascot.FindName($part).Opacity -ne 1){throw "The $part of mood $name stayed hidden (back of a turn)"}}
    if($mascot.FindName('FaceTurnScale').ScaleX -ne 1 -or $mascot.FindName('FaceTurnShift').X -ne 0){throw "The face of mood $name stayed turned"}
    foreach($wind in 'WindBack','WindFront'){if((Visible $mascot $wind)){throw "The turn's wind stayed on in mood $name"}}
    if($name -eq 'Electric' -and $mascot.FindName('BodyGradStopTop').Color.ToString() -eq '#FFEDEDEF' -and !$mascot.FindName('BodyGradStopTop').HasAnimatedProperties){throw 'Electric lost its charged tint'}
    if($name -eq 'Waking' -and !(Visible $mascot 'LeftSleepyEye')){throw 'Waking is missing its closed eyes'}
    if($name -eq 'Cheering' -and !(Visible $mascot 'LeftHappyEye')){throw 'Cheering is missing its happy eyes'}
    if($name -eq 'Praying' -and !(Visible $mascot 'LeftSleepyEye')){throw 'Praying is missing its closed eyes'}
    if($name -eq 'BreakTime' -and !(Visible $mascot 'LeftSleepyEye')){throw 'Break time is missing its closed eyes'}
    if($name -eq 'Worried' -and !(Visible $mascot 'WorryDrop')){throw 'Worried is missing its sweat drop'}
    if($name -eq 'Victory' -and !(Visible $mascot 'LeftHappyEye')){throw 'Victory is missing its happy eyes'}
    if($name -eq 'Angry' -and $mascot.FindName('AngerMark').Opacity -ne 1 -and !$mascot.FindName('AngerMark').HasAnimatedProperties){throw 'The anger mark stayed faded'}
    if($name -eq 'Defeat' -and !(Visible $mascot 'TearDropRight')){throw 'Defeat is missing its second tear'}
    if((Visible $mascot 'SweatDrop') -ne [bool]$sweat[$name] -and !($name -eq 'Hot')){throw "Sweat drop wrong in mood $name"}
    if($name -eq 'Hot' -and !(Visible $mascot 'SweatDrop')){throw 'Sweat drop is missing in mood Hot'}
   }else{
    if(Visible $mascot 'SweatDrop'){throw "Sweat drop leaked into plain mood $name"}
    if($name -eq 'Music' -and $mascot.FindName('HeadbandArc').Stroke.Color.ToString() -ne '#FF0A84FF'){throw 'The music headset did not return to blue after a gaming face'}
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
$all=@('Turning','Idle','Music','Alert','Sleeping','Focused','Happy','Angry','Hot','Freezing','Dizzy','Drained','Victory','Defeat','Worried','BreakTime','Excited','Surprised','Cheering','Praying','Bored','Waking','Electric','Money','MoneyDown','Sunny','Rainy','Snowy','Cloudy','Studying','Petted','Zzz')
foreach($style in @(@('black',[Windows.Media.Brushes]::Black),@('cream',[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xF3,0xEF,0xE0))))){
 $panel=[Windows.Controls.StackPanel]::new();$panel.Orientation=[Windows.Controls.Orientation]::Horizontal;$panel.Background=$style[1]
 foreach($name in $all){$m=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'));$m.Width=150;$m.Height=130;$m.Margin=[Windows.Thickness]::new(8,60,16,12);$mood=switch($name){'Petted'{'Idle'}'Zzz'{'Sleeping'}'Turning'{'Idle'}default{$name}}
  $m.Mood=[Enum]::Parse($moodType,$mood)
  # Headless renders do not run animations, so show the heart and the Zzz at a mid-animation state by hand.
  if($name -eq 'Petted'){$heart=$m.FindName('FloatingHeart');$heart.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$heart.Opacity=1;$m.FindName('HeartTranslate').Y=-6}
  if($name -eq 'Turning'){Show-Turn $m 600}
  if($name -eq 'Zzz'){$zzz=$m.FindName('SleepyParticle');$zzz.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$zzz.Opacity=0.95;$m.FindName('SleepyTranslate').Y=-2}
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
   for($px=[int](4*6+$origin);$px -le [int](26*6+$origin);$px++){
    $i=$py*$stride+$px*4;$b=$pixels[$i];$g=$pixels[$i+1];$r=$pixels[$i+2]
    if($r -lt 80 -and $g -lt 80 -and $b -lt 80){ # the dark ink of the eyes (brows are dark red, ear cups blue: not counted)
     $dx=($px-$origin+.5)/6;$dy=($py-$origin+.5)/6
     $side=if($dx -lt 15){'L'}else{'R'}
     $sums[$side][0]+=$dx;$sums[$side][1]+=$dy;$sums[$side][2]++
    }
   }
  }
  return $sums
 } finally { $native.Dispose() }
}
# Where the eyes' vertical centre must lie (DIPs): the pill, flat, droopy and spiral eyes are centred on 12.5; the happy and sleepy
# arcs sit a little lower by design.
$band=@{Idle=@(11.9,13.1);Music=@(12.6,14.6);Alert=@(11.9,13.1);Sleeping=@(12.4,14.2);Focused=@(12.0,13.4);Happy=@(12.6,14.6);Angry=@(11.9,13.1);Hot=@(11.9,13.1);Freezing=@(11.9,13.1);Dizzy=@(11.9,13.1);Drained=@(11.9,13.1);Victory=@(12.6,14.6);Defeat=@(11.9,13.1);Worried=@(11.9,13.1);BreakTime=@(12.4,14.2);Excited=@(11.9,13.1);Surprised=@(11.9,13.1);Cheering=@(12.6,14.6);Praying=@(12.4,14.2);Bored=@(13.5,14.8);Waking=@(12.4,14.2);Electric=@(11.9,13.1);Sunny=@(11.9,13.3);Rainy=@(11.9,13.1);Snowy=@(11.9,13.1);Cloudy=@(11.9,13.1);Studying=@(12.0,13.4)}
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
foreach($name in 'Idle','Waiting','Music','Alert','Sleeping','Happy','Money','Snowy','Cloudy','Studying'){
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
$panel=[Windows.Controls.StackPanel]::new();$panel.Orientation=[Windows.Controls.Orientation]::Horizontal;$panel.Background=[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xE4,0xEC,0xD8))
foreach($name in 'Idle','Music','Sleeping','Happy','Angry','Hot','Freezing','Dizzy','Drained','Worried','Focused','Bored','Waking','Defeat','Victory','Excited','Surprised','Cheering','Praying','Electric','Money','MoneyDown','Sunny','Rainy','Snowy','Cloudy','Studying','Turning'){
 $m=[Activator]::CreateInstance($mascotClass);$m.Width=150;$m.Height=130;$m.Margin=[Windows.Thickness]::new(8,60,16,12);$m.ApplyTheme($true);$m.Mood=[Enum]::Parse($moodType,$(if($name -eq 'Turning'){'Idle'}else{$name}));if($name -eq 'Turning'){Show-Turn $m 600};$panel.Children.Add($m)|Out-Null}
$parts=New-Host $panel 1 ([Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xE4,0xEC,0xD8)))
$sheet=$parts[1];$sheet.Measure([Windows.Size]::new(8000,400));$sheet.Arrange([Windows.Rect]::new(0,0,$sheet.DesiredSize.Width,$sheet.DesiredSize.Height));$sheet.UpdateLayout()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($sheet.ActualWidth*2),[int]($sheet.ActualHeight*2),192,192,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($sheet)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream=[IO.File]::Create((Join-Path $output 'moods-matcha.png'));$encoder.Save($stream);$stream.Dispose()
# ---- Winking closes one eye only, and the Music face sways sideways while it bounces.
$wink=[Activator]::CreateInstance($mascotType)
$wink.PerformWink()
if(!$wink.FindName('RightEyeBlinkTransform').HasAnimatedProperties){throw 'A wink did not move the right eye'}
if($wink.FindName('LeftEyeBlinkTransform').HasAnimatedProperties){throw 'A wink moved the left eye as well'}
$dance=[Activator]::CreateInstance($mascotType);$dance.Mood=[Enum]::Parse($moodType,'Music')
if(!$dance.FindName('BodyBounceTransform').HasAnimatedProperties){throw 'The Music face does not move'}
$checks++
Write-Output "Verified $checks mascot checks: layers per mood at four scales, plain moods restore the colours, extras stay near the body, the eyes of every face are centred and mirrored (measured from pixels), face strokes stay visible at 22 x 19. Strips: $output"
