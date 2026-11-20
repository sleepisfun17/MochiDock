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
        var g = GroupedGrid(availableWidth, availableHeight, new[] { count }, preferredGapPixels);
        return (g.Rows, g.Columns, g.CellWidth, g.CellHeight, g.Gap, g.Left, g.Top);
    }

    /// <summary>
    /// Like <see cref="Grid"/>, for cores in groups (the P-core threads, then the E-cores): every group starts on a row of its own, so the
    /// kinds never share a row, and all cells keep one size. <see cref="CoreGrid.Cells"/> gives each core's row and column in group order.
    /// One group is exactly <see cref="Grid"/>.
    /// </summary>
    public static CoreGrid GroupedGrid(double availableWidth, double availableHeight, IReadOnlyList<int> groupCounts, double preferredGapPixels)
    {
        int width = (int)Math.Floor(Math.Max(0, availableWidth)), height = (int)Math.Floor(Math.Max(0, availableHeight));
        var groups = groupCounts.Where(c => c > 0).ToArray();
        if (groups.Length == 0 || width <= 0 || height <= 0) return CoreGrid.Empty;
        int preferredGap = Math.Max(1, (int)Math.Round(preferredGapPixels));
        int RowsFor(int columns) => groups.Sum(g => (g + columns - 1) / columns);

        // Candidates by row count, fewest first; for each, the fewest columns that still give it (balanced rows, as Grid always did).
        var candidates = new SortedDictionary<int, int>();
        for (int columns = groups.Max(); columns >= 1; columns--) candidates[RowsFor(columns)] = columns;

        (int Rows, int Columns, int CellWidth, int CellHeight, int Gap)? best = null;
        double bestScore = double.MinValue;
        foreach (var (rows, columns) in candidates)
        {
            if (rows > height) break;
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
        if (best is not { } b) return CoreGrid.Empty;
        int usedWidth = b.CellWidth * b.Columns + b.Gap * (b.Columns - 1);
        int usedHeight = b.CellHeight * b.Rows + b.Gap * (b.Rows - 1);
        var cells = new List<(int Row, int Column)>();
        int rowOffset = 0;
        foreach (int count in groups)
        {
            for (int i = 0; i < count; i++) cells.Add((rowOffset + i / b.Columns, i % b.Columns));
            rowOffset += (count + b.Columns - 1) / b.Columns;
        }
        return new CoreGrid(b.Rows, b.Columns, b.CellWidth, b.CellHeight, b.Gap, Math.Max(0, (width - usedWidth) / 2), Math.Max(0, (height - usedHeight) / 2), cells);
    }

    private static int CellSize(int available, int cells, int gap) => (available - gap * (cells - 1)) / cells;
}

/// <summary>A laid-out core grid in device pixels: the cell size, the gap, the top-left corner and each core's row and column.</summary>
public sealed record CoreGrid(int Rows, int Columns, int CellWidth, int CellHeight, int Gap, int Left, int Top, IReadOnlyList<(int Row, int Column)> Cells)
{
    public static readonly CoreGrid Empty = new(0, 0, 0, 0, 0, 0, 0, Array.Empty<(int, int)>());
}

/// <summary>Counts physical cores and threads per kind from Windows CPU Sets, for the "8 P-cores (16 threads) · 16 E-cores" caption.</summary>
public static class CoreTopology
{
    /// <summary>
    /// One entry per logical processor: its physical core and efficiency class. On a hybrid CPU the highest class is the P-cores.
    /// Returns the summary, or "" when nothing is known.
    /// </summary>
    public static string Summary(IReadOnlyList<(int CoreIndex, int EfficiencyClass)> logical)
    {
        if (logical.Count == 0) return "";
        int top = logical.Max(l => l.EfficiencyClass);
        bool hybrid = logical.Any(l => l.EfficiencyClass != top);
        static string Cores(int n, string kind) => $"{n} {kind}{(n == 1 ? "" : "s")}";
        if (!hybrid)
        {
            int cores = logical.Select(l => l.CoreIndex).Distinct().Count();
            return cores == logical.Count ? Cores(cores, "core") : $"{Cores(cores, "core")} · {logical.Count} threads";
        }
        var p = logical.Where(l => l.EfficiencyClass == top).ToList();
        var e = logical.Where(l => l.EfficiencyClass != top).ToList();
        string Part(List<(int CoreIndex, int EfficiencyClass)> list, string kind)
        {
            int cores = list.Select(l => l.CoreIndex).Distinct().Count();
            return cores == list.Count ? Cores(cores, kind) : $"{Cores(cores, kind)} ({list.Count} threads)";
        }
        return $"{Part(p, "P-core")} · {Part(e, "E-core")}";
    }

    /// <summary>The header form for the Stats page: "PER CORE · 8P + 16E" on a hybrid CPU, "PER CORE / THREADS" otherwise.</summary>
    public static string Header(IReadOnlyList<(int CoreIndex, int EfficiencyClass)> logical)
    {
        if (logical.Count == 0) return "PER CORE / THREADS";
        int top = logical.Max(l => l.EfficiencyClass);
        if (logical.All(l => l.EfficiencyClass == top)) return "PER CORE / THREADS";
        int p = logical.Where(l => l.EfficiencyClass == top).Select(l => l.CoreIndex).Distinct().Count();
        int e = logical.Where(l => l.EfficiencyClass != top).Select(l => l.CoreIndex).Distinct().Count();
        return $"PER CORE · {p}P + {e}E";
    }
}
