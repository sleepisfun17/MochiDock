namespace MochiDock.Core;
public sealed record LyricTrackIdentity(string Session, string Title, string Artist, string Album, long Duration)
{
    public static LyricTrackIdentity Create(string session,string title,string artist,string album,double duration) =>
        new(session.Trim(),title.Trim(),artist.Trim(),album.Trim(),(long)Math.Round(Math.Max(0,duration)));
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => System.Text.Json.JsonSerializer.Serialize(this);
}