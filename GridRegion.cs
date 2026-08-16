using System;
using System.Collections.Generic;
using System.Numerics;

namespace DeepwaterEngagementSuite;

/// <summary>
/// Playable-region geometry for the grid tracker. Deliberately free of ExileCore types so it
/// can be unit tested without a running game.
/// </summary>
public static class GridRegion
{
    /// <summary>Pathfinding cells above this value are walkable.</summary>
    public const int WalkableThreshold = 3;

    /// <summary>The terrain scan samples every Nth cell — the region only needs to be roughly right.</summary>
    private const int DownsampleStep = 8;

    /// <summary>Grid units of slack around the terrain so the region does not clip the outer walls.</summary>
    private const float Padding = 12f;

    /// <summary>Slack on region membership so a position on the border still lands in its own cell.</summary>
    private const float MembershipSlack = 0.02f;

    /// <summary>
    /// Finds the bounding box of the largest connected walkable region. In a voyage this is the
    /// nine stitched charts; the boat is a separate, much smaller component and is excluded, as is
    /// the empty padding the area dimensions would otherwise include.
    /// </summary>
    public static bool TryComputeLargestWalkableBounds(int[][] pathfinding, out Vector2 origin, out Vector2 size)
    {
        origin = default;
        size = default;

        if (pathfinding is not { Length: > 0 } || pathfinding[0] is not { Length: > 0 })
        {
            return false;
        }

        var height = pathfinding.Length;
        var width = pathfinding[0].Length;
        var sampleHeight = height / DownsampleStep;
        var sampleWidth = width / DownsampleStep;
        if (sampleHeight < 3 || sampleWidth < 3)
        {
            return false;
        }

        var walkable = new bool[sampleHeight, sampleWidth];
        for (var y = 0; y < sampleHeight; y++)
        {
            var row = pathfinding[y * DownsampleStep];
            for (var x = 0; x < sampleWidth; x++)
            {
                walkable[y, x] = row[x * DownsampleStep] > WalkableThreshold;
            }
        }

        var visited = new bool[sampleHeight, sampleWidth];
        var bestCount = 0;
        int bestMinX = 0, bestMinY = 0, bestMaxX = 0, bestMaxY = 0;
        var stack = new Stack<(int Y, int X)>();
        for (var startY = 0; startY < sampleHeight; startY++)
        {
            for (var startX = 0; startX < sampleWidth; startX++)
            {
                if (!walkable[startY, startX] || visited[startY, startX])
                {
                    continue;
                }

                var count = 0;
                int minX = startX, minY = startY, maxX = startX, maxY = startY;
                stack.Push((startY, startX));
                visited[startY, startX] = true;
                while (stack.TryPop(out var cur))
                {
                    count++;
                    minX = Math.Min(minX, cur.X);
                    minY = Math.Min(minY, cur.Y);
                    maxX = Math.Max(maxX, cur.X);
                    maxY = Math.Max(maxY, cur.Y);
                    Span<(int Y, int X)> neighbours =
                        [(cur.Y - 1, cur.X), (cur.Y + 1, cur.X), (cur.Y, cur.X - 1), (cur.Y, cur.X + 1)];
                    foreach (var (ny, nx) in neighbours)
                    {
                        if (ny >= 0 && ny < sampleHeight && nx >= 0 && nx < sampleWidth &&
                            walkable[ny, nx] && !visited[ny, nx])
                        {
                            visited[ny, nx] = true;
                            stack.Push((ny, nx));
                        }
                    }
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    bestMinX = minX;
                    bestMinY = minY;
                    bestMaxX = maxX;
                    bestMaxY = maxY;
                }
            }
        }

        if (bestCount == 0)
        {
            return false;
        }

        var originX = Math.Max(0, bestMinX * DownsampleStep - Padding);
        var originY = Math.Max(0, bestMinY * DownsampleStep - Padding);
        origin = new Vector2(originX, originY);
        size = new Vector2(
            Math.Min(width, (bestMaxX + 1) * DownsampleStep + Padding) - originX,
            Math.Min(height, (bestMaxY + 1) * DownsampleStep + Padding) - originY);
        return true;
    }

    /// <summary>
    /// The board cell a grid position falls in, as row * 3 + col, or -1 when it is outside the
    /// region. Rows follow the voyage board: row 0 is the southern row, matching the ascii grid's
    /// <c>grid[2 - rowFromTop, col]</c> indexing.
    /// </summary>
    public static int CellIndex(Vector2 gridPos, Vector2 origin, Vector2 size)
    {
        if (size.X <= 0 || size.Y <= 0)
        {
            return -1;
        }

        var nx = (gridPos.X - origin.X) / size.X;
        var ny = (gridPos.Y - origin.Y) / size.Y;
        if (nx < -MembershipSlack || nx > 1 + MembershipSlack ||
            ny < -MembershipSlack || ny > 1 + MembershipSlack)
        {
            return -1; // on the boat or in the padding around the charts
        }

        var col = Math.Clamp((int)(nx * 3), 0, 2);
        var row = Math.Clamp((int)(ny * 3), 0, 2);
        return row * 3 + col;
    }
}
