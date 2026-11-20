namespace MochiDock.Models;

public class WhatsAppNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RemoteId { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Message { get; set; } = "";
    public string FromJid { get; set; } = "";
    public string DeviceId { get; set; } = "";
    /// <summary>Display name of the WhatsApp account (GOWA device) that received the message.</summary>
    public string AccountName { get; set; } = "";
    public bool IsGroup { get; set; }
    public string GroupName { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string ParticipantJid { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string FormattedTime => Timestamp.ToString("HH:mm");
}
