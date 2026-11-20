using System;
using System.Collections.Generic;
using System.Text;

namespace MochiDock.Core;

public record ScriptLine(string Text, int WordCount, int FirstWordIndex, bool IsParagraphEnd);

public static class ScriptReaderEngine
{
    public const int DefaultWpm = 140;
    public const int MinWpm = 60;
    public const int MaxWpm = 300;
    public const int WpmStep = 10;
    public const int MaxChars = 60_000;
    public const double MinLineSeconds = 0.8;
    public const double ParagraphRestSeconds = 0.6;

    public static string CleanScript(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(Math.Min(normalized.Length, MaxChars + 8));
        bool blank = false;

        string[] lines = normalized.Split('\n');
        foreach (string line in lines)
        {
            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                blank = sb.Length > 0;
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(blank ? "\n\n" : "\n");
            }
            blank = false;
            sb.Append(string.Join(" ", words));
            if (sb.Length > MaxChars) break;
        }

        if (sb.Length > MaxChars)
        {
            sb.Length = MaxChars;
            int lastSpace = -1;
            for (int i = sb.Length - 1; i >= 0; i--)
            {
                if (char.IsWhiteSpace(sb[i]))
                {
                    lastSpace = i;
                    break;
                }
            }
            if (lastSpace > 0) sb.Length = lastSpace;
        }

        return sb.ToString().Trim();
    }

    public static List<ScriptLine> WrapLines(string cleanedText, double maxWidthDip, Func<string, double> measureWordWidth)
    {
        var result = new List<ScriptLine>();
        if (string.IsNullOrWhiteSpace(cleanedText)) return result;

        double spaceWidth = Math.Max(0.0, measureWordWidth("a a") - measureWordWidth("aa"));
        int wordNo = 0;

        string[] paragraphs = cleanedText.Split(new[] { "\n\n" }, StringSplitOptions.None);
        foreach (string para in paragraphs)
        {
            int beforeCount = result.Count;
            string[] rows = para.Split('\n');
            foreach (string row in rows)
            {
                var curWords = new List<string>();
                double curWidth = 0.0;
                int firstWordIndex = wordNo;

                string[] words = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words)
                {
                    double w = measureWordWidth(word);
                    if (curWords.Count > 0 && curWidth + spaceWidth + w > maxWidthDip)
                    {
                        result.Add(new ScriptLine(string.Join(" ", curWords), curWords.Count, firstWordIndex, false));
                        curWords.Clear();
                        curWidth = 0.0;
                        firstWordIndex = wordNo;
                    }

                    if (curWords.Count > 0)
                    {
                        curWidth += spaceWidth;
                    }
                    curWords.Add(word);
                    curWidth += w;
                    wordNo++;
                }

                if (curWords.Count > 0)
                {
                    result.Add(new ScriptLine(string.Join(" ", curWords), curWords.Count, firstWordIndex, false));
                }
            }

            if (result.Count > beforeCount)
            {
                var last = result[^1];
                result[^1] = last with { IsParagraphEnd = true };
            }
        }

        return result;
    }

    public static double LineDurationSeconds(int wordCount, int wpm, bool isParagraphEnd)
    {
        int clampedWpm = Math.Clamp(wpm, MinWpm, MaxWpm);
        double readTime = (wordCount * 60.0) / clampedWpm;
        return Math.Max(readTime, MinLineSeconds) + (isParagraphEnd ? ParagraphRestSeconds : 0.0);
    }

    public static TimeSpan CalculateReadingTime(int wordCount, int wpm)
    {
        if (wordCount <= 0) return TimeSpan.Zero;
        int clampedWpm = Math.Clamp(wpm, MinWpm, MaxWpm);
        double minutes = (double)wordCount / clampedWpm;
        return TimeSpan.FromMinutes(minutes);
    }
}
