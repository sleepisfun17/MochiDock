using System;
using System.IO;

namespace MochiDock.Models;

public class ShelfFileItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public long FileSizeBytes { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public string ExtensionBadge => (Path.GetExtension(FilePath) ?? "").TrimStart('.').ToUpperInvariant();
}

