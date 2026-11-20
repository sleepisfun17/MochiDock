using System.IO;
using System.Media;
namespace MochiDock.Services;
public static class PrayerAudio
{
    private static readonly object Gate=new();
    private static SoundPlayer? _player;
    private static long _generation;
    public static void Stop(){lock(Gate){_generation++;try{_player?.Stop();}catch(Exception ex){System.Diagnostics.Debug.WriteLine("Prayer audio stop: "+ex.Message);}finally{_player?.Dispose();_player=null;}}}
    public static async Task PlayAsync(string mode)
    {
        Stop();long generation;lock(Gate)generation=_generation;
        if(mode!="Tone")return;
        // The Tone chime is the synthesised notification pop (MochiDock.Core.UiSounds), so it can never be missing.
        var player=new SoundPlayer(new MemoryStream(MochiDock.Core.UiSounds.Pop()));
        try
        {
            await Task.Run(player.Load);
            lock(Gate){if(generation!=_generation){player.Dispose();return;}_player=player;player.Play();}
        }
        catch{player.Dispose();throw;}
    }
}
