using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace MochiDock.Core;

/// <summary>Pure helpers for running yt-dlp safely: argument construction and download verification.</summary>
public static class YtDlpCommand
{
    public static bool TryParseUrl(string? input, out string url)
    {
        url = string.Empty;
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;
        url = uri.AbsoluteUri;
        return true;
    }

    /// <summary>Builds the argument list (one entry per argument, never a joined string). The URL follows "--" so it cannot be read as an option.</summary>
    /// <summary>MP3 extraction needs ffmpeg; video falls back to a single pre-merged file without it.</summary>
    public static bool RequiresFfmpeg(bool audioOnly) => audioOnly;

    public static IReadOnlyList<string> BuildArguments(string url, string outputDirectory, bool audioOnly, string? ffmpegLocation = null)
    {
        var args = new List<string> { "--no-playlist" };
        if (!string.IsNullOrWhiteSpace(ffmpegLocation))
        {
            args.Add("--ffmpeg-location");
            args.Add(ffmpegLocation);
        }
        if (audioOnly)
        {
            args.Add("-x");
            args.Add("--audio-format");
            args.Add("mp3");
        }
        else
        {
            args.Add("-f");
            // Merging separate video and audio streams needs ffmpeg; without it take a single-file format.
            args.Add(string.IsNullOrWhiteSpace(ffmpegLocation)
                ? "best[ext=mp4]/best"
                : "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best");
        }
        args.Add("-o");
        args.Add(Path.Combine(outputDirectory, "%(title)s.%(ext)s"));
        args.Add("--");
        args.Add(url);
        return args;
    }

    /// <summary>Finds the hex SHA-256 for <paramref name="fileName"/> in a "hash  filename" checksum list.</summary>
    public static string? FindExpectedHash(string checksums, string fileName)
    {
        foreach (var rawLine in checksums.Split('\n'))
        {
            var parts = rawLine.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var name = parts[1].TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) && parts[0].Length == 64)
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    public static bool HashMatches(byte[] data, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return false;
        var actual = Convert.ToHexString(SHA256.HashData(data));
        return string.Equals(actual, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
