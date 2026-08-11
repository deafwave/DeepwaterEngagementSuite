namespace DeepwaterEngagementSuite.VoyagePlannerData;

public static class ChartIds
{
    public const string NotConsume1 = "DeepwaterBorderChanceToNotConsumeChart1";
    public const string NotConsume2 = "DeepwaterBorderChanceToNotConsumeChart2";
    public const string RareDivine = "DeepwaterBorderRareMonsterDivine";
    public const string RareAnnul = "DeepwaterBorderRareMonsterAnnulment";
    public const string RareAncient = "DeepwaterBorderRareMonsterAncient";
    public const string TreasureAnchors1 = "DeepwaterBorderTreasureAnchors1";
    public const string TreasureAnchors2 = "DeepwaterBorderTreasureAnchors2";
    public const string InfiniteLanterns = "DeepwaterBorderInfiniteLanterns";

    
    public const string InfiniteLanternsDisplayHint =
        "does not reduce your Lantern count";
    public const string NotConsumeDisplayHint =
        "not consume";
    public const string TreasureAnchorsDisplayHint =
        "Treasure Anchor";
    public const string RareDivineDisplayHint = "Divine";
    public const string RareAnnulDisplayHint = "Annulment";
    public const string RareAncientDisplayHint = "Ancient";

    public const string VoyageIncreasedRareMonsters = "MapDeepwaterChartVoyageIncreasedRareMonsters";
    public const string VoyageNoEquipmentDrops = "MapDeepwaterChartVoyageNoEquipmentDrops";
    public const string VoyageSoulEater = "MapDeepwaterChartVoyageSoulEater";
    public const string VoyageRareFracture = "MapDeepwaterChartVoyageRareFracture";
    public const string VoyageMonstersPossessed = "MapDeepwaterChartVoyageMonstersPossessed";

    public const string AdjacentFracturedPrefix = "MapDeepwaterChartAdjacentFractured";
    public const string AdjacentGoldenLanternsPrefix = "MapDeepwaterChartAdjacentGoldenLanterns";
    public const string AdjacentPantheonPrefix = "MapDeepwaterChartAdjacentPantheon";
    public const string AdjacentStrongboxesPrefix = "MapDeepwaterChartAdjacentStrongboxes";
    public const string AdjacentStarfishPrefix = "MapDeepwaterChartAdjacentStarfish";
    public const string AdjacentIncreasedRarePrefix = "MapDeepwaterChartAdjacentIncreasedRareMonsters";
    public const string AdjacentDivinerBoxPrefix = "MapDeepwaterChartAdjacentDivinerBox";
    public const string AdjacentArcanistBoxPrefix = "MapDeepwaterChartAdjacentArcanistBox";
    public const string AdjacentOperativeBoxPrefix = "MapDeepwaterChartAdjacentOperativeBox";
    public const string AdjacentLostMessagePrefix = "MapDeepwaterChartAdjacentLostMessage";
    public const string AdjacentUniqueAmuletPrefix = "MapDeepwaterChartAdjacentUniqueAmulet";
    public const string AdjacentUniqueBeltPrefix = "MapDeepwaterChartAdjacentUniqueBelt";
    public const string AdjacentUniqueRingPrefix = "MapDeepwaterChartAdjacentUniqueRing";

    public const int CenterRow = 1;
    public const int CenterCol = 1;

    public const int MaxSavedBoxes = 6;
    
    
    public const int MaxSavedRareMonsterSupport = MaxSavedBoxes;
    
    public const int MaxSavedStarfish = 2;
    public const int MaxSavedRareVoyage = 5;
    public const int MaxSavedPelagic = 2;
    
    public const int MaxSaveCap = 120;
    
    public const int MaxSavedDefault = MaxSaveCap;
    public const int MaxSavedKishara = MaxSavedDefault;
    /// <summary>When SaveKishara is 0, at most this many Kishara charts stay available for placement.</summary>
    public const int MaxPlacedKisharaWhenNotSaving = 1;
    public const int MaxSavedNoEquipment = MaxSavedDefault;
    public const int MaxSavedFractured = MaxSavedDefault;
    public const int MaxSavedGoldenLanterns = 4;
    public const int MaxSavedPantheon = 2;
    public const int MaxSavedSoulEater = 0;
    public const int MaxSavedRareFracture = MaxSavedDefault;
    public const int MaxSavedRarePossessed = MaxSavedDefault;
    public const int MaxSavedUniqueAmulet2 = 0;
    public const int MaxSavedUniqueAmulet1 = 0;

    
    public const int CellsPerVoyage = 9;
    
    public const int MinKeepCrossShortestPath = 1;
    
    public const int MinKeepTeeShortestPath = 2;
    
    public const int MinKeepTwoWayShortestPath = 3;

    public const string PelagicRoomName = "Pelagic Abyss";
    public const string ClamRoomName = "Clam-infested Shelf";
    public const string AnchorfieldRoomName = "Anchorfield";
    public const string KisharaRoomName = "Kishara's Rest";

    public static readonly (int Row, int Col)[] SacrificeCorners = [(2, 0), (2, 2), (0, 2)];
    public static readonly (int Dr, int Dc)[] Ortho = [(1, 0), (-1, 0), (0, 1), (0, -1)];
}
