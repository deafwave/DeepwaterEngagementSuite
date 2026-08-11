using System;
using System.Collections.Generic;

namespace DeepwaterEngagementSuite.VoyagePlannerData;


public static class VoyagePathMetrics
{
    public const int GridSize = 3;
    public const int Cells = GridSize * GridSize;
    public const int StartCell = 0;

    private const int Unreachable = 1_000_000;

    private static readonly (Direction Dir, int Dr, int Dc)[] Dirs =
    [
        (Direction.Up, 1, 0),
        (Direction.Down, -1, 0),
        (Direction.Left, 0, -1),
        (Direction.Right, 0, 1),
    ];

    
    public static double ScoreTopology(int[] cellMask)
    {
        var metrics = AnalyzeTopology(cellMask);
        if (!metrics.IsConnected || metrics.VisitPathLength >= Unreachable)
            return double.NegativeInfinity;

        
        return 10_000_000.0
               - metrics.VisitPathLength * 100_000.0
               - metrics.InternalDeadEnds * 1_000.0
               + metrics.InGridEdgeCount;
    }

    
    public static double ScoreGrid(MapPiecePlacement[,] grid)
    {
        if (grid == null)
            return double.NegativeInfinity;

        var mask = BuildInGridMask(grid);
        return ScoreTopology(mask);
    }

    public static PathMetrics AnalyzeTopology(int[] cellMask)
    {
        if (cellMask is not { Length: Cells })
            return new PathMetrics(false, Unreachable, Cells, 0);

        var adj = new List<int>[Cells];
        for (var i = 0; i < Cells; i++)
            adj[i] = [];

        var edges = 0;
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            var cell = r * GridSize + c;
            var conn = (Direction)cellMask[cell];
            foreach (var (dir, dr, dc) in Dirs)
            {
                if (!conn.HasFlag(dir)) continue;
                var nr = r + dr;
                var nc = c + dc;
                if (nr is < 0 or >= GridSize || nc is < 0 or >= GridSize)
                    continue;

                var next = nr * GridSize + nc;
                if (next < cell) continue;
                if (!((Direction)cellMask[next]).HasFlag(dir.Opposite()))
                    continue;

                adj[cell].Add(next);
                adj[next].Add(cell);
                edges++;
            }
        }

        var deadEnds = 0;
        for (var i = 0; i < Cells; i++)
        {
            if (adj[i].Count == 1)
                deadEnds++;
        }

        var connected = IsConnected(adj);
        var pathLen = connected ? MinVisitPathLength(adj, StartCell) : Unreachable;
        return new PathMetrics(connected, pathLen, deadEnds, edges);
    }

    public static int[] BuildInGridMask(MapPiecePlacement[,] grid)
    {
        var mask = new int[Cells];
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            var placement = grid[r, c];
            if (placement == null)
                continue;

            var cell = r * GridSize + c;
            var conn = placement.Connections;
            foreach (var (dir, dr, dc) in Dirs)
            {
                if (!conn.HasFlag(dir)) continue;
                var nr = r + dr;
                var nc = c + dc;
                if (nr is < 0 or >= GridSize || nc is < 0 or >= GridSize)
                    continue;

                var neighbor = grid[nr, nc];
                if (neighbor == null) continue;
                if (!neighbor.Connections.HasFlag(dir.Opposite())) continue;
                mask[cell] |= (int)dir;
            }
        }

        return mask;
    }

    private static bool IsConnected(List<int>[] adj)
    {
        var seen = 1 << StartCell;
        var stack = new Stack<int>();
        stack.Push(StartCell);
        var count = 1;
        while (stack.TryPop(out var cell))
        {
            foreach (var next in adj[cell])
            {
                if ((seen >> next & 1) != 0) continue;
                seen |= 1 << next;
                count++;
                stack.Push(next);
            }
        }

        return count == Cells;
    }

    private static int MinVisitPathLength(List<int>[] adj, int start)
    {
        var full = (1 << Cells) - 1;
        var states = 1 << Cells;
        var dp = new int[states * Cells];
        Array.Fill(dp, Unreachable);
        dp[Index(1 << start, start)] = 0;

        for (var mask = 0; mask < states; mask++)
        {
            for (var v = 0; v < Cells; v++)
            {
                if ((mask >> v & 1) == 0) continue;
                var cur = dp[Index(mask, v)];
                if (cur >= Unreachable) continue;

                foreach (var next in adj[v])
                {
                    var nextMask = mask | (1 << next);
                    var idx = Index(nextMask, next);
                    var cand = cur + 1;
                    if (cand < dp[idx])
                        dp[idx] = cand;
                }
            }
        }

        var best = Unreachable;
        for (var v = 0; v < Cells; v++)
            best = Math.Min(best, dp[Index(full, v)]);
        return best;
    }

    private static int Index(int mask, int cell) => mask * Cells + cell;

    public readonly record struct PathMetrics(
        bool IsConnected,
        int VisitPathLength,
        int InternalDeadEnds,
        int InGridEdgeCount);
}
