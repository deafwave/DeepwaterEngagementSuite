namespace DeepwaterEngagementSuite.VoyagePlannerData;


public sealed record VoyageStrategyOptions(
    bool RareMonstersDrop = true,
    bool NoConsumeAnchorfield = true,
    bool CenterSpecialty = true,
    bool TreasureAnchors = true,
    bool InfiniteLanterns = false,
    bool ShortestPath = false,
    int SaveKishara = ChartIds.MaxSavedKishara,
    int SaveNoEquipment = ChartIds.MaxSavedNoEquipment,
    int SaveFractured = ChartIds.MaxSavedFractured,
    int SaveGoldenLanterns = ChartIds.MaxSavedGoldenLanterns,
    int SavePantheon = ChartIds.MaxSavedPantheon,
    int SaveSoulEater = ChartIds.MaxSavedSoulEater,
    int SaveRareFracture = ChartIds.MaxSavedRareFracture,
    int SaveRarePossessed = ChartIds.MaxSavedRarePossessed,
    int SaveStarfish = ChartIds.MaxSavedStarfish,
    int SaveUniqueAmulet2 = ChartIds.MaxSavedUniqueAmulet2,
    int SaveUniqueAmulet1 = ChartIds.MaxSavedUniqueAmulet1)
{
    public static VoyageStrategyOptions AllEnabled { get; } = new();
}
