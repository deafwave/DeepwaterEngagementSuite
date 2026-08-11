using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepwaterEngagementSuite.VoyagePlannerData;


public sealed class VoyageStateDump
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string CapturedAtUtc { get; set; }
    public string Note { get; set; }
    public string ProfileName { get; set; }

    
    public List<string> RawBorderMods { get; set; } = [];

    
    public List<string> RawBorderModsUi { get; set; } = [];

    
    public string BorderModsSource { get; set; }

    
    public List<TileDump> Tiles { get; set; } = [];

    
    public List<ChartDump> Charts { get; set; } = [];

    public StrategyOptionsDump StrategyOptions { get; set; } = new();
    public SolverSettingsDump Solver { get; set; } = new();

    
    public DerivedDump Derived { get; set; } = new();

    
    public PlacementDump Placement { get; set; }

    
    public SolutionDump Solution { get; set; }

    public sealed class TileDump
    {
        public int Index { get; set; }
        public int Row { get; set; }
        public int Col { get; set; }
        public List<BorderDump> Borders { get; set; } = [];

        
        public PlacedChartDump PlacedChart { get; set; }
    }

    public sealed class BorderDump
    {
        public string RawName { get; set; }
        public bool HasSettingsEntry { get; set; }
        public string Tags { get; set; }
        public double Multiplier { get; set; }
        public bool PerConnection { get; set; }
        public bool AffectsPlacedChart { get; set; }

        public BorderEffect ToBorderEffect() => new(
            RawName,
            ModifierTagParser.Parse(Tags, ModifierTag.All),
            Multiplier,
            PerConnection,
            AffectsPlacedChart);
    }

    public sealed class ChartDump
    {
        
        public int InventoryIndex { get; set; }

        
        public int PieceId { get; set; } = -1;

        public bool IncludedInSolve { get; set; }
        public string RoomName { get; set; }

        
        public int RoomPath { get; set; }

        public string BaseConnections { get; set; }
        public string PieceType { get; set; }
        public List<ModDump> Mods { get; set; } = [];

        public MapPiece ToMapPiece(int? overrideId = null)
        {
            var connections = (Direction)RoomPath;
            var mods = new List<Modifier> { new("Default", 1) };
            mods.AddRange(Mods.Select(m => m.ToModifier()));
            return new MapPiece(overrideId ?? PieceId, ClassifyPieceType(connections), connections, mods, RoomName ?? "");
        }
    }

    public sealed class PlacedChartDump
    {
        public string RoomName { get; set; }
        public int RoomPath { get; set; }
        public int Rotation { get; set; }
        public string EffectiveConnections { get; set; }
        public List<ModDump> Mods { get; set; } = [];
    }

    public sealed class ModDump
    {
        public string RawName { get; set; }

        
        public bool HasSettingsEntry { get; set; }

        public double Weight { get; set; }
        public bool IsGlobal { get; set; }
        public string Tags { get; set; }
        public int Value1 { get; set; }
        public List<int> Values { get; set; } = [];

        public Modifier ToModifier() => new(
            RawName,
            Weight,
            IsGlobal,
            ModifierTagParser.Parse(Tags, ModifierTag.None),
            Value1);
    }

    public sealed class StrategyOptionsDump
    {
        public bool RareMonstersDrop { get; set; } = true;
        public bool NoConsumeAnchorfield { get; set; } = true;
        public bool CenterSpecialty { get; set; } = true;
        public bool TreasureAnchors { get; set; } = true;
        public bool InfiniteLanterns { get; set; }
        public bool ShortestPath { get; set; }
        public int SaveKishara { get; set; } = ChartIds.MaxSavedKishara;
        public int SaveNoEquipment { get; set; } = ChartIds.MaxSavedNoEquipment;
        public int SaveFractured { get; set; } = ChartIds.MaxSavedFractured;
        public int SaveGoldenLanterns { get; set; } = ChartIds.MaxSavedGoldenLanterns;
        public int SavePantheon { get; set; } = ChartIds.MaxSavedPantheon;
        public int SaveSoulEater { get; set; } = ChartIds.MaxSavedSoulEater;
        public int SaveRareFracture { get; set; } = ChartIds.MaxSavedRareFracture;
        public int SaveRarePossessed { get; set; } = ChartIds.MaxSavedRarePossessed;
        public int SaveStarfish { get; set; } = ChartIds.MaxSavedStarfish;
        public int SaveUniqueAmulet2 { get; set; } = ChartIds.MaxSavedUniqueAmulet2;
        public int SaveUniqueAmulet1 { get; set; } = ChartIds.MaxSavedUniqueAmulet1;

        public static StrategyOptionsDump From(VoyageStrategyOptions o)
        {
            o ??= VoyageStrategyOptions.AllEnabled;
            return new StrategyOptionsDump
            {
                RareMonstersDrop = o.RareMonstersDrop,
                NoConsumeAnchorfield = o.NoConsumeAnchorfield,
                CenterSpecialty = o.CenterSpecialty,
                TreasureAnchors = o.TreasureAnchors,
                InfiniteLanterns = o.InfiniteLanterns,
                ShortestPath = o.ShortestPath,
                SaveKishara = o.SaveKishara,
                SaveNoEquipment = o.SaveNoEquipment,
                SaveFractured = o.SaveFractured,
                SaveGoldenLanterns = o.SaveGoldenLanterns,
                SavePantheon = o.SavePantheon,
                SaveSoulEater = o.SaveSoulEater,
                SaveRareFracture = o.SaveRareFracture,
                SaveRarePossessed = o.SaveRarePossessed,
                SaveStarfish = o.SaveStarfish,
                SaveUniqueAmulet2 = o.SaveUniqueAmulet2,
                SaveUniqueAmulet1 = o.SaveUniqueAmulet1,
            };
        }

        public VoyageStrategyOptions ToOptions() => new(
            RareMonstersDrop,
            NoConsumeAnchorfield,
            CenterSpecialty,
            TreasureAnchors,
            InfiniteLanterns,
            ShortestPath,
            SaveKishara,
            SaveNoEquipment,
            SaveFractured,
            SaveGoldenLanterns,
            SavePantheon,
            SaveSoulEater,
            SaveRareFracture,
            SaveRarePossessed,
            SaveStarfish,
            SaveUniqueAmulet2,
            SaveUniqueAmulet1);
    }

    public sealed class SolverSettingsDump
    {
        public bool UseFastSolver { get; set; } = true;
        public double TimeLimitSeconds { get; set; } = 5;
        public int TopN { get; set; } = 10;
    }

    public sealed class DerivedDump
    {
        public List<string> DivineCenters { get; set; } = [];
        public List<string> AnnulCenters { get; set; } = [];
        public List<string> AncientCenters { get; set; } = [];
        public int StarfishChartCount { get; set; }
        public int AdjacentRareT2ChartCount { get; set; }
        public int RareVoyageChartCount { get; set; }
        public int StrongboxChartCount { get; set; }
        public int PelagicChartCount { get; set; }
        public bool BoardHasStrongTreasureAnchors { get; set; }
        public bool BoardHasStrongInfiniteLanterns { get; set; }
    }

    public sealed class PlacementDump
    {
        public List<LockDump> Locks { get; set; } = [];
        public List<int> RemainingPieceIds { get; set; } = [];
        public List<string> ActiveStrategies { get; set; } = [];
        public Dictionary<string, int> SavedCounts { get; set; } = new();
        public bool NoConsumeActive { get; set; }

        
        public int DroppedLockCount { get; set; }

        
        public List<string> DroppedLocks { get; set; } = [];
    }

    public sealed class LockDump
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int PieceId { get; set; }
        public int? Rotation { get; set; }
        public string Strategy { get; set; }
        public int Priority { get; set; }
        public string PieceRoomName { get; set; }
        public List<string> PieceMods { get; set; } = [];
    }

    public sealed class SolutionDump
    {
        public int SolutionCount { get; set; }
        public double TotalScore { get; set; }
        public bool IsValid { get; set; }
        public long NodesExplored { get; set; }
        public long NodesPruned { get; set; }

        
        public List<string> Grid { get; set; } = [];
    }

    
    public List<MapPiece> ToMapPieces() =>
        Charts.Where(c => c.IncludedInSolve)
            .OrderBy(c => c.PieceId)
            .Select(c => c.ToMapPiece())
            .ToList();

    public IReadOnlyList<BorderEffect>[,] ToTileBorders()
    {
        var borders = new IReadOnlyList<BorderEffect>[3, 3];
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
            borders[r, c] = [];

        foreach (var tile in Tiles)
        {
            if (tile.Row is < 0 or > 2 || tile.Col is < 0 or > 2)
                continue;
            borders[tile.Row, tile.Col] = tile.Borders.Select(b => b.ToBorderEffect()).ToList();
        }

        return borders;
    }

    public VoyageStrategyOptions ToStrategyOptions() =>
        (StrategyOptions ?? new StrategyOptionsDump()).ToOptions();

    public VoyagePlannerSettings ToPlannerSettings() =>
        new(Solver?.TopN ?? 10, true, Solver?.TimeLimitSeconds ?? 5.0);

    public static PieceType ClassifyPieceType(Direction connections) =>
        connections.CountConnections() switch
        {
            4 => PieceType.Cross,
            3 => PieceType.Tee,
            1 => PieceType.Single,
            2 => connections.HasFlag(Direction.Left) == connections.HasFlag(Direction.Right)
                ? PieceType.Straight
                : PieceType.Corner,
            _ => PieceType.Single,
        };

    
    public string Describe()
    {
        var lines = new List<string>
        {
            $"Voyage dump v{Version} captured {CapturedAtUtc}" + (string.IsNullOrEmpty(Note) ? "" : $" — {Note}"),
            $"Border source: {BorderModsSource ?? "?"}  " +
            $"Data slots: {RawBorderMods.Count(s => !string.IsNullOrWhiteSpace(s))}/12  " +
            $"UI texts: {RawBorderModsUi.Count(s => !string.IsNullOrWhiteSpace(s))}/12",
            $"Charts: {Charts.Count(c => c.IncludedInSolve)} in solve / {Charts.Count} total",
            $"Divine: [{string.Join(" ", Derived.DivineCenters)}]  " +
            $"Annul: [{string.Join(" ", Derived.AnnulCenters)}]  " +
            $"Ancient: [{string.Join(" ", Derived.AncientCenters)}]",
            $"Starfish charts: {Derived.StarfishChartCount}, adjRareT2: {Derived.AdjacentRareT2ChartCount}, " +
            $"voyageRare: {Derived.RareVoyageChartCount}, boxes: {Derived.StrongboxChartCount}",
        };

        if (Placement != null)
        {
            lines.Add($"Locks ({Placement.Locks.Count}): " + (Placement.Locks.Count == 0
                ? "none"
                : string.Join(", ", Placement.Locks.Select(l => $"#{l.PieceId}@({l.Row},{l.Col})"))));
            lines.Add("Active strategies: " + (Placement.ActiveStrategies.Count == 0
                ? "none"
                : string.Join(", ", Placement.ActiveStrategies)));
        }

        lines.Add(Solution == null
            ? "Solution: none"
            : $"Solution: {Solution.SolutionCount} found, best score {Solution.TotalScore:F2}, valid={Solution.IsValid}");

        return string.Join(Environment.NewLine, lines);
    }
}
