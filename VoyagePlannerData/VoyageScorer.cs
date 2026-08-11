using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepwaterEngagementSuite.VoyagePlannerData;

public class VoyageScorer
{
    private const int GridSize = 3;
    private const int CellCount = GridSize * GridSize;
    private const int MaxConn = 4;
    private const int MaxSlot = MaxConn + 1;

    private static readonly (int Dr, int Dc)[] NeighborOffsets = [(1, 0), (-1, 0), (0, -1), (0, 1)];

    private readonly record struct ModEntry(int MaskIdx, double Weight);

    private readonly int _maskCount;
    private readonly Dictionary<MapPiece, int> _pieceIndex = new();

    private readonly double[][][] _tileMult;
    private readonly double[][][] _chartMult;

    private readonly double[][] _tileMultMax;

    private readonly ModEntry[][] _localEntries;
    private readonly ModEntry[][] _globalEntries;

    private readonly double[] _pieceGlobalUpperBound;

    private readonly double[][][] _deliverMax;

    private readonly double[] _sScratch;
    private readonly int[] _connScratch = new int[CellCount];
    private readonly List<double> _globalsScratch = [];
    private readonly Dictionary<ModifierTag, int> _maskIndex;
    private readonly IReadOnlyList<BorderEffect>[] _bordersByCell = new IReadOnlyList<BorderEffect>[CellCount];
    private readonly bool _optimizeShortestPath;

    public VoyageScorer(VoyagePuzzle puzzle)
    {
        _optimizeShortestPath = puzzle.OptimizeShortestPath;
        var pieces = puzzle.AvailablePieces;

        var maskIndex = new Dictionary<ModifierTag, int>();
        foreach (var mod in pieces.SelectMany(p => p.Modifiers))
        {
            if (!maskIndex.ContainsKey(mod.Tags))
                maskIndex[mod.Tags] = maskIndex.Count;
        }

        if (maskIndex.Count == 0)
            maskIndex[ModifierTag.None] = 0;

        _maskIndex = maskIndex;
        _maskCount = maskIndex.Count;
        _sScratch = new double[_maskCount];
        var masks = new ModifierTag[_maskCount];
        foreach (var (mask, idx) in maskIndex)
            masks[idx] = mask;

        _tileMult = new double[CellCount][][];
        _chartMult = new double[CellCount][][];
        _tileMultMax = new double[CellCount][];
        var chartMultMax = new double[CellCount][];
        for (var cell = 0; cell < CellCount; cell++)
        {
            var borders = puzzle.TileBorders?[cell / GridSize, cell % GridSize] ?? [];
            _bordersByCell[cell] = borders;
            _tileMult[cell] = new double[_maskCount][];
            _chartMult[cell] = new double[_maskCount][];
            _tileMultMax[cell] = new double[_maskCount];
            chartMultMax[cell] = new double[_maskCount];
            for (var mi = 0; mi < _maskCount; mi++)
            {
                var tileByConn = new double[MaxConn + 1];
                var chartByConn = new double[MaxConn + 1];
                for (var n = 0; n <= MaxConn; n++)
                {
                    double tile = 1, chart = 1;
                    foreach (var b in borders)
                    {
                        if (!ModifierTagParser.Matches(b.Tags, masks[mi]))
                            continue;

                        var m = b.PerConnection ? Math.Max(0, 1 + (b.Multiplier - 1) * n) : b.Multiplier;
                        if (b.AffectsPlacedChart)
                            chart *= m;
                        else
                            tile *= m;
                    }

                    tileByConn[n] = tile;
                    chartByConn[n] = chart;
                }

                _tileMult[cell][mi] = tileByConn;
                _chartMult[cell][mi] = chartByConn;
                _tileMultMax[cell][mi] = tileByConn.Skip(1).Max();
                chartMultMax[cell][mi] = chartByConn.Skip(1).Max();
            }
        }

        var sGlobalMax = new double[_maskCount];
        var chartMaxOverCells = new double[_maskCount];
        for (var mi = 0; mi < _maskCount; mi++)
        {
            for (var cell = 0; cell < CellCount; cell++)
            {
                sGlobalMax[mi] += _tileMultMax[cell][mi];
                chartMaxOverCells[mi] = Math.Max(chartMaxOverCells[mi], chartMultMax[cell][mi]);
            }
        }

        _localEntries = new ModEntry[pieces.Count][];
        _globalEntries = new ModEntry[pieces.Count][];
        _pieceGlobalUpperBound = new double[pieces.Count];
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            _pieceIndex[piece] = i;
            _localEntries[i] = BuildEntries(piece.Modifiers, maskIndex, isGlobal: false);
            _globalEntries[i] = BuildEntries(piece.Modifiers, maskIndex, isGlobal: true);
            _pieceGlobalUpperBound[i] = _globalEntries[i]
                .Sum(e => e.Weight * chartMaxOverCells[e.MaskIdx] * sGlobalMax[e.MaskIdx]);
        }

        _deliverMax = new double[CellCount][][];
        for (var from = 0; from < CellCount; from++)
        {
            _deliverMax[from] = new double[CellCount][];
            var fr = from / GridSize;
            var fc = from % GridSize;
            foreach (var (dr, dc) in NeighborOffsets)
            {
                var tr = fr + dr;
                var tc = fc + dc;
                if (tr < 0 || tr >= GridSize || tc < 0 || tc >= GridSize)
                    continue;

                var to = tr * GridSize + tc;
                var bySlot = new double[MaxSlot + 1];
                for (var slot = 0; slot <= MaxSlot; slot++)
                {
                    double best = 0;
                    for (var i = 0; i < pieces.Count; i++)
                    {
                        double v = 0;
                        foreach (var e in _localEntries[i])
                        {
                            var tileM = slot == MaxSlot
                                ? _tileMultMax[to][e.MaskIdx]
                                : _tileMult[to][e.MaskIdx][slot];
                            v += e.Weight * chartMultMax[from][e.MaskIdx] * tileM;
                        }

                        best = Math.Max(best, v);
                    }

                    bySlot[slot] = best;
                }

                _deliverMax[from][to] = bySlot;
            }
        }
    }

    private static ModEntry[] BuildEntries(IEnumerable<Modifier> mods, Dictionary<ModifierTag, int> maskIndex, bool isGlobal)
    {
        return mods
            .Where(m => m.IsGlobal == isGlobal && m.Weight != 0)
            .GroupBy(m => maskIndex[m.Tags])
            .Select(g => new ModEntry(g.Key, g.Sum(m => m.Weight)))
            .ToArray();
    }

    public double Score(MapPiecePlacement[,] grid) => ScoreInternal(grid, null);

    public double[,] CellScores(MapPiecePlacement[,] grid)
    {
        var cells = new double[GridSize, GridSize];
        ScoreInternal(grid, cells);
        return cells;
    }

    private double ScoreInternal(MapPiecePlacement[,] grid, double[,] cellsOut)
    {
        if (_optimizeShortestPath)
        {
            var pathScore = VoyagePathMetrics.ScoreGrid(grid);
            if (cellsOut != null)
            {
                var metrics = VoyagePathMetrics.AnalyzeTopology(VoyagePathMetrics.BuildInGridMask(grid));
                for (var r = 0; r < GridSize; r++)
                for (var c = 0; c < GridSize; c++)
                {
                    
                    cellsOut[r, c] = metrics.IsConnected
                        ? pathScore / CellCount
                        : double.NegativeInfinity;
                }
            }

            return pathScore;
        }

        var conn = _connScratch;
        for (var cell = 0; cell < CellCount; cell++)
            conn[cell] = grid[cell / GridSize, cell % GridSize].Connections.CountConnections();

        var s = _sScratch;
        for (var mi = 0; mi < _maskCount; mi++)
        {
            double sum = 0;
            for (var cell = 0; cell < CellCount; cell++)
                sum += _tileMult[cell][mi][conn[cell]];
            s[mi] = sum;
        }

        double score = 0;
        for (var cell = 0; cell < CellCount; cell++)
        {
            var r = cell / GridSize;
            var c = cell % GridSize;
            double cellScore = 0;

            foreach (var (dr, dc) in NeighborOffsets)
            {
                var nr = r + dr;
                var nc = c + dc;
                if (nr < 0 || nr >= GridSize || nc < 0 || nc >= GridSize)
                    continue;

                var u = nr * GridSize + nc;
                var pi = _pieceIndex[grid[nr, nc].Piece];
                foreach (var e in _localEntries[pi])
                {
                    cellScore += e.Weight * _chartMult[u][e.MaskIdx][conn[u]] * _tileMult[cell][e.MaskIdx][conn[cell]];
                }
            }

            var piSelf = _pieceIndex[grid[r, c].Piece];
            foreach (var e in _globalEntries[piSelf])
            {
                cellScore += e.Weight * _chartMult[cell][e.MaskIdx][conn[cell]] * s[e.MaskIdx];
            }

            score += cellScore;
            if (cellsOut != null)
                cellsOut[r, c] = cellScore;
        }

        return score;
    }

    public double UpperBound(MapPiecePlacement[,] grid, bool[] pieceUsed, int filledCount)
    {
        if (_optimizeShortestPath)
        {
            
            return 10_000_000.0 - 8 * 100_000.0 + 12;
        }

        var conn = _connScratch;
        for (var cell = 0; cell < CellCount; cell++)
        {
            var placement = grid[cell / GridSize, cell % GridSize];
            conn[cell] = placement?.Connections.CountConnections() ?? -1;
        }

        var s = _sScratch;
        for (var mi = 0; mi < _maskCount; mi++)
        {
            double sum = 0;
            for (var cell = 0; cell < CellCount; cell++)
            {
                sum += conn[cell] >= 0
                    ? _tileMult[cell][mi][conn[cell]]
                    : _tileMultMax[cell][mi];
            }

            s[mi] = sum;
        }

        double score = 0;
        for (var cell = 0; cell < CellCount; cell++)
        {
            var r = cell / GridSize;
            var c = cell % GridSize;

            foreach (var (dr, dc) in NeighborOffsets)
            {
                var nr = r + dr;
                var nc = c + dc;
                if (nr < 0 || nr >= GridSize || nc < 0 || nc >= GridSize)
                    continue;

                var u = nr * GridSize + nc;
                if (conn[u] >= 0)
                {
                    var pi = _pieceIndex[grid[nr, nc].Piece];
                    foreach (var e in _localEntries[pi])
                    {
                        var tileM = conn[cell] >= 0
                            ? _tileMult[cell][e.MaskIdx][conn[cell]]
                            : _tileMultMax[cell][e.MaskIdx];
                        score += e.Weight * _chartMult[u][e.MaskIdx][conn[u]] * tileM;
                    }
                }
                else
                {
                    score += _deliverMax[u][cell][conn[cell] >= 0 ? conn[cell] : MaxSlot];
                }
            }

            if (conn[cell] >= 0)
            {
                var pi = _pieceIndex[grid[r, c].Piece];
                foreach (var e in _globalEntries[pi])
                {
                    score += e.Weight * _chartMult[cell][e.MaskIdx][conn[cell]] * s[e.MaskIdx];
                }
            }
        }

        var remaining = CellCount - filledCount;
        if (remaining > 0)
        {
            _globalsScratch.Clear();
            for (var i = 0; i < pieceUsed.Length; i++)
            {
                if (!pieceUsed[i] && _pieceGlobalUpperBound[i] > 0)
                    _globalsScratch.Add(_pieceGlobalUpperBound[i]);
            }

            _globalsScratch.Sort((a, b) => b.CompareTo(a));
            var take = Math.Min(remaining, _globalsScratch.Count);
            for (var i = 0; i < take; i++)
                score += _globalsScratch[i];
        }

        return score;
    }

    public IReadOnlyList<BorderEffect> BordersAt(int row, int col) => _bordersByCell[row * GridSize + col];

    public List<ScoreContribution>[,] Explain(MapPiecePlacement[,] grid)
    {
        if (_optimizeShortestPath)
        {
            var metrics = VoyagePathMetrics.AnalyzeTopology(VoyagePathMetrics.BuildInGridMask(grid));
            var pathScore = VoyagePathMetrics.ScoreTopology(VoyagePathMetrics.BuildInGridMask(grid));
            var resultPath = new List<ScoreContribution>[GridSize, GridSize];
            for (var r = 0; r < GridSize; r++)
            for (var c = 0; c < GridSize; c++)
            {
                resultPath[r, c] =
                [
                    new ScoreContribution(
                        $"Path L={metrics.VisitPathLength} dead-ends={metrics.InternalDeadEnds} edges={metrics.InGridEdgeCount}",
                        -1, r, c, true, pathScore / CellCount,
                        1, [], 1, [], pathScore / CellCount),
                ];
            }

            return resultPath;
        }

        var conn = new int[CellCount];
        for (var cell = 0; cell < CellCount; cell++)
            conn[cell] = grid[cell / GridSize, cell % GridSize].Connections.CountConnections();

        var s = new double[_maskCount];
        for (var mi = 0; mi < _maskCount; mi++)
        {
            for (var cell = 0; cell < CellCount; cell++)
                s[mi] += _tileMult[cell][mi][conn[cell]];
        }

        var result = new List<ScoreContribution>[GridSize, GridSize];
        for (var cell = 0; cell < CellCount; cell++)
        {
            var r = cell / GridSize;
            var c = cell % GridSize;
            var rows = new List<ScoreContribution>();
            double baseValue = 0, baseWeight = 0;

            foreach (var (dr, dc) in NeighborOffsets)
            {
                var nr = r + dr;
                var nc = c + dc;
                if (nr < 0 || nr >= GridSize || nc < 0 || nc >= GridSize)
                    continue;

                var u = nr * GridSize + nc;
                var piece = grid[nr, nc].Piece;
                foreach (var mod in piece.Modifiers)
                {
                    if (mod.IsGlobal || mod.Weight == 0)
                        continue;

                    var mi = _maskIndex[mod.Tags];
                    var chartM = _chartMult[u][mi][conn[u]];
                    var tileM = _tileMult[cell][mi][conn[cell]];
                    var value = mod.Weight * chartM * tileM;
                    if (mod.Name == "Default")
                    {
                        baseValue += value;
                        baseWeight += mod.Weight;
                        continue;
                    }

                    rows.Add(new ScoreContribution(
                        mod.Name, piece.Id, nr, nc, false, mod.Weight,
                        chartM, MatchedBorders(u, mod.Tags, conn[u], chartSide: true),
                        tileM, MatchedBorders(cell, mod.Tags, conn[cell], chartSide: false),
                        value));
                }
            }

            var selfPiece = grid[r, c].Piece;
            foreach (var mod in selfPiece.Modifiers)
            {
                if (!mod.IsGlobal || mod.Weight == 0)
                    continue;

                var mi = _maskIndex[mod.Tags];
                var chartM = _chartMult[cell][mi][conn[cell]];
                rows.Add(new ScoreContribution(
                    mod.Name, selfPiece.Id, r, c, true, mod.Weight,
                    chartM, MatchedBorders(cell, mod.Tags, conn[cell], chartSide: true),
                    s[mi], [], mod.Weight * chartM * s[mi]));
            }

            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (baseWeight > 0)
            {
                rows.Add(new ScoreContribution(
                    "Base adjacency", -1, -1, -1, false, baseWeight,
                    1, [], baseValue / baseWeight, [], baseValue));
            }

            result[r, c] = rows;
        }

        return result;
    }

    private List<AppliedBorder> MatchedBorders(int cell, ModifierTag tags, int connections, bool chartSide)
    {
        var list = new List<AppliedBorder>();
        foreach (var b in _bordersByCell[cell])
        {
            if (b.AffectsPlacedChart != chartSide)
                continue;
            if (!ModifierTagParser.Matches(b.Tags, tags))
                continue;

            var m = b.PerConnection ? Math.Max(0, 1 + (b.Multiplier - 1) * connections) : b.Multiplier;
            list.Add(new AppliedBorder(b.Name, m));
        }

        return list;
    }
}

public record AppliedBorder(string Name, double Multiplier);

public record ScoreContribution(
    string ModName,
    int SourcePieceId,
    int SourceRow,
    int SourceCol,
    bool IsGlobal,
    double Weight,
    double ChartMultiplier,
    IReadOnlyList<AppliedBorder> ChartBorders,
    double TileFactor,
    IReadOnlyList<AppliedBorder> TileBorders,
    double Value);
