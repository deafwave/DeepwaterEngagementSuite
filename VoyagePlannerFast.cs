using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DeepwaterEngagementSuite.VoyagePlannerData;

namespace DeepwaterEngagementSuite;

public class VoyagePlannerFast
{
    private const int GridSize = 3;
    private const int Cells = GridSize * GridSize;
    private const int States = 1 << Cells;

    private static readonly (Direction Dir, int Dr, int Dc)[] Dirs =
    [
        (Direction.Up, 1, 0),
        (Direction.Down, -1, 0),
        (Direction.Left, 0, -1),
        (Direction.Right, 0, 1),
    ];

    private static readonly int[] InGrid = BuildInGrid();
    private const int StartCell = 0 * GridSize + 0;
    private static readonly int[] SacrificeCornerCells = [6, 8, 2];
    private static readonly int[][] TopologiesFull = BuildTopologiesFull();
    private static readonly int[][] TopologiesWithSacrificeCorners = BuildTopologiesWithSacrificeCorners();

    private static int[] BuildInGrid()
    {
        var mask = new int[Cells];
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        foreach (var (dir, dr, dc) in Dirs)
        {
            var nr = r + dr;
            var nc = c + dc;
            if (nr < 0 || nr >= GridSize || nc < 0 || nc >= GridSize) continue;
            mask[r * GridSize + c] |= (int)dir;
        }

        return mask;
    }

    private static List<(int A, int B, Direction Dir)> BuildEdges()
    {
        var edges = new List<(int A, int B, Direction Dir)>();
        for (var r = 0; r < GridSize; r++)
        for (var c = 0; c < GridSize; c++)
        {
            var i = r * GridSize + c;
            if (c < GridSize - 1) edges.Add((i, i + 1, Direction.Right));
            if (r < GridSize - 1) edges.Add((i, i + GridSize, Direction.Up));
        }

        return edges;
    }

    private static int[][] BuildTopologiesFull()
    {
        var edges = BuildEdges();
        var found = new List<int[]>();
        var neighbours = new List<int>[Cells];

        for (var subset = 0; subset < 1 << edges.Count; subset++)
        {
            var cellMask = new int[Cells];
            for (var i = 0; i < Cells; i++) neighbours[i] = [];

            for (var e = 0; e < edges.Count; e++)
            {
                if ((subset >> e & 1) == 0) continue;
                var (a, b, dir) = edges[e];
                cellMask[a] |= (int)dir;
                cellMask[b] |= (int)dir.Opposite();
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }

            if (Reaches(neighbours, requiredMask: States - 1, start: StartCell))
                found.Add(cellMask);
        }

        return found.ToArray();
    }

    private static int[][] BuildTopologiesWithSacrificeCorners()
    {
        var edges = BuildEdges();
        var found = new List<int[]>(TopologiesFull);
        var neighbours = new List<int>[Cells];

        for (var isolateBits = 1; isolateBits < 1 << SacrificeCornerCells.Length; isolateBits++)
        {
            var isolated = 0;
            for (var b = 0; b < SacrificeCornerCells.Length; b++)
            {
                if ((isolateBits >> b & 1) != 0)
                    isolated |= 1 << SacrificeCornerCells[b];
            }

            if ((isolated >> StartCell & 1) != 0) continue;

            var required = (States - 1) & ~isolated;
            if ((required >> StartCell & 1) == 0) continue;

            var usableEdges = new List<(int A, int B, Direction Dir)>();
            foreach (var e in edges)
            {
                if ((isolated >> e.A & 1) != 0 || (isolated >> e.B & 1) != 0) continue;
                usableEdges.Add(e);
            }

            var edgeCount = usableEdges.Count;
            for (var subset = 0; subset < 1 << edgeCount; subset++)
            {
                var cellMask = new int[Cells];
                for (var i = 0; i < Cells; i++) neighbours[i] = [];

                for (var e = 0; e < edgeCount; e++)
                {
                    if ((subset >> e & 1) == 0) continue;
                    var (a, b, dir) = usableEdges[e];
                    cellMask[a] |= (int)dir;
                    cellMask[b] |= (int)dir.Opposite();
                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                }

                if (!Reaches(neighbours, required, StartCell)) continue;
                found.Add(cellMask);
            }
        }

        return found.ToArray();
    }

    private static bool Reaches(List<int>[] neighbours, int requiredMask, int start)
    {
        if ((requiredMask >> start & 1) == 0) return false;
        var seen = 1 << start;
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.TryPop(out var cell))
        {
            foreach (var next in neighbours[cell])
            {
                if ((requiredMask >> next & 1) == 0) continue;
                if ((seen >> next & 1) != 0) continue;
                seen |= 1 << next;
                stack.Push(next);
            }
        }

        return seen == requiredMask;
    }

    public IEnumerable<VoyageSolutionResult> Solve(VoyagePuzzle puzzle, VoyagePlannerSettings settings = null)
    {
        settings ??= new VoyagePlannerSettings();
        var pieces = puzzle.AvailablePieces;
        var n = pieces.Count;
        var topN = Math.Max(1, settings.TopN);

        if (n < Cells)
        {
            yield return new VoyageSolutionResult([], 0, 0);
            yield break;
        }

        var borders = new IReadOnlyList<BorderEffect>[Cells];
        for (var cell = 0; cell < Cells; cell++)
            borders[cell] = puzzle.TileBorders?[cell / GridSize, cell % GridSize] ?? [];

        double TileFactor(int cell, ModifierTag tags)
        {
            double m = 1;
            foreach (var b in borders[cell])
                if (!b.PerConnection && !b.AffectsPlacedChart && ModifierTagParser.Matches(b.Tags, tags))
                    m *= b.Multiplier;
            return m;
        }

        double ChartFactor(int cell, ModifierTag tags)
        {
            double m = 1;
            foreach (var b in borders[cell])
                if (!b.PerConnection && b.AffectsPlacedChart && ModifierTagParser.Matches(b.Tags, tags))
                    m *= b.Multiplier;
            return m;
        }

        double NeighbourTileSum(int cell, ModifierTag tags)
        {
            var r = cell / GridSize;
            var c = cell % GridSize;
            double sum = 0;
            foreach (var (_, dr, dc) in Dirs)
            {
                var nr = r + dr;
                var nc = c + dc;
                if (nr < 0 || nr >= GridSize || nc < 0 || nc >= GridSize) continue;
                sum += TileFactor(nr * GridSize + nc, tags);
            }

            return sum;
        }

        double Global(ModifierTag tags)
        {
            double sum = 0;
            for (var cell = 0; cell < Cells; cell++) sum += TileFactor(cell, tags);
            return sum;
        }

        var optimizeShortestPath = puzzle.OptimizeShortestPath;
        var weight = new double[n][];
        var eligible = new int[n][];
        var rotation = new byte[n][];

        for (var i = 0; i < n; i++)
        {
            var piece = pieces[i];
            weight[i] = new double[Cells];
            eligible[i] = new int[Cells];
            rotation[i] = new byte[Cells * 16];
            rotation[i].AsSpan().Fill(byte.MaxValue);

            for (var cell = 0; cell < Cells; cell++)
            {
                if (optimizeShortestPath)
                {
                    
                    weight[i][cell] = 0;
                    continue;
                }

                double w = 0;
                foreach (var mod in piece.Modifiers)
                {
                    if (mod.Weight == 0) continue;
                    var cf = ChartFactor(cell, mod.Tags);
                    w += mod.IsGlobal
                        ? mod.Weight * cf * Global(mod.Tags)
                        : mod.Weight * cf * NeighbourTileSum(cell, mod.Tags);
                }

                weight[i][cell] = w;
            }

            for (var rot = 0; rot < piece.DistinctRotations; rot++)
            {
                var conn = (int)piece.GetConnections(rot);
                for (var cell = 0; cell < Cells; cell++)
                {
                    if (VoyagePlacementRules.IsCenterOnlyUniqueChart(piece) &&
                        cell != VoyagePlacementRules.CenterRow * GridSize + VoyagePlacementRules.CenterCol)
                        continue;

                    var inGrid = conn & InGrid[cell];
                    var slot = cell * 16 + inGrid;
                    if (rotation[i][slot] != byte.MaxValue) continue;
                    eligible[i][cell] |= 1 << inGrid;
                    rotation[i][slot] = (byte)rot;
                }
            }

            if (VoyagePlacementRules.IsCenterOnlyUniqueChart(piece))
            {
                for (var cell = 0; cell < Cells; cell++)
                {
                    if (cell == VoyagePlacementRules.CenterRow * GridSize + VoyagePlacementRules.CenterCol)
                        continue;
                    weight[i][cell] = double.NegativeInfinity;
                    eligible[i][cell] = 0;
                }
            }
        }

        ApplyLocks(puzzle, pieces, weight, eligible, rotation);

        if (puzzle.AllowSacrificeCornerBorderDeadEnds)
            AllowSacrificeCornerBorderDeadEnds(pieces, eligible, rotation);

        var topologies = puzzle.AllowSacrificeCornerBorderDeadEnds
            ? TopologiesWithSacrificeCorners
            : TopologiesFull;

        var reachable = new int[topologies.Length][];
        var bound = new double[topologies.Length];
        var topoScore = optimizeShortestPath ? new double[topologies.Length] : null;

        for (var t = 0; t < topologies.Length; t++)
        {
            var topo = topologies[t];
            var allow = new int[n];
            var total = 0.0;
            var feasible = true;

            if (puzzle.LockedPlacements is { Count: > 0 })
            {
                foreach (var lp in puzzle.LockedPlacements)
                {
                    var cell = lp.Row * GridSize + lp.Col;
                    if (cell is < 0 or >= Cells)
                    {
                        feasible = false;
                        break;
                    }

                    var pieceIdx = -1;
                    for (var i = 0; i < n; i++)
                    {
                        if (pieces[i].Id != lp.PieceId) continue;
                        pieceIdx = i;
                        break;
                    }

                    if (pieceIdx < 0 || (eligible[pieceIdx][cell] >> topo[cell] & 1) == 0)
                    {
                        feasible = false;
                        break;
                    }
                }
            }

            for (var cell = 0; cell < Cells && feasible; cell++)
            {
                var best = double.NegativeInfinity;
                var any = false;
                for (var i = 0; i < n; i++)
                {
                    if ((eligible[i][cell] >> topo[cell] & 1) == 0) continue;
                    allow[i] |= 1 << cell;
                    any = true;
                    if (weight[i][cell] > best) best = weight[i][cell];
                }

                if (!any)
                    feasible = false;
                else if (!optimizeShortestPath)
                    total += best;
            }

            reachable[t] = allow;
            if (!feasible)
            {
                bound[t] = double.NegativeInfinity;
                if (topoScore != null)
                    topoScore[t] = double.NegativeInfinity;
                continue;
            }

            if (optimizeShortestPath)
            {
                var pathScore = VoyagePathMetrics.ScoreTopology(topo);
                topoScore[t] = pathScore;
                bound[t] = pathScore;
            }
            else
            {
                bound[t] = total;
            }
        }

        var order = Enumerable.Range(0, topologies.Length).OrderByDescending(t => bound[t]).ToArray();

        var dpPrev = new double[States];
        var dpNext = new double[States];
        var choice = new byte[n][];
        for (var i = 0; i < n; i++) choice[i] = new byte[States];

        var top = new List<VoyageSolution>(topN);
        var explored = 0L;
        var pruned = 0L;
        var assignment = new int[Cells];

        for (var o = 0; o < order.Length; o++)
        {
            var t = order[o];

            if (double.IsNegativeInfinity(bound[t]) || (top.Count >= topN && bound[t] <= top[^1].TotalScore))
            {
                pruned += order.Length - o;
                break;
            }

            explored++;
            var assignScore = BestAssignment(n, weight, reachable[t], dpPrev, dpNext, choice, assignment);
            if (double.IsNegativeInfinity(assignScore)) continue;

            var score = optimizeShortestPath ? topoScore[t] : assignScore;
            if (double.IsNegativeInfinity(score)) continue;
            if (top.Count >= topN && score <= top[^1].TotalScore) continue;

            var topo = topologies[t];
            var grid = new MapPiecePlacement[GridSize, GridSize];
            for (var cell = 0; cell < Cells; cell++)
            {
                var piece = pieces[assignment[cell]];
                var rot = rotation[assignment[cell]][cell * 16 + topo[cell]];
                grid[cell / GridSize, cell % GridSize] = new MapPiecePlacement(piece, rot, piece.GetConnections(rot));
            }

            Insert(top, topN, new VoyageSolution(grid, score, true));
        }

        yield return new VoyageSolutionResult(top, explored, pruned);
    }

    private static double BestAssignment(
        int n, double[][] weight, int[] allow, double[] dpPrev, double[] dpNext, byte[][] choice, int[] assignment)
    {
        dpPrev.AsSpan().Fill(double.NegativeInfinity);
        dpPrev[0] = 0;

        for (var i = 0; i < n; i++)
        {
            var mine = choice[i];
            var open = allow[i];
            var w = weight[i];

            Array.Copy(dpPrev, dpNext, States);
            mine.AsSpan().Fill(byte.MaxValue);

            if (open != 0)
            {
                for (var mask = 0; mask < States; mask++)
                {
                    var from = dpPrev[mask];
                    if (double.IsNegativeInfinity(from)) continue;

                    var free = open & ~mask;
                    while (free != 0)
                    {
                        var bit = free & -free;
                        free ^= bit;
                        var cell = BitOperations.TrailingZeroCount(bit);
                        var value = from + w[cell];
                        if (value <= dpNext[mask | bit]) continue;
                        dpNext[mask | bit] = value;
                        mine[mask | bit] = (byte)cell;
                    }
                }
            }

            (dpPrev, dpNext) = (dpNext, dpPrev);
        }

        var full = States - 1;
        if (double.IsNegativeInfinity(dpPrev[full])) return double.NegativeInfinity;

        var state = full;
        for (var i = n - 1; i >= 0; i--)
        {
            var cell = choice[i][state];
            if (cell == byte.MaxValue) continue;
            assignment[cell] = i;
            state ^= 1 << cell;
        }

        return dpPrev[full];
    }

    private static void Insert(List<VoyageSolution> top, int topN, VoyageSolution solution)
    {
        var at = top.Count;
        for (var i = 0; i < top.Count; i++)
        {
            if (solution.TotalScore <= top[i].TotalScore) continue;
            at = i;
            break;
        }

        top.Insert(at, solution);
        if (top.Count > topN) top.RemoveAt(top.Count - 1);
    }

    private static void AllowSacrificeCornerBorderDeadEnds(
        List<MapPiece> pieces,
        int[][] eligible,
        byte[][] rotation)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            foreach (var cell in SacrificeCornerCells)
            {
                for (var rot = 0; rot < piece.DistinctRotations; rot++)
                {
                    var conn = (int)piece.GetConnections(rot);
                    if (conn == 0 || (conn & InGrid[cell]) != 0) continue;
                    var slot = cell * 16 + 0;
                    if (rotation[i][slot] == byte.MaxValue)
                        rotation[i][slot] = (byte)rot;
                    eligible[i][cell] |= 1;
                }
            }
        }
    }

    private static void ApplyLocks(
        VoyagePuzzle puzzle,
        List<MapPiece> pieces,
        double[][] weight,
        int[][] eligible,
        byte[][] rotation)
    {
        if (puzzle.LockedPlacements is not { Count: > 0 })
            return;

        var idToIndex = new Dictionary<int, int>(pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
            idToIndex[pieces[i].Id] = i;

        foreach (var lp in puzzle.LockedPlacements)
        {
            if (!idToIndex.TryGetValue(lp.PieceId, out var pieceIdx))
                continue;

            var cell = lp.Row * GridSize + lp.Col;
            if (cell is < 0 or >= Cells)
                continue;

            for (var c = 0; c < Cells; c++)
            {
                if (c == cell) continue;
                eligible[pieceIdx][c] = 0;
                weight[pieceIdx][c] = double.NegativeInfinity;
            }

            for (var i = 0; i < pieces.Count; i++)
            {
                if (i == pieceIdx) continue;
                eligible[i][cell] = 0;
                weight[i][cell] = double.NegativeInfinity;
            }

            var piece = pieces[pieceIdx];
            if (lp.Rotation is { } fixedRot)
            {
                var conn = (int)piece.GetConnections(fixedRot);
                var inGrid = conn & InGrid[cell];
                eligible[pieceIdx][cell] = 1 << inGrid;
                rotation[pieceIdx][cell * 16 + inGrid] = (byte)fixedRot;
            }
            else if (eligible[pieceIdx][cell] == 0)
            {
                for (var rot = 0; rot < piece.DistinctRotations; rot++)
                {
                    var conn = (int)piece.GetConnections(rot);
                    var inGrid = conn & InGrid[cell];
                    var slot = cell * 16 + inGrid;
                    eligible[pieceIdx][cell] |= 1 << inGrid;
                    if (rotation[pieceIdx][slot] == byte.MaxValue)
                        rotation[pieceIdx][slot] = (byte)rot;
                }
            }

            if (double.IsNegativeInfinity(weight[pieceIdx][cell]))
                weight[pieceIdx][cell] = 0;
        }
    }
}
