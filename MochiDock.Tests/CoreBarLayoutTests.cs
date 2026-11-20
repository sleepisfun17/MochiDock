using MochiDock.Core;

public class CoreBarLayoutTests
{
    [Theory]
    [InlineData(190, 32, 3)]
    [InlineData(238, 32, 4)]   // 125 %
    [InlineData(285, 32, 5)]   // 150 %
    [InlineData(380, 32, 6)]   // 200 %
    [InlineData(190, 4, 3)]
    [InlineData(190, 8, 3)]
    [InlineData(190, 16, 3)]
    [InlineData(190, 24, 3)]
    [InlineData(190, 64, 3)]
    [InlineData(191, 7, 3)]
    public void BarsAreEqualWholePixelsThatFitAndSitCentred(double width, int count, double gap)
    {
        var (bar, g, left) = CoreBarLayout.Compute(width, count, gap);
        Assert.True(bar >= 1);
        Assert.True(g >= 1);
        int used = bar * count + g * (count - 1);
        Assert.True(used <= (int)width, $"{used} > {width}");
        int right = (int)width - used - left;
        Assert.InRange(right - left, 0, 1);   // centred to the pixel
    }

    [Fact]
    public void ThirtyTwoCoresGetTheSameWidthEverywhere()
    {
        var (bar, gap, _) = CoreBarLayout.Compute(190, 32, 3);
        Assert.Equal(3, bar);
        Assert.Equal(3, gap);
    }

    [Fact]
    public void ACrowdedRowShrinksTheGapBeforeTheBars()
    {
        var (bar, gap, _) = CoreBarLayout.Compute(150, 64, 3);
        Assert.True(bar >= 1);
        Assert.Equal(1, gap);
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(190, 0)]
    [InlineData(-5, 8)]
    public void NothingToDrawGivesZero(double width, int count) => Assert.Equal((0, 0, 0), CoreBarLayout.Compute(width, count, 3));

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void GridCellsAreEqualWholePixelsThatFitAndSitCentred(int count)
    {
        var (rows, columns, cellWidth, cellHeight, gap, left, top) = CoreBarLayout.Grid(190, 46, count, 3);
        Assert.True(rows >= 1 && columns >= 1);
        Assert.True(rows * columns >= count, $"{rows} x {columns} < {count}");
        Assert.True((rows - 1) * columns < count, "an empty row");
        Assert.True(cellWidth >= 1 && cellHeight >= 1 && gap >= 1);
        int usedWidth = cellWidth * columns + gap * (columns - 1), usedHeight = cellHeight * rows + gap * (rows - 1);
        Assert.True(usedWidth <= 190, $"{usedWidth} > 190");
        Assert.True(usedHeight <= 46, $"{usedHeight} > 46");
        Assert.InRange((190 - usedWidth - left) - left, 0, 1);   // centred to the pixel
        Assert.InRange((46 - usedHeight - top) - top, 0, 1);
    }

    [Theory]
    [InlineData(8, 1, 8)]
    [InlineData(16, 1, 16)]
    [InlineData(24, 2, 12)]
    [InlineData(32, 2, 16)]
    [InlineData(64, 4, 16)]
    public void GridWrapsPastSixteenPerRow(int count, int expectedRows, int expectedColumns)
    {
        var grid = CoreBarLayout.Grid(190, 46, count, 3);
        Assert.Equal(expectedRows, grid.Rows);
        Assert.Equal(expectedColumns, grid.Columns);
        Assert.True(grid.CellWidth >= CoreBarLayout.MinCellWidth);
        Assert.True(grid.CellHeight >= CoreBarLayout.MinCellHeight);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ThirtyTwoThreadsKeepTwoRowsAtEveryZoom(double zoom)
    {
        var grid = CoreBarLayout.Grid(Math.Round(190 * zoom), Math.Round(46 * zoom), 32, 3 * zoom);
        Assert.Equal((2, 16), (grid.Rows, grid.Columns));
    }

    [Fact]
    public void OneRowGridMatchesTheBars()
    {
        var grid = CoreBarLayout.Grid(190, 46, 8, 3);
        var (bar, gap, left) = CoreBarLayout.Compute(190, 8, 3);
        Assert.Equal((bar, gap, left), (grid.CellWidth, grid.Gap, grid.Left));
        Assert.Equal(46, grid.CellHeight);
    }

    [Theory]
    [InlineData(0, 46, 32)]
    [InlineData(190, 0, 32)]
    [InlineData(190, 46, 0)]
    public void EmptyGridGivesZero(double width, double height, int count) =>
        Assert.Equal((0, 0, 0, 0, 0, 0, 0), CoreBarLayout.Grid(width, height, count, 3));
}
