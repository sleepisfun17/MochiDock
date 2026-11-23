using System.Runtime.InteropServices;
using MochiDock.Services;
using Endpoint = MochiDock.Services.AudioEndpointService.IAudioEndpointVolume;
using EndpointCallback = MochiDock.Services.AudioEndpointService.IAudioEndpointVolumeCallback;
using Enumerator = MochiDock.Services.AudioEndpointService.IMMDeviceEnumerator;
using NotificationClient = MochiDock.Services.AudioEndpointService.IMMNotificationClient;

public class AudioEndpointServiceTests
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);   // AUDCLNT_E_DEVICE_INVALIDATED

    // A managed stand-in for one output's IAudioEndpointVolume.
    private sealed class FakeEndpoint(float level = 0.5f) : Endpoint
    {
        public float Level = level;
        public int ReadResult;                 // HRESULT returned by GetMasterVolumeLevelScalar
        public int Reads, Sets, Registers, Unregisters;
        public float LastSet = -1f;
        public Action? OnUnregister;
        public int RegisterControlChangeNotify(EndpointCallback pNotify) { Interlocked.Increment(ref Registers); return 0; }
        public int UnregisterControlChangeNotify(EndpointCallback pNotify) { Interlocked.Increment(ref Unregisters); OnUnregister?.Invoke(); return 0; }
        public int GetChannelCount(out uint pnChannelCount) { pnChannelCount = 2; return 0; }
        public int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext) => 0;
        public int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext)
        {
            Interlocked.Increment(ref Sets);
            if (ReadResult < 0) return ReadResult;
            LastSet = fLevel; Level = fLevel; return 0;
        }
        public int GetMasterVolumeLevel(out float pfLevelDB) { pfLevelDB = 0; return 0; }
        public int GetMasterVolumeLevelScalar(out float pfLevel) { Interlocked.Increment(ref Reads); pfLevel = Level; return ReadResult; }
        public int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext) => 0;
        public int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext) => 0;
        public int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB) { pfLevelDB = 0; return 0; }
        public int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel) { pfLevel = Level; return 0; }
        public int SetMute(bool bMute, ref Guid pguidEventContext) => 0;
        public int GetMute(out bool pbMute) { pbMute = false; return 0; }
        public int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount) { pnStep = 0; pnStepCount = 0; return 0; }
        public int VolumeStepUp(ref Guid pguidEventContext) => 0;
        public int VolumeStepDown(ref Guid pguidEventContext) => 0;
        public int QueryHardwareSupport(out uint pdwHardwareSupportMask) { pdwHardwareSupportMask = 0; return 0; }
        public int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB) { pflVolumeMindB = pflVolumeMaxdB = pflVolumeIncrementdB = 0; return 0; }
    }

    private sealed class FakeEnumerator : Enumerator
    {
        public int Calls;
        public int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out AudioEndpointService.IMMDeviceCollection ppDevices) { Interlocked.Increment(ref Calls); ppDevices = null!; return unchecked((int)0x80004001); }
        public int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out AudioEndpointService.IMMDevice ppEndpoint) { Interlocked.Increment(ref Calls); ppEndpoint = null!; return unchecked((int)0x80004001); }
        public int GetDevice(string pwstrId, out AudioEndpointService.IMMDevice ppDevice) { Interlocked.Increment(ref Calls); ppDevice = null!; return unchecked((int)0x80004001); }
        public int RegisterEndpointNotificationCallback(NotificationClient pClient) { Interlocked.Increment(ref Calls); return 0; }
        public int UnregisterEndpointNotificationCallback(NotificationClient pClient) { Interlocked.Increment(ref Calls); return 0; }
    }

    // The endpoints the factory hands out, one per build; every build is counted. Queued rebuilds are collected, not run.
    private sealed class Rig
    {
        public readonly List<FakeEndpoint> Endpoints = new();
        public readonly List<Action> Queued = new();
        public int Builds;
        public readonly FakeEnumerator Enumerator = new();
        public AudioEndpointService Service = null!;
        private readonly Func<int, FakeEndpoint> _make;

        public Rig(Func<int, FakeEndpoint>? make = null)
        {
            _make = make ?? (_ => new FakeEndpoint());
            Service = new AudioEndpointService(Enumerator, Build, work => { lock (Queued) Queued.Add(work); });
        }

        private Endpoint? Build()
        {
            int n = Interlocked.Increment(ref Builds);
            var e = _make(n);
            lock (Endpoints) Endpoints.Add(e);
            return e;
        }
    }

    [Fact]
    public void AFailingEndpointIsRebuiltOnceAndTheCallRetriedOnTheNewOne()
    {
        var rig = new Rig(n => n == 1 ? new FakeEndpoint(0.5f) { ReadResult = DeviceInvalidated } : new FakeEndpoint(0.30f));
        Assert.Equal(1, rig.Builds);

        float result = rig.Service.AdjustMasterVolume(10);

        Assert.Equal(2, rig.Builds);                       // exactly one rebuild
        Assert.Equal(40f, result);                         // 30 + 10 on the new output
        Assert.Equal(0.40f, rig.Endpoints[1].LastSet, 3);  // the new endpoint was changed...
        Assert.Equal(0, rig.Endpoints[0].Sets);            // ...the dead one never was
        Assert.Equal(1, rig.Endpoints[1].Registers);       // the new endpoint is listening for volume changes
    }

    [Fact]
    public void ASecondFailureGivesUpWithoutInventingAVolume()
    {
        var rig = new Rig(_ => new FakeEndpoint(0.5f) { ReadResult = DeviceInvalidated });
        rig.Service.CheckVolumeState();
        int builds = rig.Builds;

        float adjusted = rig.Service.AdjustMasterVolume(10);
        float read = rig.Service.GetMasterVolume();

        Assert.Equal(0f, adjusted);                        // no last known level: never a made-up 50
        Assert.Equal(0f, read);
        Assert.All(rig.Endpoints, e => Assert.Equal(0, e.Sets));   // nothing was "set" on a dead endpoint
        Assert.True(rig.Builds > builds);                  // it did try a rebuild (and only once per call)
    }

    [Fact]
    public void ALevelReadEarlierIsReturnedWhenTheEndpointFails()
    {
        var rig = new Rig(n => new FakeEndpoint(0.42f) { ReadResult = n == 1 ? 0 : DeviceInvalidated });
        rig.Service.CheckVolumeState();                    // reads 42 and remembers it
        rig.Endpoints[0].ReadResult = DeviceInvalidated;   // now the output goes away
        Assert.Equal(42f, rig.Service.GetMasterVolume());
        Assert.Equal(42f, rig.Service.AdjustMasterVolume(5));   // does nothing, returns the last known level
        Assert.All(rig.Endpoints, e => Assert.Equal(0, e.Sets));
    }

    [Fact]
    public void ADeviceNotificationDoesNoComWorkOnTheCallingThread()
    {
        var rig = new Rig();
        int builds = rig.Builds, reads = rig.Endpoints[0].Reads, enumeratorCalls = rig.Enumerator.Calls, unregisters = rig.Endpoints[0].Unregisters;

        rig.Service.NotificationClientForTests.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "new-device");

        Assert.Equal(builds, rig.Builds);                       // the factory was not called
        Assert.Equal(reads, rig.Endpoints[0].Reads);
        Assert.Equal(unregisters, rig.Endpoints[0].Unregisters);    // the old endpoint was not released in the callback
        Assert.Equal(enumeratorCalls, rig.Enumerator.Calls);
        Assert.Single(rig.Queued);                              // one rebuild is queued for a pool thread

        rig.Queued[0]();                                        // what the pool thread does
        Assert.Equal(builds + 1, rig.Builds);
    }

    [Fact]
    public void ThreeRoleNotificationsForOneSwitchRebuildOnce()
    {
        var rig = new Rig();
        int builds = rig.Builds;
        var client = rig.Service.NotificationClientForTests;

        client.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eConsole, "d");
        client.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "d");
        client.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eCommunications, "d");

        Assert.Single(rig.Queued);
        rig.Queued[0]();
        Assert.Equal(builds + 1, rig.Builds);
        // A capture device changing is not our output.
        rig.Queued.Clear();
        client.OnDefaultDeviceChanged(EDataFlow.eCapture, ERole.eMultimedia, "mic");
        Assert.Empty(rig.Queued);
    }

    [Fact]
    public void ASwitchAnnouncesTheNewOutputsLevelOnce()
    {
        var rig = new Rig(n => new FakeEndpoint(n == 1 ? 0.2f : 0.7f));
        var seen = new List<float>();
        rig.Service.MasterVolumeChanged += (v, _) => { lock (seen) seen.Add(v); };

        rig.Service.NotificationClientForTests.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "d");
        rig.Queued[0]();

        Assert.Equal(new[] { 70f }, seen);
    }

    // OnNotify runs on a Windows thread while another thread rebuilds the endpoint. Unregistering the old endpoint waits for the running
    // callback to finish (as the real one does); the callback must therefore never need a lock the rebuild holds. The old code deadlocked.
    [Fact]
    public async Task ARunningVolumeCallbackNeverBlocksARebuild()
    {
        var oldEndpoint = new FakeEndpoint(0.5f);
        var newEndpoint = new FakeEndpoint(0.6f);
        int builds = 0;
        var unregisterEntered = new ManualResetEventSlim();
        var notifyReturned = new ManualResetEventSlim();
        var handlerEntered = new ManualResetEventSlim();
        var unregisterDone = new ManualResetEventSlim();
        oldEndpoint.OnUnregister = () =>
        {
            unregisterEntered.Set();
            notifyReturned.Wait(5000);      // the real Unregister waits for the callback in progress
            unregisterDone.Set();
        };
        var service = new AudioEndpointService(new FakeEnumerator(), () => Interlocked.Increment(ref builds) == 1 ? oldEndpoint : newEndpoint, null);
        service.MasterVolumeChanged += (_, _) =>
        {
            handlerEntered.Set();
            unregisterEntered.Wait(2000);   // stay "in flight" until the rebuild is unregistering
        };

        var data = new NotifyData { guid = Guid.Empty, muted = 0, volume = 0.25f };
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NotifyData>());
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            var notify = Task.Run(() => { service.VolumeCallbackForTests.OnNotify(ptr); notifyReturned.Set(); });
            Assert.True(handlerEntered.Wait(2000), "the callback never started");

            var rebuild = Task.Run(() => { service.InvalidateCache(); return service.GetMasterVolume(); });

            try { await Task.WhenAll(notify, rebuild).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { Assert.Fail("the callback and the rebuild blocked each other (deadlock)"); }
            Assert.True(unregisterDone.Wait(2000), "the old endpoint was never unregistered");
            Assert.Equal(60f, await rebuild);
        }
        finally { Marshal.FreeHGlobal(ptr); service.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyData { public Guid guid; public int muted; public float volume; }

    [Fact]
    public void DisposeIsSafeAndIdempotent()
    {
        var rig = new Rig();
        rig.Service.Dispose();
        rig.Service.Dispose();
        rig.Service.NotificationClientForTests.OnDefaultDeviceChanged(EDataFlow.eRender, ERole.eMultimedia, "d");   // after Dispose: ignored
        Assert.Empty(rig.Queued);
        Assert.Equal(0f, rig.Service.GetMasterVolume());
        Assert.Equal(1, rig.Builds);
    }
}
