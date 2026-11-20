namespace MochiDock.Core;

/// <summary>
/// Lays out the Stats per-core bars in whole device pixels: every bar exactly as wide as the others, the same gap between each pair,
/// and the group centred. (A UniformGrid rounds each cell on its own, so 32 thin bars came out 2 and 3 pixels wide side by side.)
/// <see cref="Grid"/> wraps many threads into rows (like Task Manager) so a 32-thread CPU is not drawn as hair-thin bars.
/// </summary>
public static class CoreBarLayout
{
    /// <summary>The most cells in one row before the grid wraps; 16 keeps the layout the same at every zoom.</summary>
    public const int MaxColumns = 16;

    /// <summary>The smallest comfortable cell in device pixels; a crowded grid shrinks the gap before going below it.</summary>
    public const int MinCellWidth = 7, MinCellHeight = 8;

    /// <summary>The bar width, the gap and the left edge (all in device pixels) for <paramref name="count"/> bars in <paramref name="availablePixels"/>.</summary>
    public static (int BarWidth, int Gap, int Left) Compute(double availablePixels, int count, double preferredGapPixels)
    {
        int width = (int)Math.Floor(Math.Max(0, availablePixels));
        if (count <= 0 || width <= 0) return (0, 0, 0);
        int gap = Math.Max(1, (int)Math.Round(preferredGapPixels));
        // Many cores in little room: shrink the gap before a bar would vanish.
        while (gap > 1 && (width - gap * (count - 1)) / count < 2) gap--;
        int bar = Math.Max(1, (width - gap * (count - 1)) / count);
        int used = bar * count + gap * (count - 1);
        int left = Math.Max(0, (width - used) / 2);
        return (bar, gap, left);
    }

    /// <summary>
    /// A rows x columns grid of equal whole-pixel cells for <paramref name="count"/> cores in the given device-pixel area, centred, filled
    /// row by row (the last row may be short). It takes the fewest rows that keep at most <see cref="MaxColumns"/> per row and each cell
    /// at least <see cref="MinCellWidth"/> x <see cref="MinCellHeight"/> pixels (16 threads in one row, 32 in 2 x 16, 64 in 4 x 16); when
    /// no grid reaches that, the one whose cells come closest.
    /// </summary>
    public static (int Rows, int Columns, int CellWidth, int CellHeight, int Gap, int Left, int Top) Grid(
        double availableWidth, double availableHeight, int count, double preferredGapPixels)
    {
        int width = (int)Math.Floor(Math.Max(0, availableWidth)), height = (int)Math.Floor(Math.Max(0, availableHeight));
        if (count <= 0 || width <= 0 || height <= 0) return (0, 0, 0, 0, 0, 0, 0);
        int preferredGap = Math.Max(1, (int)Math.Round(preferredGapPixels));

        (int Rows, int Columns, int CellWidth, int CellHeight, int Gap)? best = null;
        double bestScore = double.MinValue;
        for (int rows = 1; rows <= count && rows <= height; rows++)
        {
            int columns = (count + rows - 1) / rows;
            if (rows > 1 && (count + rows - 2) / (rows - 1) == columns) continue;   // same columns as fewer rows: only adds empty rows
            int gap = preferredGap;
            int cellWidth = CellSize(width, columns, gap), cellHeight = CellSize(height, rows, gap);
            // Crowded: give up gap before cells shrink below the minimum.
            while (gap > 1 && (cellWidth < MinCellWidth || cellHeight < MinCellHeight))
            {
                gap--;
                cellWidth = CellSize(width, columns, gap);
                cellHeight = CellSize(height, rows, gap);
            }
            if (cellWidth < 1 || cellHeight < 1) continue;
            if (columns <= MaxColumns && cellWidth >= MinCellWidth && cellHeight >= MinCellHeight)
            {
                best = (rows, columns, cellWidth, cellHeight, gap);
                break;
            }
            double score = Math.Min((double)cellWidth / MinCellWidth, (double)cellHeight / MinCellHeight);
            if (score > bestScore) { bestScore = score; best = (rows, columns, cellWidth, cellHeight, gap); }
        }
        if (best is not { } b) return (0, 0, 0, 0, 0, 0, 0);
        int usedWidth = b.CellWidth * b.Columns + b.Gap * (b.Columns - 1);
        int usedHeight = b.CellHeight * b.Rows + b.Gap * (b.Rows - 1);
        return (b.Rows, b.Columns, b.CellWidth, b.CellHeight, b.Gap, Math.Max(0, (width - usedWidth) / 2), Math.Max(0, (height - usedHeight) / 2));
    }

    private static int CellSize(int available, int cells, int gap) => (available - gap * (cells - 1)) / cells;
}
