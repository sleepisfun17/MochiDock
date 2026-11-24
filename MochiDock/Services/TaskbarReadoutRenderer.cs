using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>The device-pixel rectangle of one block in the rendered readout (its hit region: contiguous, full height).</summary>
public sealed record TaskbarBlockRect(TaskbarBlockKind Kind, Rect Pixels);

/// <summary>The rendered readout: a premultiplied BGRA bitmap and where each block is. <see cref="OneLine"/> is the small-taskbar layout.</summary>
public sealed record TaskbarRender(int Width, int Height, byte[] Pixels, IReadOnlyList<TaskbarBlockRect> Blocks, bool OneLine);

/// <summary>
/// Draws the taskbar readout blocks into a bitmap, without any window, so the widget and the verify harness share one drawing.
/// The colours follow the Windows taskbar's own light or dark theme (with optional user label/value colours adapted to stay >= 4.5:1).
/// Two lines per block scale to fit the measured taskbar height (including tall Windhawk taskbars); a taskbar under
/// <see cref="SmallTaskbarDip"/> DIPs high (small icons) gets one line per block, "CPU 2% · GPU 5%".
/// </summary>
public static class TaskbarReadoutRenderer
{
    public const double SmallTaskbarDip = 36;
    public const double FontSize = 12;
    public const double Padding = 6, BlockGap = 14;
    public const string SegoeIcons = "Segoe Fluent Icons, Segoe MDL2 Assets";
    public const string DefaultTaskbarFont = "Segoe UI Variable Text, Segoe UI Variable, Segoe UI, Yu Gothic UI, Meiryo UI";
    public const string ChargingBolt = "\uE945";

    static readonly Color LightText = Color.FromRgb(0x1B, 0x1B, 0x1B);
    static readonly Color DarkText = Color.FromRgb(0xFF, 0xFF, 0xFF);
    static readonly Color LightWarn = Color.FromRgb(0xC4, 0x2B, 0x1C);
    static readonly Color DarkWarn = Color.FromRgb(0xFF, 0x5A, 0x5A);
    static readonly Color BoltBlue = Color.FromRgb(0x3B, 0x9E, 0xFF);
    static readonly Color LightPlate = Color.FromArgb(240, 243, 243, 243);
    static readonly Color DarkPlate = Color.FromArgb(240, 32, 32, 32);
    const byte StaleAlpha = 128;

    /// <summary>The Windows taskbar's default text colour for a light or dark taskbar (the harness uses it to check contrast).</summary>
    public static Color TextColor(bool darkTaskbar) => darkTaskbar ? DarkText : LightText;
    public static Color WarnColor(bool darkTaskbar) => darkTaskbar ? DarkWarn : LightWarn;

    public static Color ResolveInkColor(string? customHex, bool darkTaskbar)
    {
        var (r, g, b) = TaskbarStyle.ResolveColor(customHex, darkTaskbar);
        return Color.FromRgb(r, g, b);
    }

    public static FontFamily ResolveFontFamily(string? familyName)
    {
        string trimmed = familyName?.Trim() ?? "";
        if (trimmed.Length == 0) return new FontFamily(DefaultTaskbarFont);
        if (string.Equals(trimmed, "Quicksand", StringComparison.OrdinalIgnoreCase))
            return Application.Current?.TryFindResource("QuicksandFont") as FontFamily ?? new FontFamily("Segoe UI");
        return new FontFamily($"{trimmed}, {DefaultTaskbarFont}");
    }

    // One drawn piece of a line: an optional icon glyph, an optional colour emoji image, an optional charging bolt, an optional label, and the value.
    private sealed record Segment(
        FormattedText? Glyph,
        BitmapSource? Emoji,
        double EmojiSize,
        double EmojiGap,
        FormattedText? Bolt,
        FormattedText? Label,
        FormattedText? Value)
    {
        public double Width =>
            (Glyph is null ? 0 : Glyph.WidthIncludingTrailingWhitespace + 3)
            + (Emoji is null ? 0 : EmojiSize + EmojiGap)
            + (Bolt is null ? 0 : Bolt.WidthIncludingTrailingWhitespace)
            + (Label is null ? 0 : Label.WidthIncludingTrailingWhitespace)
            + (Value is null ? 0 : Value.WidthIncludingTrailingWhitespace);

        public double Height => Math.Max(
            Math.Max(Value?.Height ?? 0, Label?.Height ?? 0),
            Math.Max(Emoji is null ? 0 : EmojiSize, Math.Max(Glyph?.Height ?? 0, Bolt?.Height ?? 0)));
    }

    private sealed record Line(List<Segment> Segments)
    {
        public double Width => Segments.Sum(s => s.Width);
        public double Height => Segments.Count == 0 ? 0 : Segments.Max(s => s.Height);
    }

    /// <param name="dpi">Device DPI of the taskbar (96, 120, 144, 192 ...).</param>
    /// <param name="heightPx">The taskbar's height in device pixels.</param>
    /// <param name="background">A plate behind the text for transparent or restyled taskbars.</param>
    /// <param name="style">Optional font, size, bold and label/value colour preferences.</param>
    public static TaskbarRender? Render(IReadOnlyList<TaskbarBlock> blocks, double dpi, bool darkTaskbar, int heightPx, bool background = false, TaskbarAppearance? style = null)
    {
        if (blocks.Count == 0) return null;
        double scale = dpi / 96.0;
        double heightDip = heightPx / scale;
        bool oneLine = heightDip < SmallTaskbarDip;

        var fontFamily = ResolveFontFamily(style?.FontFamily);
        var weight = style?.Bold == true ? FontWeights.Bold : FontWeights.Normal;
        var textFace = new Typeface(fontFamily, FontStyles.Normal, weight, FontStretches.Normal);
        var iconFace = new Typeface(new FontFamily(SegoeIcons), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var culture = CultureInfo.InvariantCulture;

        double sample = new FormattedText("Ag", culture, FlowDirection.LeftToRight, textFace, 12.0, Brushes.Black, scale).Height;
        double lineHeightRatio = sample / 12.0;
        double fontSize = TaskbarStyle.ComputeFontSize(heightDip, lineHeightRatio, oneLine, style?.FontSize ?? 0);

        var labelBase = ResolveInkColor(style?.LabelColorHex, darkTaskbar);
        var valueBase = ResolveInkColor(style?.ValueColorHex, darkTaskbar);
        var warnBase = WarnColor(darkTaskbar);

        FormattedText Text(string text, Color color) => new(text, culture, FlowDirection.LeftToRight, textFace, fontSize, new SolidColorBrush(color), scale);
        FormattedText Icon(string text, Color color) => new(text, culture, FlowDirection.LeftToRight, iconFace, fontSize + 1, new SolidColorBrush(color), scale);
        static Color Shade(Color c, bool dim) => dim ? Color.FromArgb(StaleAlpha, c.R, c.G, c.B) : c;

        double emojiSize = Math.Max(8, fontSize * 1.05);
        double emojiGap = Math.Max(2, Math.Round(fontSize * 0.22));

        Segment MakeSegment(string glyph, string emojiKey, bool charging, string labelText, string valueText, bool warn, bool dim)
        {
            BitmapSource? emoji = emojiKey.Length > 0 ? EmojiImages.Get(emojiKey) : null;
            string effectiveLabel = labelText;
            if (emojiKey.Length > 0 && emoji is null)
                effectiveLabel = (emojiKey == TaskbarReadouts.UploadEmojiKey ? "▲ " : "▼ ") + labelText;

            var lColor = Shade(warn ? warnBase : labelBase, dim);
            var vColor = Shade(warn ? warnBase : valueBase, dim);

            return new Segment(
                glyph.Length > 0 ? Icon(glyph, lColor) : null,
                emoji,
                emojiSize,
                emojiGap,
                charging ? Icon(ChargingBolt, Shade(BoltBlue, dim)) : null,
                effectiveLabel.Length > 0 ? Text(effectiveLabel, lColor) : null,
                valueText.Length > 0 ? Text(valueText, vColor) : null);
        }

        // The lines of every block: two per block, or one (the lines joined with a dot) on a small taskbar.
        var layout = new List<(TaskbarBlock Block, List<Line> Lines)>();
        foreach (var block in blocks)
        {
            var top = MakeSegment(block.TopGlyph, block.TopEmojiKey, block.TopCharging, block.TopLabel, block.TopValue, block.TopWarn, block.TopDim);
            var lines = new List<Line>();
            if (oneLine)
            {
                var segments = new List<Segment> { top };
                if (block.HasBottom)
                {
                    segments.Add(new Segment(null, null, 0, 0, null, Text("  ·  ", labelBase), null));
                    segments.Add(MakeSegment(block.BottomGlyph, block.BottomEmojiKey, block.BottomCharging, block.BottomLabel, block.BottomValue, block.BottomWarn, block.BottomDim));
                }
                lines.Add(new Line(segments));
            }
            else
            {
                lines.Add(new Line(new List<Segment> { top }));
                if (block.HasBottom)
                    lines.Add(new Line(new List<Segment> { MakeSegment(block.BottomGlyph, block.BottomEmojiKey, block.BottomCharging, block.BottomLabel, block.BottomValue, block.BottomWarn, block.BottomDim) }));
            }
            layout.Add((block, lines));
        }

        double padding = Math.Max(Padding, Math.Round(fontSize * 0.5));
        double blockGap = Math.Max(BlockGap, Math.Round(fontSize * 1.15));
        double widthDip = padding * 2 + layout.Sum(l => l.Lines.Max(x => x.Width)) + blockGap * (layout.Count - 1);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(widthDip * scale));
        int pixelHeight = Math.Max(1, heightPx);

        var rects = new List<TaskbarBlockRect>();
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            // Alpha 1 keeps the whole rectangle hit-testable (hover tooltip, click) while staying visually transparent.
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, pixelWidth / scale, heightDip));
            if (background)
                dc.DrawRoundedRectangle(new SolidColorBrush(darkTaskbar ? DarkPlate : LightPlate), null, new Rect(0, 4, pixelWidth / scale, Math.Max(1, heightDip - 8)), 6, 6);

            double x = padding;
            for (int i = 0; i < layout.Count; i++)
            {
                var (block, lines) = layout[i];
                double blockWidth = lines.Max(l => l.Width);
                double total = lines.Sum(l => l.Height);
                double y = (heightDip - total) / 2;
                foreach (var line in lines)
                {
                    double sx = x;
                    foreach (var segment in line.Segments)
                    {
                        if (segment.Glyph is { } glyph)
                        {
                            dc.DrawText(glyph, new Point(sx, y + (line.Height - glyph.Height) / 2));
                            sx += glyph.WidthIncludingTrailingWhitespace + 3;
                        }
                        if (segment.Emoji is { } emoji)
                        {
                            double ey = y + (line.Height - segment.EmojiSize) / 2;
                            dc.DrawImage(emoji, new Rect(sx, ey, segment.EmojiSize, segment.EmojiSize));
                            sx += segment.EmojiSize + segment.EmojiGap;
                        }
                        if (segment.Bolt is { } bolt)
                        {
                            dc.DrawText(bolt, new Point(sx, y + (line.Height - bolt.Height) / 2));
                            sx += bolt.WidthIncludingTrailingWhitespace;
                        }
                        if (segment.Label is { } label)
                        {
                            dc.DrawText(label, new Point(sx, y + (line.Height - label.Height) / 2));
                            sx += label.WidthIncludingTrailingWhitespace;
                        }
                        if (segment.Value is { } value)
                        {
                            dc.DrawText(value, new Point(sx, y + (line.Height - value.Height) / 2));
                            sx += value.WidthIncludingTrailingWhitespace;
                        }
                    }
                    y += line.Height;
                }
                // The hit region: from the middle of the gap before this block to the middle of the gap after it.
                double left = i == 0 ? 0 : x - blockGap / 2;
                double right = i == layout.Count - 1 ? pixelWidth / scale : x + blockWidth + blockGap / 2;
                rects.Add(new TaskbarBlockRect(block.Kind, new Rect(left * scale, 0, (right - left) * scale, pixelHeight)));
                x += blockWidth + blockGap;
            }
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        bitmap.CopyPixels(pixels, pixelWidth * 4, 0);
        return new TaskbarRender(pixelWidth, pixelHeight, pixels, rects, oneLine);
    }

    /// <summary>The block at a device-pixel x position, or null.</summary>
    public static TaskbarBlockKind? HitTest(IReadOnlyList<TaskbarBlockRect> blocks, double xPixels)
    {
        foreach (var block in blocks)
            if (xPixels >= block.Pixels.Left && xPixels < block.Pixels.Right) return block.Kind;
        return null;
    }
}
