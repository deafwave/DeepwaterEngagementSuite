using System.Numerics;
using Xunit;

namespace DeepwaterEngagementSuite.Tests;

public class GridRegionTests
{
    private const int Walkable = 5;
    private const int Blocked = 0;

    /// <summary>Pathfinding array indexed [y][x], everything blocked.</summary>
    private static int[][] EmptyMap(int width, int height)
    {
        var map = new int[height][];
        for (var y = 0; y < height; y++)
            map[y] = new int[width];
        return map;
    }

    private static void Carve(int[][] map, int minX, int minY, int maxX, int maxY)
    {
        for (var y = minY; y < maxY; y++)
        for (var x = minX; x < maxX; x++)
            map[y][x] = Walkable;
    }

    [Fact]
    public void PicksTheLargestWalkableComponentAndIgnoresTheBoat()
    {
        var map = EmptyMap(240, 240);
        Carve(map, 8, 8, 24, 24); // the boat: a small island, disconnected
        Carve(map, 80, 80, 160, 160); // the charts: the real playable region

        Assert.True(GridRegion.TryComputeLargestWalkableBounds(map, out var origin, out var size));

        // Downsample lands on samples 10..19 on both axes; padded by 12 and clamped to the map.
        Assert.Equal(new Vector2(68, 68), origin);
        Assert.Equal(new Vector2(104, 104), size);
    }

    [Fact]
    public void TreatsValuesAtOrBelowTheWalkableThresholdAsBlocked()
    {
        var map = EmptyMap(240, 240);
        Carve(map, 80, 80, 160, 160);
        for (var y = 0; y < 240; y++)
        for (var x = 0; x < 240; x++)
            if (map[y][x] == Blocked)
                map[y][x] = GridRegion.WalkableThreshold; // "> 3" is walkable, "== 3" is not

        Assert.True(GridRegion.TryComputeLargestWalkableBounds(map, out var origin, out var size));
        Assert.Equal(new Vector2(68, 68), origin);
        Assert.Equal(new Vector2(104, 104), size);
    }

    [Fact]
    public void FailsOnAnUnusableTerrainArray()
    {
        Assert.False(GridRegion.TryComputeLargestWalkableBounds(null, out _, out _));
        Assert.False(GridRegion.TryComputeLargestWalkableBounds([], out _, out _));
        Assert.False(GridRegion.TryComputeLargestWalkableBounds(EmptyMap(16, 16), out _, out _)); // too small to downsample
        Assert.False(GridRegion.TryComputeLargestWalkableBounds(EmptyMap(240, 240), out _, out _)); // nothing walkable
    }

    [Fact]
    public void MapsCornersToBoardCellsWithRowZeroAtTheBottom()
    {
        var origin = new Vector2(68, 68);
        var size = new Vector2(104, 104);

        // World Y grows north, and the board calls the southern row 0, so low Y is row 0.
        Assert.Equal(0, GridRegion.CellIndex(new Vector2(69, 69), origin, size)); // (row 0, col 0)
        Assert.Equal(2, GridRegion.CellIndex(new Vector2(171, 69), origin, size)); // (row 0, col 2)
        Assert.Equal(6, GridRegion.CellIndex(new Vector2(69, 171), origin, size)); // (row 2, col 0)
        Assert.Equal(8, GridRegion.CellIndex(new Vector2(171, 171), origin, size)); // (row 2, col 2)
        Assert.Equal(4, GridRegion.CellIndex(new Vector2(120, 120), origin, size)); // (row 1, col 1)
    }

    [Fact]
    public void RejectsPositionsOutsideTheRegion()
    {
        var origin = new Vector2(68, 68);
        var size = new Vector2(104, 104);

        Assert.Equal(-1, GridRegion.CellIndex(new Vector2(0, 120), origin, size)); // on the boat, west of the charts
        Assert.Equal(-1, GridRegion.CellIndex(new Vector2(120, 240), origin, size));
        Assert.Equal(-1, GridRegion.CellIndex(new Vector2(120, 120), origin, Vector2.Zero)); // region not computed yet

        // A sliver of slack keeps the edge of the region inside its own cell.
        Assert.Equal(0, GridRegion.CellIndex(new Vector2(67, 67), origin, size));
    }
}
