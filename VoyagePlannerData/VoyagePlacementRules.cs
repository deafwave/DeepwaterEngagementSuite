using System;
using System.Collections.Generic;
using DeepwaterEngagementSuite.VoyagePlannerData.Strategies;

namespace DeepwaterEngagementSuite.VoyagePlannerData;

public static class VoyagePlacementRules
{

    public const string NotConsume1 = ChartIds.NotConsume1;
    public const string NotConsume2 = ChartIds.NotConsume2;
    public const string RareDivine = ChartIds.RareDivine;
    public const string RareAnnul = ChartIds.RareAnnul;
    public const string RareAncient = ChartIds.RareAncient;
    public const string TreasureAnchors1 = ChartIds.TreasureAnchors1;
    public const string TreasureAnchors2 = ChartIds.TreasureAnchors2;
    public const string InfiniteLanterns = ChartIds.InfiniteLanterns;

    public const string VoyageIncreasedRareMonsters = ChartIds.VoyageIncreasedRareMonsters;
    public const string VoyageNoEquipmentDrops = ChartIds.VoyageNoEquipmentDrops;
    public const string VoyageSoulEater = ChartIds.VoyageSoulEater;
    public const string VoyageRareFracture = ChartIds.VoyageRareFracture;
    public const string VoyageMonstersPossessed = ChartIds.VoyageMonstersPossessed;
    public const string AdjacentFracturedPrefix = ChartIds.AdjacentFracturedPrefix;
    public const string AdjacentGoldenLanternsPrefix = ChartIds.AdjacentGoldenLanternsPrefix;
    public const string AdjacentPantheonPrefix = ChartIds.AdjacentPantheonPrefix;
    public const string AdjacentStrongboxesPrefix = ChartIds.AdjacentStrongboxesPrefix;
    public const string AdjacentStarfishPrefix = ChartIds.AdjacentStarfishPrefix;
    public const string AdjacentIncreasedRarePrefix = ChartIds.AdjacentIncreasedRarePrefix;
    public const string AdjacentDivinerBoxPrefix = ChartIds.AdjacentDivinerBoxPrefix;
    public const string AdjacentArcanistBoxPrefix = ChartIds.AdjacentArcanistBoxPrefix;
    public const string AdjacentOperativeBoxPrefix = ChartIds.AdjacentOperativeBoxPrefix;
    public const string AdjacentLostMessagePrefix = ChartIds.AdjacentLostMessagePrefix;
    public const string AdjacentUniqueAmuletPrefix = ChartIds.AdjacentUniqueAmuletPrefix;
    public const string AdjacentUniqueBeltPrefix = ChartIds.AdjacentUniqueBeltPrefix;
    public const string AdjacentUniqueRingPrefix = ChartIds.AdjacentUniqueRingPrefix;

    public const int CenterRow = ChartIds.CenterRow;
    public const int CenterCol = ChartIds.CenterCol;

    public const int MaxSavedBoxes = ChartIds.MaxSavedBoxes;
    public const int MaxSavedRareMonsterSupport = ChartIds.MaxSavedRareMonsterSupport;
    public const int MaxSavedStarfish = ChartIds.MaxSavedStarfish;
    public const int MaxSavedRareVoyage = ChartIds.MaxSavedRareVoyage;
    public const int MaxSavedPelagic = ChartIds.MaxSavedPelagic;
    public const int MaxSaveCap = ChartIds.MaxSaveCap;
    public const int MaxSavedDefault = ChartIds.MaxSavedDefault;
    public const int MaxSavedKishara = ChartIds.MaxSavedKishara;
    public const int MaxPlacedKisharaWhenNotSaving = ChartIds.MaxPlacedKisharaWhenNotSaving;
    public const int MaxSavedNoEquipment = ChartIds.MaxSavedNoEquipment;
    public const int MaxSavedFractured = ChartIds.MaxSavedFractured;
    public const int MaxSavedGoldenLanterns = ChartIds.MaxSavedGoldenLanterns;
    public const int MaxSavedPantheon = ChartIds.MaxSavedPantheon;
    public const int MaxSavedSoulEater = ChartIds.MaxSavedSoulEater;
    public const int MaxSavedRareFracture = ChartIds.MaxSavedRareFracture;
    public const int MaxSavedRarePossessed = ChartIds.MaxSavedRarePossessed;
    public const int MaxSavedUniqueAmulet2 = ChartIds.MaxSavedUniqueAmulet2;
    public const int MaxSavedUniqueAmulet1 = ChartIds.MaxSavedUniqueAmulet1;

    public const string PelagicRoomName = ChartIds.PelagicRoomName;
    public const string ClamRoomName = ChartIds.ClamRoomName;
    public const string AnchorfieldRoomName = ChartIds.AnchorfieldRoomName;
    public const string KisharaRoomName = ChartIds.KisharaRoomName;

    public static readonly (int Row, int Col)[] SacrificeCorners = ChartIds.SacrificeCorners;

    public sealed record Result(
        List<MapPiece> Pieces,
        List<LockedPlacement> Locks,
        int SavedPelagicCount,
        int SavedFarmCount,
        int SavedStrongboxCount,
        int SavedStarfishCount,
        int SavedRareVoyageCount,
        int SavedAdjacentRareCount,
        int SavedOperativeBoxCount,
        int SavedLostMessageCount,
        int SavedKisharaCount,
        int SavedNoEquipmentCount,
        int SavedFracturedCount,
        int SavedGoldenLanternsCount,
        int SavedPantheonCount,
        int SavedSoulEaterCount,
        int SavedRareFractureCount,
        int SavedRarePossessedCount,
        int SavedUniqueBeltCount,
        int SavedUniqueRingCount,
        int SavedUniqueAmulet2Count,
        int SavedUniqueAmulet1Count,
        int SavedShortestPathPremiumCount = 0,
        bool NoConsumeActive = false,
        IReadOnlyList<string> ActiveStrategies = null);

    public static bool IsSacrificeCorner(int row, int col) =>
        ChartPredicates.IsSacrificeCorner(row, col);

    public static Result Apply(
        IReadOnlyList<MapPiece> pieces,
        IReadOnlyList<BorderEffect>[,] tileBorders,
        VoyageStrategyOptions options = null)
    {
        var ctx = PlacementPipeline.Run(pieces, tileBorders, options);
        return ToResult(ctx);
    }

    private static Result ToResult(PlacementContext ctx) =>
        new(
            ctx.Working,
            ctx.Locks,
            ctx.GetSaved(SaveCountKeys.Pelagic),
            ctx.GetSaved(SaveCountKeys.Farm),
            ctx.GetSaved(SaveCountKeys.Strongbox),
            ctx.GetSaved(SaveCountKeys.Starfish),
            ctx.GetSaved(SaveCountKeys.RareVoyage),
            ctx.GetSaved(SaveCountKeys.AdjacentRare),
            ctx.GetSaved(SaveCountKeys.OperativeBox),
            ctx.GetSaved(SaveCountKeys.LostMessage),
            ctx.GetSaved(SaveCountKeys.Kishara),
            ctx.GetSaved(SaveCountKeys.NoEquipment),
            ctx.GetSaved(SaveCountKeys.Fractured),
            ctx.GetSaved(SaveCountKeys.GoldenLanterns),
            ctx.GetSaved(SaveCountKeys.Pantheon),
            ctx.GetSaved(SaveCountKeys.SoulEater),
            ctx.GetSaved(SaveCountKeys.RareFracture),
            ctx.GetSaved(SaveCountKeys.RarePossessed),
            ctx.GetSaved(SaveCountKeys.UniqueBelt),
            ctx.GetSaved(SaveCountKeys.UniqueRing),
            ctx.GetSaved(SaveCountKeys.UniqueAmulet2),
            ctx.GetSaved(SaveCountKeys.UniqueAmulet1),
            ctx.GetSaved(SaveCountKeys.ShortestPathPremium),
            NoConsumeActive: ctx.NoConsumeActive,
            ActiveStrategies: ctx.ActiveStrategies);

    public static bool IsClamChart(MapPiece piece) => ChartPredicates.IsClamChart(piece);
    public static bool IsAnchorfieldChart(MapPiece piece) => ChartPredicates.IsAnchorfieldChart(piece);
    public static bool IsFarmChart(MapPiece piece) => ChartPredicates.IsFarmChart(piece);
    public static bool IsPelagic(MapPiece piece) => ChartPredicates.IsPelagic(piece);
    public static bool IsKishara(MapPiece piece) => ChartPredicates.IsKishara(piece);
    public static bool IsNoEquipmentChart(MapPiece piece) => ChartPredicates.IsNoEquipmentChart(piece);
    public static bool IsSoulEaterChart(MapPiece piece) => ChartPredicates.IsSoulEaterChart(piece);
    public static bool IsRareFractureChart(MapPiece piece) => ChartPredicates.IsRareFractureChart(piece);
    public static bool IsRarePossessedChart(MapPiece piece) => ChartPredicates.IsRarePossessedChart(piece);
    public static bool IsFracturedChart(MapPiece piece) => ChartPredicates.IsFracturedChart(piece);
    public static bool IsGoldenLanternsChart(MapPiece piece) => ChartPredicates.IsGoldenLanternsChart(piece);
    public static bool IsPantheonChart(MapPiece piece) => ChartPredicates.IsPantheonChart(piece);
    public static bool IsAdjacentStrongboxesChart(MapPiece piece) => ChartPredicates.IsAdjacentStrongboxesChart(piece);
    public static bool IsPremiumBoxChart(MapPiece piece) => ChartPredicates.IsPremiumBoxChart(piece);
    public static bool IsStrongboxCountChart(MapPiece piece) => ChartPredicates.IsStrongboxCountChart(piece);
    public static bool IsOperativeBoxChart(MapPiece piece) => ChartPredicates.IsOperativeBoxChart(piece);
    public static bool IsStarfishChart(MapPiece piece) => ChartPredicates.IsStarfishChart(piece);
    public static bool IsAdjacentRareChart(MapPiece piece) => ChartPredicates.IsAdjacentRareChart(piece);
    public static bool IsAdjacentRareSaveChart(MapPiece piece) => ChartPredicates.IsAdjacentRareSaveChart(piece);
    public static bool IsRareVoyageChart(MapPiece piece) => ChartPredicates.IsRareVoyageChart(piece);
    public static bool IsLostMessageChart(MapPiece piece) => ChartPredicates.IsLostMessageChart(piece);
    public static bool IsUniqueAmuletChart(MapPiece piece) => ChartPredicates.IsUniqueAmuletChart(piece);
    public static bool IsUniqueAmulet1Chart(MapPiece piece) => ChartPredicates.IsUniqueAmulet1Chart(piece);
    public static bool IsUniqueAmulet2Chart(MapPiece piece) => ChartPredicates.IsUniqueAmulet2Chart(piece);
    public static bool IsUniqueBeltChart(MapPiece piece) => ChartPredicates.IsUniqueBeltChart(piece);
    public static bool IsUniqueRingChart(MapPiece piece) => ChartPredicates.IsUniqueRingChart(piece);
    public static bool IsCenterOnlyUniqueChart(MapPiece piece) => ChartPredicates.IsCenterOnlyUniqueChart(piece);
    public static bool IsOrbRareGlobalChart(MapPiece piece) => ChartPredicates.IsOrbRareGlobalChart(piece);
    public static bool IsOrbRareComboChart(MapPiece piece) => ChartPredicates.IsOrbRareComboChart(piece);

    public static bool IsSpecialtyComboModifier(string rawName) =>
        ChartPredicates.IsSpecialtyComboModifier(rawName);

    public static bool IsIncreasedRareStrategyModifier(string rawName) =>
        ChartPredicates.IsIncreasedRareStrategyModifier(rawName);

    public static bool HasStrategyOrb(IEnumerable<string> borderNames) =>
        ChartPredicates.HasStrategyOrb(borderNames);

    public static HashSet<int> SelectInventorySpecialtyIndices(
        IReadOnlyList<string> roomNames,
        IReadOnlyList<IReadOnlyList<(string RawName, int Value1)>> modsPerChart) =>
        ChartPredicates.SelectInventorySpecialtyIndices(roomNames, modsPerChart);

    public static bool TrySpecialtyRoomLabel(string roomName, out string label) =>
        ChartPredicates.TrySpecialtyRoomLabel(roomName, out label);

    public static bool IsStrategyBorder(string rawName) =>
        ChartPredicates.IsStrategyBorder(rawName);

    public static bool IsTreasureAnchorsBorder(string rawName) =>
        ChartPredicates.IsTreasureAnchorsBorder(rawName);

    public static bool IsInfiniteLanternsBorder(string rawName) =>
        ChartPredicates.IsInfiniteLanternsBorder(rawName);

    public static bool IsStrongNoConsume(IReadOnlyList<BorderEffect> borders) =>
        ChartPredicates.IsStrongNoConsume(borders);

    public static bool IsStrongTreasureAnchorsCounts(int t1, int t2) =>
        ChartPredicates.IsStrongTreasureAnchorsCounts(t1, t2);

    public static bool IsStrongTreasureAnchors(IEnumerable<string> borderNames) =>
        ChartPredicates.IsStrongTreasureAnchors(borderNames);

    public static bool IsStrongTreasureAnchors(IReadOnlyList<BorderEffect> borders) =>
        ChartPredicates.IsStrongTreasureAnchors(borders);

    public static bool IsStrongInfiniteLanternsCount(int count) =>
        ChartPredicates.IsStrongInfiniteLanternsCount(count);

    public static bool IsStrongInfiniteLanterns(IEnumerable<string> borderNames) =>
        ChartPredicates.IsStrongInfiniteLanterns(borderNames);

    public static bool IsStrongInfiniteLanterns(IReadOnlyList<BorderEffect> borders) =>
        ChartPredicates.IsStrongInfiniteLanterns(borders);

    public static int OrbPriority(IReadOnlyList<BorderEffect> borders) =>
        ChartPredicates.OrbPriority(borders);
}
