namespace MochiDock.Core;
public static class LocalFeaturePolicy{
 public static int NextTab(int active,int count,bool forward)=>count<=0?-1:(Math.Max(0,active)+(forward?1:count-1))%count;
 public static bool SampleHardware(bool locked,bool awake,bool visible,bool pinned,bool game,bool alerts)=>!locked&&awake&&(visible||pinned||game||alerts);
 public static bool Hide(bool enabled,bool fullscreen,bool keepForGaming=false)=>enabled&&fullscreen&&!keepForGaming;
 public static string Battery(byte flag,byte percent,byte ac,byte saver)=>flag==255?"Battery —":(flag&128)!=0?"No battery installed":$"Battery {(percent<=100?percent.ToString()+"%":"—")} • {((flag&8)!=0?"Charging":ac==1?"On AC power":ac==0?"On battery":"Power source —")} • Battery saver: {(saver==1?"On":saver==0?"Off":"—")}";
}
