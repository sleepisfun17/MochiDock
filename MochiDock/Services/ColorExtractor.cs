using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace MochiDock.Services;

public static class ColorExtractor
{
    public static MediaColor ExtractDominantColor(BitmapSource? bitmap)
    {
        if (bitmap == null) return MediaColor.FromRgb(10, 132, 255); // Default blue

        try
        {
            // Format to Bgra32 if not already
            FormatConvertedBitmap converted = new();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();

            int width = Math.Min(32, converted.PixelWidth);
            int height = Math.Min(32, converted.PixelHeight);

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            // Render scaled thumbnail for fast color histogram
            TransformedBitmap scaled = new(converted, new ScaleTransform((double)width / converted.PixelWidth, (double)height / converted.PixelHeight));
            scaled.CopyPixels(pixels, stride, 0);

            double maxScore = -1;
            MediaColor bestColor = MediaColor.FromRgb(10, 132, 255);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));

                // Ignore near-black and near-white
                if (max < 30 || min > 230) continue;

                double delta = max - min;
                double saturation = max == 0 ? 0 : delta / max;
                double score = saturation * (max / 255.0);

                if (score > maxScore)
                {
                    maxScore = score;
                    bestColor = MediaColor.FromRgb(r, g, b);
                }
            }

            return bestColor;
        }
        catch
        {
            return MediaColor.FromRgb(10, 132, 255);
        }
    }
}
