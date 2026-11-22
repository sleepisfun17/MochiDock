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
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, IntPtr pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
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
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptrVal;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
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
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr pNotify);
    }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioVolumeNotificationData
    {
        public Guid guidEventContext;
        public int bMuted;
        public float fMasterVolume;
    }

    // Windows 10 & 11 Audio Policy Configuration Interface
    [Guid("F8679F50-850A-41CF-9C72-430F290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
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

    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _cachedVolumeObject;
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceNotificationClient _notificationClient;
    private float _lastVolume = -1f;
    private bool _lastMuted;
    private bool _disposed;
    private readonly object _lock = new();

    public AudioEndpointService()
    {
        _volumeCallback = new VolumeCallback(this);
        _notificationClient = new DeviceNotificationClient(this);
        EnsureCallbacksRegistered();
    }

    private sealed class VolumeCallback(AudioEndpointService owner) : IAudioEndpointVolumeCallback
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

    private sealed class DeviceNotificationClient(AudioEndpointService owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState) => 0;
        public int OnDeviceAdded(string pwstrDeviceId) => 0;
        public int OnDeviceRemoved(string pwstrDeviceId) => 0;
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
                owner.OnDefaultOutputChanged();
            return 0;
        }
        public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => 0;
    }

    private void EnsureCallbacksRegistered()
    {
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                if (_enumerator == null)
                {
                    _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                    _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
                }
                _ = GetMasterVolumeObjectLocked();
            }
            catch (Exception ex) { AppLog.Warn("Audio", ex); }
        }
    }

    private void OnCoreAudioVolumeChanged(float level, bool isMuted)
    {
        if (_disposed) return;
        float currentVol = (float)Math.Round(level * 100f, 0);
        lock (_lock)
        {
            if (Math.Abs(currentVol - _lastVolume) < 0.5f && isMuted == _lastMuted) return;
            _lastVolume = currentVol;
            _lastMuted = isMuted;
        }
        MasterVolumeChanged?.Invoke(currentVol, isMuted);
    }

    private void OnDefaultOutputChanged()
    {
        if (_disposed) return;
        InvalidateCache();
        var vol = GetMasterVolumeObject();
        if (vol != null && vol.GetMasterVolumeLevelScalar(out float level) == 0 && vol.GetMute(out bool muted) == 0)
            OnCoreAudioVolumeChanged(level, muted);
    }

    private IAudioEndpointVolume? GetMasterVolumeObject()
    {
        lock (_lock) return GetMasterVolumeObjectLocked();
    }

    private IAudioEndpointVolume? GetMasterVolumeObjectLocked()
    {
        if (_disposed) return null;
        if (_cachedVolumeObject != null) return _cachedVolumeObject;

        IMMDevice? device = null;
        try
        {
            _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) == 0 && device != null)
            {
                var iid = IID_IAudioEndpointVolume;
                if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) == 0 && obj is IAudioEndpointVolume endpoint)
                {
                    _cachedVolumeObject = endpoint;
                    _cachedVolumeObject.RegisterControlChangeNotify(_volumeCallback);
                    if (_lastVolume < 0f && _cachedVolumeObject.GetMasterVolumeLevelScalar(out float level) == 0 && _cachedVolumeObject.GetMute(out bool muted) == 0)
                    {
                        _lastVolume = (float)Math.Round(level * 100f, 0);
                        _lastMuted = muted;
                    }
                    return _cachedVolumeObject;
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
        finally
        {
            if (device != null) try { Marshal.ReleaseComObject(device); } catch { }
        }
        _cachedVolumeObject = null;
        return null;
    }

    public void InvalidateCache()
    {
        lock (_lock)
        {
            if (_cachedVolumeObject != null)
            {
                try { _cachedVolumeObject.UnregisterControlChangeNotify(_volumeCallback); } catch { }
                try { Marshal.ReleaseComObject(_cachedVolumeObject); } catch { }
                _cachedVolumeObject = null;
            }
        }
    }

    public void CheckVolumeState()
    {
        try
        {
            var vol = GetMasterVolumeObject();
            if (vol != null && vol.GetMasterVolumeLevelScalar(out float level) == 0 && vol.GetMute(out bool isMuted) == 0)
                OnCoreAudioVolumeChanged(level, isMuted);
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
    }

    public float GetMasterVolume()
    {
        try
        {
            var vol = GetMasterVolumeObject();
            if (vol != null && vol.GetMasterVolumeLevelScalar(out float level) == 0)
            {
                return (float)Math.Round(level * 100f, 0);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
        return 50f;
    }

    public void SetMasterVolume(float percent)
    {
        try
        {
            var vol = GetMasterVolumeObject();
            if (vol != null)
            {
                float scalar = Math.Clamp(percent / 100f, 0f, 1f);
                var emptyGuid = Guid.Empty;
                vol.SetMasterVolumeLevelScalar(scalar, ref emptyGuid);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
    }

    public float AdjustMasterVolume(float deltaPercent)
    {
        float current = GetMasterVolume();
        float newVol = Math.Clamp(current + deltaPercent, 0f, 100f);
        SetMasterVolume(newVol);
        return newVol;
    }

    public bool ToggleMute()
    {
        try
        {
            var vol = GetMasterVolumeObject();
            if (vol != null && vol.GetMute(out bool isMuted) == 0)
            {
                var emptyGuid = Guid.Empty;
                vol.SetMute(!isMuted, ref emptyGuid);
                return !isMuted;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
        return false;
    }

    public bool IsMuted()
    {
        try
        {
            var vol = GetMasterVolumeObject();
            if (vol != null && vol.GetMute(out bool isMuted) == 0)
            {
                return isMuted;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
        return false;
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
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            if (_cachedVolumeObject != null)
            {
                try { _cachedVolumeObject.UnregisterControlChangeNotify(_volumeCallback); } catch { }
                try { Marshal.ReleaseComObject(_cachedVolumeObject); } catch { }
                _cachedVolumeObject = null;
            }
            if (_enumerator != null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_notificationClient); } catch { }
                try { Marshal.ReleaseComObject(_enumerator); } catch { }
                _enumerator = null;
            }
        }
    }
}
