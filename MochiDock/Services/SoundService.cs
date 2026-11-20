using System.IO;
using System.Media;

namespace MochiDock.Services;

public static class SoundService
{
    private static SoundPlayer? _popPlayer;
    private static SoundPlayer? _lovePlayer;
    private static bool _initialized;

    /// <summary>Master switch for the short UI sounds (set from settings).</summary>
    public static bool Enabled { get; set; } = true;

    // The sounds are synthesised (MochiDock.Core.UiSounds), so there is no audio file to ship or to go missing.
    private static void Init()
    {
        if (_initialized) return;
        try
        {
            _popPlayer = new SoundPlayer(new MemoryStream(MochiDock.Core.UiSounds.Pop()));
            _popPlayer.Load();
            _lovePlayer = new SoundPlayer(new MemoryStream(MochiDock.Core.UiSounds.Love()));
            _lovePlayer.Load();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Sound", logEx); }
        _initialized = true;
    }

    public static void PlayNotificationSound()
    {
        if (!Enabled) return;
        try
        {
            Init();
            _popPlayer?.Play();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Sound", logEx); }
    }

    public static void PlayPetSound()
    {
        if (!Enabled) return;
        try
        {
            Init();
            _lovePlayer?.Play();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Sound", logEx); }
    }
}
