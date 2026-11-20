namespace MochiDock.Core;
public readonly record struct AdapterCounter(string Id,long Received,long Sent);
public sealed class NetworkHistory {
 readonly Dictionary<string,(long Rx,long Tx)> previous=new();
 readonly Queue<(double Time,double Down,double Up)> samples=new();
 double? last;
 public long Received {get;private set;} public long Sent {get;private set;}
 public double Down {get;private set;} public double Up {get;private set;}
 public double PeakDown => samples.Count==0?0:samples.Max(s=>s.Down);
 public double PeakUp => samples.Count==0?0:samples.Max(s=>s.Up);
 public void Update(double now,IEnumerable<AdapterCounter> counters) {
  long rx=0,tx=0; var current=new Dictionary<string,(long Rx,long Tx)>();
  foreach(var c in counters) { current[c.Id]=(c.Received,c.Sent); if(previous.TryGetValue(c.Id,out var old)) {rx+=Math.Max(0,c.Received-old.Rx);tx+=Math.Max(0,c.Sent-old.Tx);} }
  previous.Clear();foreach(var c in current)previous[c.Key]=c.Value;
  Received+=rx;Sent+=tx;
  var interval=last.HasValue?now-last.Value:0;Down=interval>0?rx/interval:0;Up=interval>0?tx/interval:0;last=now;
  samples.Enqueue((now,Down,Up));while(samples.Count>0&&samples.Peek().Time<=now-60)samples.Dequeue();
 }
 public void ResetBaseline(){ previous.Clear();last=null;samples.Clear();Down=Up=0; }
}