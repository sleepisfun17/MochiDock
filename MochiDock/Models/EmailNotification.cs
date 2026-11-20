namespace MochiDock.Models;

public class EmailNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public string BodyPreview { get; set; } = "";
    public string FolderName { get; set; } = "Inbox";
    public string AccountEmail { get; set; } = "";
    public string ClientSource { get; set; } = "Mail";
    public string MessageId { get; set; } = "";
    public string EntryId { get => MessageId; set => MessageId = value; }
    public DateTime ReceivedTime { get; set; } = DateTime.Now;
    public string FormattedTime => ReceivedTime.ToString("HH:mm");
    public string? ExtractedVerificationCode { get; set; }
    public bool HasVerificationCode => !string.IsNullOrWhiteSpace(ExtractedVerificationCode);
}
