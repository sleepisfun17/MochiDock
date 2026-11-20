using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace MochiDock.Services;
public sealed class PowerNotifications : IDisposable {
 readonly HwndSource source;readonly List<IntPtr> registrations=new();
 public event Action<bool>? DisplayChanged;public event Action? PowerChanged;
 [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr hwnd,ref Guid guid,int flags);
 [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
 public PowerNotifications(IntPtr hwnd){source=HwndSource.FromHwnd(hwnd);source.AddHook(Hook);foreach(var id in new[]{"6fe69556-704a-47a0-8f24-c28d936fda47","a7ad8041-b45a-4cae-87a3-eecbb468a9e1","5d3e9a59-e9d5-4b00-a6bd-ff34ff516548","e00958c0-c213-4ace-ac77-fecced2eeea5"}){var guid=new Guid(id);var h=RegisterPowerSettingNotification(hwnd,ref guid,0);if(h!=IntPtr.Zero)registrations.Add(h);}}
 IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x218&&w.ToInt32()==0x8013){PowerChanged?.Invoke();var guid=Marshal.PtrToStructure<Guid>(l);if(guid==new Guid("6fe69556-704a-47a0-8f24-c28d936fda47"))DisplayChanged?.Invoke(Marshal.ReadInt32(l,20)!=0);}return IntPtr.Zero;}
 public void Dispose(){if(!source.IsDisposed)source.RemoveHook(Hook);foreach(var h in registrations)UnregisterPowerSettingNotification(h);registrations.Clear();}
}