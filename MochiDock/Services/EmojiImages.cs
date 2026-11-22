using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace MochiDock.Services;

/// <summary>
/// The coloured emoji pictures (Twemoji 72 x 72 PNG, CC-BY 4.0) packed in <c>Assets\emoji.pak</c> beside the app (a zip named by code points, for example
/// "1f600.png"). WPF text cannot draw colour emoji, so message text shows these pictures instead. A missing picture gives null and the
/// caller keeps the emoji as text.
/// </summary>
public static class EmojiImages
{
    private const int CacheLimit = 600;

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.Ordinal);
    private static ZipArchive? _pak;
    private static bool _pakTried;

    public static BitmapSource? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var image = Load(key);
        if (Cache.Count >= CacheLimit) Cache.Clear();
        Cache[key] = image;
        return image;
    }

    private static BitmapSource? Load(string key)
    {
        try
        {
            lock (Gate)
            {
                var pak = OpenPak();
                if (pak == null) return null;
                foreach (string name in Candidates(key))
                {
                    var entry = pak.GetEntry(name + ".png");
                    if (entry == null) continue;
                    using var stream = entry.Open();
                    using var memory = new MemoryStream((int)Math.Max(entry.Length, 1));
                    stream.CopyTo(memory);
                    memory.Position = 0;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 32;
                    image.StreamSource = memory;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
        }
        catch (Exception ex) { AppLog.Warn("Emoji", ex); }
        return null;
    }

    // The exact name first, then the name without U+FE0F, then with U+FE0F after the first symbol (how Twemoji names some joined emoji).
    private static string[] Candidates(string key)
    {
        string stripped = string.Join('-', Array.FindAll(key.Split('-'), part => part != "fe0f"));
        int dash = stripped.IndexOf('-');
        string withVs = dash > 0 ? stripped.Insert(dash, "-fe0f") : stripped;
        return new[] { key, stripped, withVs };
    }

    private static ZipArchive? OpenPak()
    {
        if (_pakTried) return _pak;
        _pakTried = true;
        string filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "emoji.pak");
        if (!File.Exists(filePath))
        {
            AppLog.Warn("Emoji", "Assets\\emoji.pak is missing; emoji stay as text.");
            return null;
        }
        _pak = new ZipArchive(File.OpenRead(filePath), ZipArchiveMode.Read);
        return _pak;
    }
}
