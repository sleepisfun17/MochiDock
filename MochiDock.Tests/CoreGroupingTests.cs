using MochiDock.Core;

public class CoreGroupingTests
{
    [Fact]
    public void SixteenPAndSixteenEGetARowEach()
    {
        var grid = CoreBarLayout.GroupedGrid(190, 76, new[] { 16, 16 }, 3);
        Assert.Equal((2, 16), (grid.Rows, grid.Columns));
        Assert.All(grid.Cells.Take(16), c => Assert.Equal(0, c.Row));
        Assert.All(grid.Cells.Skip(16), c => Assert.Equal(1, c.Row));
    }

    [Fact]
    public void KindsNeverShareARow()
    {
        // 6 P-cores with Hyper-Threading (12 threads) and 8 E-cores: one grid of 20 would put E-cores in the P row.
        var grid = CoreBarLayout.GroupedGrid(190, 76, new[] { 12, 8 }, 3);
        var pRows = grid.Cells.Take(12).Select(c => c.Row).ToHashSet();
        var eRows = grid.Cells.Skip(12).Select(c => c.Row).ToHashSet();
        Assert.Empty(pRows.Intersect(eRows));
        Assert.Equal(20, grid.Cells.Distinct().Count());
        Assert.All(grid.Cells, c => Assert.InRange(c.Column, 0, grid.Columns - 1));
        int usedWidth = grid.CellWidth * grid.Columns + grid.Gap * (grid.Columns - 1), usedHeight = grid.CellHeight * grid.Rows + grid.Gap * (grid.Rows - 1);
        Assert.True(usedWidth <= 190 && usedHeight <= 76);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(64)]
    public void OneGroupIsTheGrid(int count)
    {
        var grid = CoreBarLayout.GroupedGrid(190, 46, new[] { count }, 3);
        var plain = CoreBarLayout.Grid(190, 46, count, 3);
        Assert.Equal((plain.Rows, plain.Columns, plain.CellWidth, plain.CellHeight, plain.Gap, plain.Left, plain.Top),
            (grid.Rows, grid.Columns, grid.CellWidth, grid.CellHeight, grid.Gap, grid.Left, grid.Top));
    }

    [Fact]
    public void ATallerBoxGivesTallerCells()
    {
        Assert.True(CoreBarLayout.GroupedGrid(190, 76, new[] { 16, 16 }, 3).CellHeight > CoreBarLayout.GroupedGrid(190, 46, new[] { 16, 16 }, 3).CellHeight);
        Assert.Same(CoreGrid.Empty, CoreBarLayout.GroupedGrid(190, 46, new[] { 0 }, 3));
    }

    [Fact]
    public void SummaryCountsPhysicalCoresPerKind()
    {
        // i9-14900: 8 P-cores with two threads each (class 1), then 16 E-cores (class 0).
        var i9 = Enumerable.Range(0, 16).Select(i => (i / 2, 1)).Concat(Enumerable.Range(0, 16).Select(i => (8 + i, 0))).ToList();
        Assert.Equal("8 P-cores (16 threads) · 16 E-cores", CoreTopology.Summary(i9));
        var ryzen = Enumerable.Range(0, 32).Select(i => (i / 2, 0)).ToList();
        Assert.Equal("16 cores · 32 threads", CoreTopology.Summary(ryzen));
        Assert.Equal("4 cores", CoreTopology.Summary(Enumerable.Range(0, 4).Select(i => (i, 0)).ToList()));
        Assert.Equal("", CoreTopology.Summary(new List<(int, int)>()));
        Assert.Equal("PER CORE · 8P + 16E", CoreTopology.Header(i9));
        Assert.Equal("PER CORE / THREADS", CoreTopology.Header(ryzen));
    }
}
