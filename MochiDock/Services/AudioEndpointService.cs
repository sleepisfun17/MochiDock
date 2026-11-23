using System.Runtime.InteropServices;

namespace MochiDock.Services;

public enum EDataFlow
{
    eRender,
    eCapture,
    eAll
}

public enum ERole : uint
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

public class AudioDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
}

public class AudioEndpointService : IDisposable
{
    private const string CLSID_MMDeviceEnumerator = "BCDE0395-E52F-467C-8E3D-C4579291692E";
    private static readonly Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;

    [ComImport]
    [Guid(CLSID_MMDeviceEnumerator)]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, IntPtr pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid guid, uint id)
        {
            fmtid = guid;
            pid = id;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptrVal;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr pNotify);
    }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioVolumeNotificationData
    {
        public Guid guidEventContext;
        public int bMuted;
        public float fMasterVolume;
    }

    // Windows 10 & 11 Audio Policy Configuration Interface
    [Guid("F8679F50-850A-41CF-9C72-430F290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, [In, MarshalAs(UnmanagedType.U4)] ERole role);
        [PreserveSig] int SetEndpointVisibility();
    }

    // CoClass for Windows 10/11 PolicyConfig Client
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class CPolicyConfigClient { }

    private static readonly PropertyKey PKEY_Device_FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public event Action<float, bool>? MasterVolumeChanged;

    // ---- State -------------------------------------------------------------------------------------------------------------------
    // Windows calls the two callbacks below on its own audio threads. They must not block and must not do COM work, so they only mark the
    // endpoint stale and queue one rebuild on the thread pool. No lock is ever held across a COM call:
    //   _gate         guards the fields _endpoint, _enumerator and _disposed (only ever swapped under it, no COM inside);
    //   _rebuildSerial makes two rebuilds run one after the other (COM inside, but no callback and no _gate holder ever waits for it);
    //   _levelGate    guards the last level and the de-duplication of volume events (tiny, never around COM).
    private readonly object _gate = new();
    private readonly object _rebuildSerial = new();
    private readonly object _levelGate = new();
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceNotificationClient _notificationClient;
    private readonly Func<IAudioEndpointVolume?> _endpointFactory;
    private readonly Action<Action> _queueRebuild;
    private volatile bool _endpointStale = true;
    private int _rebuildQueued;
    // Only a switch of the multimedia output shows the new output's level. Other rebuilds (any device's state changing, a device removed,
    // the communications or console default changing) are silent, so the volume bar does not pop up when a mic or headset connects.
    private volatile bool _announcePending;
    private float _lastVolume = -1f;
    private bool _lastMuted;
    private volatile bool _disposed;

    public AudioEndpointService() : this(null, null, null) { }

    /// <summary>
    /// The seam for tests: a fake enumerator (it only has to accept the notification callback), a factory that gives the endpoint for the
    /// current default output (null means none) and a queue for the coalesced rebuild (null means the thread pool).
    /// </summary>
    internal AudioEndpointService(IMMDeviceEnumerator? enumerator, Func<IAudioEndpointVolume?>? endpointFactory, Action<Action>? queueRebuild)
    {
        _volumeCallback = new VolumeCallback(this);
        _notificationClient = new DeviceNotificationClient(this);
        _endpointFactory = endpointFactory ?? CreateDefaultEndpoint;
        _queueRebuild = queueRebuild ?? (work => ThreadPool.QueueUserWorkItem(_ => work()));
        try
        {
            _enumerator = enumerator ?? (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            if (hr < 0) AppLog.Warn("Audio", new COMException("RegisterEndpointNotificationCallback failed", hr));
        }
        catch (Exception ex) { AppLog.Warn("Audio", ex); }
        _ = GetEndpoint();
    }

    internal IAudioEndpointVolumeCallback VolumeCallbackForTests => _volumeCallback;
    internal IMMNotificationClient NotificationClientForTests => _notificationClient;

    // ---- Windows callbacks (no COM, no lock held during COM) ----------------------------------------------------------------------
    internal sealed class VolumeCallback(AudioEndpointService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr pNotify)
        {
            if (pNotify != IntPtr.Zero)
            {
                var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(pNotify);
                owner.OnCoreAudioVolumeChanged(data.fMasterVolume, data.bMuted != 0);
            }
            return 0;
        }
    }

    internal sealed class DeviceNotificationClient(AudioEndpointService owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState) { owner.MarkStaleAndQueueRebuild(); return 0; }
        public int OnDeviceAdded(string pwstrDeviceId) => 0;
        public int OnDeviceRemoved(string pwstrDeviceId) { owner.MarkStaleAndQueueRebuild(); return 0; }
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            // One switch of the output sends a notification per role: they all mark the same endpoint stale and share one rebuild.
            if (flow == EDataFlow.eRender) owner.MarkStaleAndQueueRebuild(announce: role == ERole.eMultimedia);
            return 0;
        }
        public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => 0;
    }

    private void MarkStaleAndQueueRebuild(bool announce = false)
    {
        if (_disposed) return;
        _endpointStale = true;
        if (announce) _announcePending = true;
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 0) _queueRebuild(RunQueuedRebuild);
    }

    // The queued rebuild (a thread-pool thread, never a Windows callback): builds the endpoint of the new output and, after a switch of the
    // multimedia output, shows its level once.
    private void RunQueuedRebuild()
    {
        Volatile.Write(ref _rebuildQueued, 0);
        bool announce = _announcePending;
        _announcePending = false;
        if (_disposed || (!_endpointStale && !announce)) return;   // a volume call may already have rebuilt; a switch is still announced
        try
        {
            var endpoint = GetEndpoint();
            if (endpoint == null || !announce) return;
            lock (_levelGate) { _lastVolume = -1f; }   // the new output's level is announced even if it equals the old one
            if (endpoint.GetMasterVolumeLevelScalar(out float level) >= 0 && endpoint.GetMute(out bool muted) >= 0)
                OnCoreAudioVolumeChanged(level, muted);
        }
        catch (Exception ex) { AppLog.Warn("Audio", ex); }
    }

    private void OnCoreAudioVolumeChanged(float level, bool isMuted)
    {
        if (_disposed) return;
        float currentVol = (float)Math.Round(level * 100f, 0);
        lock (_levelGate)
        {
            if (Math.Abs(currentVol - _lastVolume) < 0.5f && isMuted == _lastMuted) return;
            _lastVolume = currentVol;
            _lastMuted = isMuted;
        }
        MasterVolumeChanged?.Invoke(currentVol, isMuted);
    }

    // ---- The endpoint ----------------------------------------------------------------------------------------------------------------
    // The real factory: the default render endpoint's IAudioEndpointVolume (COM, so never called from a Windows callback or under _gate).
    private IAudioEndpointVolume? CreateDefaultEndpoint()
    {
        IMMDevice? device = null;
        try
        {
            var enumerator = _enumerator;
            if (enumerator == null) return null;
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            if (hr < 0 || device == null) { AppLog.Warn("Audio", new COMException("GetDefaultAudioEndpoint failed", hr)); return null; }
            var iid = IID_IAudioEndpointVolume;
            hr = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj);
            if (hr < 0 || obj is not IAudioEndpointVolume endpoint) { AppLog.Warn("Audio", new COMException("Activate(IAudioEndpointVolume) failed", hr)); return null; }
            return endpoint;
        }
        catch (Exception ex) { AppLog.Warn("Audio", ex); return null; }
        finally
        {
            if (device != null) try { Marshal.ReleaseComObject(device); } catch { }
        }
    }

    // The current endpoint, rebuilt first when it is missing or marked stale.
    private IAudioEndpointVolume? GetEndpoint()
    {
        if (_disposed) return null;
        if (!_endpointStale)
        {
            lock (_gate)
            {
                if (_endpoint != null) return _endpoint;
            }
        }
        return Rebuild();
    }

    private IAudioEndpointVolume? Rebuild()
    {
        IAudioEndpointVolume? old;
        IAudioEndpointVolume? fresh;
        lock (_rebuildSerial)
        {
            if (_disposed) return null;
            // Another thread may have rebuilt while this one waited for its turn.
            if (!_endpointStale)
            {
                lock (_gate) { if (_endpoint != null) return _endpoint; }
            }
            _endpointStale = false;   // cleared before building: a device change during the build marks it stale again
            fresh = null;
            try
            {
                fresh = _endpointFactory();
                if (fresh != null)
                {
                    int hr = fresh.RegisterControlChangeNotify(_volumeCallback);
                    if (hr < 0) AppLog.Warn("Audio", new COMException("RegisterControlChangeNotify failed", hr));
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Audio", ex);
                if (fresh != null) { ReleaseLater(fresh, unregister: false); fresh = null; }
            }
            lock (_gate)
            {
                if (_disposed) { old = null; }
                else { old = _endpoint; _endpoint = fresh; }
            }
            if (fresh == null) _endpointStale = true;   // nothing usable yet: the next call tries again
        }
        if (_disposed && fresh != null) { ReleaseLater(fresh, unregister: true); return null; }
        if (old != null) ReleaseLater(old, unregister: true);   // outside every lock, on a pool thread
        return fresh;
    }

    // Unregister and release an endpoint that was swapped out. Unregister waits for a volume callback that is running, so it never runs
    // under a lock or on the caller's thread (it may be the UI thread).
    private static Task ReleaseLater(IAudioEndpointVolume endpoint, bool unregister, IAudioEndpointVolumeCallback? callback = null) =>
        Task.Run(() =>
        {
            if (unregister && callback != null) try { endpoint.UnregisterControlChangeNotify(callback); } catch { }
            try { Marshal.ReleaseComObject(endpoint); } catch { }
        });

    private void ReleaseLater(IAudioEndpointVolume endpoint, bool unregister) => ReleaseLater(endpoint, unregister, _volumeCallback);

    /// <summary>Marks the endpoint stale: the next volume call (or the queued rebuild) builds a new one.</summary>
    public void InvalidateCache() => _endpointStale = true;

    // Runs one call on the endpoint. A failing HRESULT (the output changed under us, AUDCLNT_E_DEVICE_INVALIDATED) rebuilds the endpoint and
    // retries once; a second failure gives up and leaves the endpoint stale for the next call.
    private bool TryCall(Func<IAudioEndpointVolume, int> call)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = GetEndpoint();
            if (endpoint == null) return false;
            int hr;
            try { hr = call(endpoint); }
            catch (COMException ex) { hr = ex.HResult; }
            catch (Exception ex) { AppLog.Warn("Audio", ex); return false; }
            if (hr >= 0) return true;
            _endpointStale = true;
            AppLog.Warn("Audio", new COMException($"Audio endpoint call failed (0x{hr:X8}); rebuilding the endpoint" + (attempt == 0 ? " and retrying" : ""), hr));
        }
        return false;
    }

    public void CheckVolumeState()
    {
        float level = 0f; bool muted = false;
        if (TryCall(e => { int hr = e.GetMasterVolumeLevelScalar(out level); return hr < 0 ? hr : e.GetMute(out muted); }))
            OnCoreAudioVolumeChanged(level, muted);
    }

    /// <summary>The master volume in percent; on a real failure the last known level (0 when none was ever read), never an invented 50.</summary>
    public float GetMasterVolume()
    {
        float level = 0f;
        if (TryCall(e => e.GetMasterVolumeLevelScalar(out level)))
        {
            float percent = (float)Math.Round(level * 100f, 0);
            return percent;
        }
        lock (_levelGate) return Math.Max(0f, _lastVolume);
    }

    public void SetMasterVolume(float percent)
    {
        float scalar = Math.Clamp(percent / 100f, 0f, 1f);
        TryCall(e => { var empty = Guid.Empty; return e.SetMasterVolumeLevelScalar(scalar, ref empty); });
    }

    /// <summary>Changes the volume by a delta and returns the new level; when the endpoint cannot be read or set it does nothing and returns the last known level.</summary>
    public float AdjustMasterVolume(float deltaPercent)
    {
        float level = 0f;
        if (!TryCall(e => e.GetMasterVolumeLevelScalar(out level)))
        {
            lock (_levelGate) return Math.Max(0f, _lastVolume);
        }
        float current = (float)Math.Round(level * 100f, 0);
        float newVol = Math.Clamp(current + deltaPercent, 0f, 100f);
        float scalar = newVol / 100f;
        if (!TryCall(e => { var empty = Guid.Empty; return e.SetMasterVolumeLevelScalar(scalar, ref empty); })) return current;
        return newVol;
    }

    public bool ToggleMute()
    {
        bool isMuted = false;
        if (!TryCall(e => e.GetMute(out isMuted))) { lock (_levelGate) return _lastMuted; }
        if (!TryCall(e => { var empty = Guid.Empty; return e.SetMute(!isMuted, ref empty); })) return isMuted;
        return !isMuted;
    }

    public bool IsMuted()
    {
        bool isMuted = false;
        if (TryCall(e => e.GetMute(out isMuted))) return isMuted;
        lock (_levelGate) return _lastMuted;
    }

    public List<AudioDeviceInfo> GetPlaybackDevices()
    {
        var list = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? defaultDev = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            string defaultId = "";
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out defaultDev) == 0 && defaultDev != null)
            {
                defaultDev.GetId(out defaultId);
            }

            if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out collection) == 0 && collection != null)
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice? dev = null;
                    IPropertyStore? store = null;
                    try
                    {
                        if (collection.Item(i, out dev) == 0 && dev != null)
                        {
                            dev.GetId(out string devId);
                            string friendlyName = "Audio Device";

                            if (dev.OpenPropertyStore(0 /* STGM_READ */, out store) == 0 && store != null)
                            {
                                var key = PKEY_Device_FriendlyName;
                                PropVariant pv = default;
                                try
                                {
                                    if (store.GetValue(ref key, out pv) == 0 && pv.ptrVal != IntPtr.Zero)
                                        friendlyName = Marshal.PtrToStringUni(pv.ptrVal) ?? friendlyName;
                                }
                                finally
                                {
                                    _ = PropVariantClear(ref pv);
                                }
                            }

                            list.Add(new AudioDeviceInfo
                            {
                                Id = devId,
                                Name = friendlyName,
                                IsDefault = string.Equals(devId, defaultId, StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }
                    finally
                    {
                        if (store != null) try { Marshal.ReleaseComObject(store); } catch { }
                        if (dev != null) try { Marshal.ReleaseComObject(dev); } catch { }
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
        finally
        {
            if (collection != null) try { Marshal.ReleaseComObject(collection); } catch { }
            if (defaultDev != null) try { Marshal.ReleaseComObject(defaultDev); } catch { }
            if (enumerator != null) try { Marshal.ReleaseComObject(enumerator); } catch { }
        }
        return list;
    }

    public bool SetDefaultPlaybackDevice(string deviceId)
    {
        IPolicyConfig? policyConfig = null;
        try
        {
            policyConfig = (IPolicyConfig)new CPolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            // Do not wait for Windows' notification (it can be late or lost): the next volume call rebuilds, and so does the queued rebuild.
            MarkStaleAndQueueRebuild(announce: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (policyConfig != null) try { Marshal.ReleaseComObject(policyConfig); } catch { }
        }
    }

    public void Dispose()
    {
        IAudioEndpointVolume? endpoint;
        IMMDeviceEnumerator? enumerator;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            endpoint = _endpoint; _endpoint = null;
            enumerator = _enumerator; _enumerator = null;
        }
        // Outside the lock; the unregistering runs on a pool thread and is waited for at most a second, so a callback that is still running
        // (and waiting for the UI thread) can never hold up shutdown.
        var work = Task.Run(() =>
        {
            if (endpoint != null)
            {
                try { endpoint.UnregisterControlChangeNotify(_volumeCallback); } catch { }
                try { Marshal.ReleaseComObject(endpoint); } catch { }
            }
            if (enumerator != null)
            {
                try { enumerator.UnregisterEndpointNotificationCallback(_notificationClient); } catch { }
                try { Marshal.ReleaseComObject(enumerator); } catch { }
            }
        });
        try { work.Wait(1000); } catch { }
    }
}
