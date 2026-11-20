using System;

namespace MochiDock.Models;

public class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }

    public string PreviewText => Text.Length > 80 ? Text.Substring(0, 80).Replace("\r", " ").Replace("\n", " ") + "..." : Text.Replace("\r", " ").Replace("\n", " ");
    public string FormattedTime => CopiedAt.ToString("HH:mm");
}

