using System.Collections.Generic;
using System.Linq;

namespace DeepwaterEngagementSuite.VoyagePlannerData.Strategies;

public static class PlacementPipeline
{
    private static readonly IVoyageStrategy[] Strategies =
    [
        new SaveKisharaStrategy(),
        new SaveNoEquipmentStrategy(),
        new SaveFracturedStrategy(),
        new SaveGoldenLanternsStrategy(),
        new SavePantheonStrategy(),
        new SaveSoulEaterStrategy(),
        new SaveRareFractureStrategy(),
        new SaveRarePossessedStrategy(),
        
        
        new SaveStarfishStrategy(),
        new SaveUniqueAmulet2Strategy(),
        new SaveUniqueAmulet1Strategy(),

        new RareMonstersDropLockStrategy(),
        new CenterSpecialtyLockStrategy(),
        new NoConsumeFarmLockStrategy(),

        new NoConsumeFarmSaveStrategy(),
        new RareMonstersDropSaveStrategy(),
        new CenterSpecialtySaveStrategy(),
        new CenterOnlyJewelrySaveStrategy(),

        new ShortestPathStrategy(),
        new ActiveStrategyLabelsStrategy(),
    ];

    public static IReadOnlyList<IVoyageStrategy> All => Strategies;

    public static PlacementContext Run(
        IReadOnlyList<MapPiece> pieces,
        IReadOnlyList<BorderEffect>[,] tileBorders,
        VoyageStrategyOptions options = null)
    {
        var ctx = new PlacementContext(pieces, tileBorders, options);
        foreach (var strategy in Strategies.OrderBy(s => s.Order).ThenBy(s => s.Id))
        {
            if (!IsStrategyEnabled(strategy, ctx))
                continue;
            strategy.Apply(ctx);
        }

        return ctx;
    }

    
    private static bool IsStrategyEnabled(IVoyageStrategy strategy, PlacementContext ctx)
    {
        if (strategy.IsEnabled(ctx.Options))
            return true;

        if (ctx.DivineCenters.Count == 0)
            return false;

        return strategy is RareMonstersDropLockStrategy or RareMonstersDropSaveStrategy;
    }
}
