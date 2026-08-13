using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DeepwaterEngagementSuite.VoyagePlannerData;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using Newtonsoft.Json;
using Direction = DeepwaterEngagementSuite.VoyagePlannerData.Direction;

namespace DeepwaterEngagementSuite;

public partial class DeepwaterEngagementSuite
{
    private string _lastVoyageDumpPath;
    private string _lastVoyageDumpError;

    private string VoyageDumpDirectory => Path.Combine(ConfigDirectory, "voyage-dumps");

    
    private VoyageStateDump CaptureVoyageState(VoyageWindow tree, string note = null)
    {
        var dump = new VoyageStateDump
        {
            CapturedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Note = note,
            ProfileName = Settings.VoyageSettings.ProfileSelector?.Value,
            Solver = new VoyageStateDump.SolverSettingsDump
            {
                UseFastSolver = true,
                TimeLimitSeconds = Settings.VoyageSettings.SolverTimeLimitSeconds.Value,
                TopN = 10,
            },
            StrategyOptions = VoyageStateDump.StrategyOptionsDump.From(Settings.VoyageSettings.Strategies.ToOptions()),
        };

        foreach (var name in ReadBorderModRawNames(tree))
            dump.RawBorderMods.Add(name);
        foreach (var text in ReadBorderModUiTexts(tree))
            dump.RawBorderModsUi.Add(text);
        dump.BorderModsSource = BorderModsFromDataUsable(dump.RawBorderMods)
            ? "Data.BorderMods"
            : dump.RawBorderModsUi.Any(t => !string.IsNullOrWhiteSpace(t))
                ? "UI 3->10->i->1->0.Text"
                : "none";

        CaptureTiles(tree, dump);
        CaptureCharts(tree, dump);

        var pieces = dump.ToMapPieces();
        var tileBorders = dump.ToTileBorders();
        CaptureDerived(dump, pieces, tileBorders);

        try
        {
            var placement = VoyagePlacementRules.Apply(pieces, tileBorders, dump.ToStrategyOptions());
            dump.Placement = BuildPlacementDump(placement, pieces);
            if (dump.Placement != null)
                dump.Placement.DroppedLockCount = _voyageSolve?.DroppedLockCount ?? 0;
                dump.Placement.DroppedLocks = _voyageSolve?.DroppedLocks?.ToList() ?? [];
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Voyage dump: placement rules threw: {ex}");
        }

        CaptureSolution(dump);
        return dump;
    }

    private void CaptureTiles(VoyageWindow tree, VoyageStateDump dump)
    {
        var modsPerTileIndex = GetTileMods(tree);
        var tiles = tree?.Tiles;

        for (var index = 0; index < 9; index++)
        {
            var tileDump = new VoyageStateDump.TileDump
            {
                Index = index,
                Row = index / 3,
                Col = index % 3,
            };

            foreach (var borderMod in modsPerTileIndex.GetValueOrDefault(index) ?? [])
            {
                var setting = FindBorderSetting(borderMod.Id, borderMod.DisplayText);
                tileDump.Borders.Add(new VoyageStateDump.BorderDump
                {
                    RawName = borderMod.Id,
                    HasSettingsEntry = setting != null,
                    Tags = setting?.Tags.Value,
                    Multiplier = setting?.ValueMultiplier.Value ?? 1,
                    PerConnection = setting?.PerConnection.Value ?? false,
                    AffectsPlacedChart = setting?.AffectsPlacedChart.Value ?? false,
                });
            }

            if (tiles != null && index < tiles.Count)
            {
                var placed = tiles[index]?.ItemContainer?.Entity;
                if (placed != null && placed.TryGetComponent(out DeepwaterChart chart))
                {
                    var basePath = (Direction)chart.Room.Path;
                    tileDump.PlacedChart = new VoyageStateDump.PlacedChartDump
                    {
                        RoomName = chart.Room.Name ?? "",
                        RoomPath = chart.Room.Path,
                        Rotation = chart.Rotation,
                        EffectiveConnections = basePath.RotateCcw(chart.Rotation).ToString(),
                        Mods = CaptureMods(placed.GetComponent<Mods>()?.ImplicitMods),
                    };
                }
            }

            dump.Tiles.Add(tileDump);
        }
    }

    private void CaptureCharts(VoyageWindow tree, VoyageStateDump dump)
    {
        var all = tree?.AvailableCharts ?? [];
        var included = GetAvailableCharts();
        var pieceIdByAddress = new Dictionary<long, int>();
        for (var i = 0; i < included.Count; i++)
        {
            var address = included[i]?.Address ?? 0;
            if (address != 0)
                pieceIdByAddress[address] = i;
        }

        for (var inventoryIndex = 0; inventoryIndex < all.Count; inventoryIndex++)
        {
            var item = all[inventoryIndex];
            var chartDump = new VoyageStateDump.ChartDump
            {
                InventoryIndex = inventoryIndex,
                PieceId = item != null && pieceIdByAddress.TryGetValue(item.Address, out var id) ? id : -1,
            };

            if (item?.Item != null && item.Item.TryGetComponent(out DeepwaterChart chart))
            {
                var connections = (Direction)chart.Room.Path;
                chartDump.RoomName = chart.Room.Name ?? "";
                chartDump.RoomPath = chart.Room.Path;
                chartDump.BaseConnections = connections.ToString();
                chartDump.PieceType = VoyageStateDump.ClassifyPieceType(connections).ToString();
                chartDump.Mods = CaptureMods(item.Item.GetComponent<Mods>()?.ImplicitMods);
                
                
                chartDump.IncludedInSolve = chartDump.PieceId >= 0;
            }
            else
            {
                chartDump.RoomName = "";
                chartDump.IncludedInSolve = false;
            }

            dump.Charts.Add(chartDump);
        }
    }

    private List<VoyageStateDump.ModDump> CaptureMods(IList<ItemMod> mods)
    {
        var result = new List<VoyageStateDump.ModDump>();
        if (mods == null)
            return result;

        foreach (var mod in mods)
        {
            if (mod == null)
                continue;

            var setting = Settings.VoyageSettings.ChartModifiers.Content
                .FirstOrDefault(cm => cm.Id.Value.Equals(mod.RawName, StringComparison.OrdinalIgnoreCase));

            var values = new List<int>();
            try
            {
                if (mod.Values != null)
                    values.AddRange(mod.Values);
            }
            catch (Exception)
            {
                
            }

            result.Add(new VoyageStateDump.ModDump
            {
                RawName = mod.RawName,
                HasSettingsEntry = setting != null,
                Weight = setting?.Weight.Value ?? 0,
                IsGlobal = setting?.IsGlobal.Value ?? false,
                Tags = setting?.Tags.Value,
                Value1 = values.Count > 0 ? values[0] : 0,
                Values = values,
            });
        }

        return result;
    }

    private static void CaptureDerived(
        VoyageStateDump dump,
        List<MapPiece> pieces,
        IReadOnlyList<BorderEffect>[,] tileBorders)
    {
        foreach (var (row, col) in ChartPredicates.EnumerateCells())
        {
            var label = $"({row},{col})";
            switch (ChartPredicates.OrbPriority(ChartPredicates.BordersAt(tileBorders, row, col)))
            {
                case 3: dump.Derived.DivineCenters.Add(label); break;
                case 2: dump.Derived.AnnulCenters.Add(label); break;
                case 1: dump.Derived.AncientCenters.Add(label); break;
            }
        }

        dump.Derived.StarfishChartCount = pieces.Count(ChartPredicates.IsStarfishChart);
        dump.Derived.AdjacentRareT2ChartCount = pieces.Count(ChartPredicates.IsAdjacentRareSaveChart);
        dump.Derived.RareVoyageChartCount = pieces.Count(ChartPredicates.IsRareVoyageChart);
        dump.Derived.StrongboxChartCount = pieces.Count(ChartPredicates.IsStrongboxCountChart);
        dump.Derived.PelagicChartCount = pieces.Count(ChartPredicates.IsPelagic);
        dump.Derived.BoardHasStrongTreasureAnchors = ChartPredicates.BoardHasStrongTreasureAnchors(tileBorders);
        dump.Derived.BoardHasStrongInfiniteLanterns = ChartPredicates.BoardHasStrongInfiniteLanterns(tileBorders);
    }

    private static VoyageStateDump.PlacementDump BuildPlacementDump(
        VoyagePlacementRules.Result placement,
        List<MapPiece> allPieces)
    {
        if (placement == null)
            return null;

        var byId = allPieces
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var result = new VoyageStateDump.PlacementDump
        {
            NoConsumeActive = placement.NoConsumeActive,
            ActiveStrategies = placement.ActiveStrategies?.ToList() ?? new List<string>(),
            RemainingPieceIds = placement.Pieces.Select(p => p.Id).ToList(),
        };

        foreach (var lockedPlacement in placement.Locks)
        {
            var lockDump = new VoyageStateDump.LockDump
            {
                Row = lockedPlacement.Row,
                Col = lockedPlacement.Col,
                PieceId = lockedPlacement.PieceId,
                Rotation = lockedPlacement.Rotation,
                Strategy = lockedPlacement.Strategy,
                Priority = lockedPlacement.Priority,
            };

            if (byId.TryGetValue(lockedPlacement.PieceId, out var piece))
            {
                lockDump.PieceRoomName = piece.Name;
                lockDump.PieceMods = piece.Modifiers
                    .Where(m => m.Name != "Default")
                    .Select(m => m.Name)
                    .ToList();
            }

            result.Locks.Add(lockDump);
        }

        foreach (var bit in FormatSavedChartBits(placement))
        {
            var split = bit.IndexOf(' ');
            if (split <= 0 || !int.TryParse(bit[..split], out var count))
                continue;
            result.SavedCounts[bit[(split + 1)..]] = count;
        }

        return result;
    }

    private void CaptureSolution(VoyageStateDump dump)
    {
        var result = _result;
        if (result == null || result.Solutions.Count == 0)
            return;

        var index = Math.Clamp(_selectedSolutionIndex, 0, result.Solutions.Count - 1);
        var solution = result.Solutions[index];
        var solutionDump = new VoyageStateDump.SolutionDump
        {
            SolutionCount = result.Solutions.Count,
            TotalScore = solution.TotalScore,
            IsValid = solution.IsValid,
            NodesExplored = result.NodesExplored,
            NodesPruned = result.NodesPruned,
        };

        for (var i = 0; i < 9; i++)
        {
            var placement = solution.Grid[i / 3, i % 3];
            solutionDump.Grid.Add(placement?.Piece == null
                ? "-"
                : $"{placement.Piece.Id}:{placement.Rotation}:{placement.Connections}");
        }

        dump.Solution = solutionDump;
    }

    
    private string DumpVoyageStateToFile(VoyageWindow tree, string note = null)
    {
        _lastVoyageDumpError = null;
        try
        {
            if (tree is not { IsValid: true, IsVisible: true })
            {
                _lastVoyageDumpError = "Voyage window is not open.";
                return null;
            }

            var dump = CaptureVoyageState(tree, note);
            Directory.CreateDirectory(VoyageDumpDirectory);
            var fileName = $"voyage-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var path = Path.Combine(VoyageDumpDirectory, fileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(dump, Formatting.Indented));

            _lastVoyageDumpPath = path;
            DebugWindow.LogMsg($"Voyage state dumped to {path}", 10);
            foreach (var line in dump.Describe().Split('\n'))
                DebugWindow.LogMsg(line.TrimEnd('\r'), 10);
            return path;
        }
        catch (Exception ex)
        {
            _lastVoyageDumpError = ex.Message;
            DebugWindow.LogError($"Voyage dump failed: {ex}");
            return null;
        }
    }
}
