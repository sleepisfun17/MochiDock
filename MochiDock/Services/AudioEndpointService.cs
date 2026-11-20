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

public class AudioEndpointService
{
    private const string CLSID_MMDeviceEnumerator = "BCDE0395-E52F-467C-8E3D-C4579291692E";
    private static readonly Guid IID_IMMDeviceEnumerator = typeof(IMMDeviceEnumerator).GUID;
    private static readonly Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;

    [ComImport]
    [Guid(CLSID_MMDeviceEnumerator)]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice(string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, IntPtr pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
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

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptrVal;
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        int VolumeStepUp(ref Guid pguidEventContext);
        int VolumeStepDown(ref Guid pguidEventContext);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
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

    private IAudioEndpointVolume? _cachedVolumeObject;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private float _lastVolume = -1f;
    private bool _lastMuted;
    private readonly object _lock = new();

    private IAudioEndpointVolume? GetMasterVolumeObject()
    {
        lock (_lock)
        {
            if (_cachedVolumeObject != null && (DateTime.UtcNow - _lastCacheTime).TotalSeconds < 3.0)
            {
                return _cachedVolumeObject;
            }

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device) == 0)
                {
                    var iid = IID_IAudioEndpointVolume;
                    if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) == 0)
                    {
                        _cachedVolumeObject = obj as IAudioEndpointVolume;
                        _lastCacheTime = DateTime.UtcNow;
                        return _cachedVolumeObject;
                    }
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
            _cachedVolumeObject = null;
            return null;
        }
    }

    public void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedVolumeObject = null;
            _lastCacheTime = DateTime.MinValue;
        }
    }

    public void CheckVolumeState()
    {
        try
        {
            var vol = GetMasterVolumeObject();
            if (vol != null && vol.GetMasterVolumeLevelScalar(out float level) == 0 && vol.GetMute(out bool isMuted) == 0)
            {
                float currentVol = (float)Math.Round(level * 100f, 0);
                if (_lastVolume < 0f)
                {
                    _lastVolume = currentVol;
                    _lastMuted = isMuted;
                    return;
                }

                if (Math.Abs(currentVol - _lastVolume) >= 0.5f || isMuted != _lastMuted)
                {
                    _lastVolume = currentVol;
                    _lastMuted = isMuted;
                    MasterVolumeChanged?.Invoke(currentVol, isMuted);
                }
            }
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
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            string defaultId = "";
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDev) == 0)
            {
                defaultDev.GetId(out defaultId);
            }

            if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out var collection) == 0)
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var dev) == 0)
                    {
                        dev.GetId(out string devId);
                        string friendlyName = "Audio Device";

                        if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0)
                        {
                            var key = PKEY_Device_FriendlyName;
                            if (store.GetValue(ref key, out var pv) == 0 && pv.ptrVal != IntPtr.Zero)
                            {
                                friendlyName = Marshal.PtrToStringUni(pv.ptrVal) ?? friendlyName;
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
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Audio", logEx); }
        return list;
    }

    public bool SetDefaultPlaybackDevice(string deviceId)
    {
        try
        {
            var policyConfig = (IPolicyConfig)new CPolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
