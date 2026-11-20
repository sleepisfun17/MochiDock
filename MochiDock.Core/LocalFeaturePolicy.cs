namespace MochiDock.Core;
public static class LocalFeaturePolicy{
 /// <summary>
 /// Whether the screen the user sees is on. Windows' console display state describes the PC's physical monitors; in a Remote Desktop
 /// session those are often off while the user is busy on the remote screen, so a remote session counts as awake (otherwise FX,
 /// hardware sampling and Mochi's faces would pause for the whole remote session).
 /// </summary>
 public static bool DisplayAwake(bool consoleDisplayOn,bool remoteSession)=>remoteSession||consoleDisplayOn;
 public static int NextTab(int active,int count,bool forward)=>count<=0?-1:(Math.Max(0,active)+(forward?1:count-1))%count;
 public static bool SampleHardware(bool locked,bool awake,bool visible,bool pinned,bool game,bool alerts)=>!locked&&awake&&(visible||pinned||game||alerts);
 public static bool Hide(bool enabled,bool fullscreen,bool keepForGaming=false)=>enabled&&fullscreen&&!keepForGaming;
 public static string Battery(byte flag,byte percent,byte ac,byte saver)=>flag==255?"Battery —":(flag&128)!=0?"No battery installed":$"Battery {(percent<=100?percent.ToString()+"%":"—")} • {((flag&8)!=0?"Charging":ac==1?"On AC power":ac==0?"On battery":"Power source —")} • Battery saver: {(saver==1?"On":saver==0?"Off":"—")}";
}
