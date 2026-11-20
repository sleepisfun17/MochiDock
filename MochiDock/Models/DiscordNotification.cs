namespace MochiDock.Models;

public class DiscordNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Sender { get; set; } = "Discord";
    public string Message { get; set; } = "";
    public string ChannelOrServer { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string FormattedTime => Timestamp.ToString("HH:mm");
    public bool IsDirectMessage { get; set; } = true;
}
