using System;
using System.Linq;

namespace DeepwaterEngagementSuite.VoyagePlannerData.Strategies;


public sealed class ShortestPathStrategy : IVoyageStrategy
{
    public string Id => "ShortestPath";
    public int Order => StrategyOrders.ShortestPath;

    public bool IsEnabled(VoyageStrategyOptions options) => options.ShortestPath;

    public void Apply(PlacementContext ctx)
    {
        StripCurrencyWeights(ctx);
        var saved = ConservePremiumPiecesForLaterVoyages(ctx);
        ctx.AddSaved(SaveCountKeys.ShortestPathPremium, saved);
        ctx.ActiveStrategies.Add("Shortest Path");
    }

    private static void StripCurrencyWeights(PlacementContext ctx)
    {
        for (var i = 0; i < ctx.Working.Count; i++)
        {
            var piece = ctx.Working[i];
            if (piece.Modifiers.Count == 1 &&
                piece.Modifiers[0].Name == "Default" &&
                piece.Modifiers[0].Weight == 1 &&
                !piece.Modifiers[0].IsGlobal)
            {
                continue;
            }

            ctx.Working[i] = new MapPiece(
                piece.Id,
                piece.Type,
                piece.BaseConnections,
                [new Modifier("Default", 1)],
                piece.Name);
        }
    }

    
    private static int ConservePremiumPiecesForLaterVoyages(PlacementContext ctx)
    {
        if (ctx.Working.Count <= ChartIds.CellsPerVoyage)
            return 0;

        
        var voyagesAhead = Math.Max(1,
            (int)Math.Ceiling(ctx.Working.Count / (double)ChartIds.CellsPerVoyage));

        var saved = 0;
        saved += SaveExcess(ctx, PieceType.Cross, ChartIds.MinKeepCrossShortestPath, voyagesAhead);
        saved += SaveExcess(ctx, PieceType.Tee, ChartIds.MinKeepTeeShortestPath, voyagesAhead);
        saved += SaveExcessTwoWay(ctx, ChartIds.MinKeepTwoWayShortestPath, voyagesAhead);
        return saved;
    }

    private static int SaveExcess(
        PlacementContext ctx,
        PieceType type,
        int minKeep,
        int voyagesAhead)
    {
        var unused = ctx.Working
            .Where(p => !ctx.UsedPieceIds.Contains(p.Id) && p.Type == type)
            .Select(p => p.Id)
            .ToList();

        if (unused.Count == 0)
            return 0;

        var keep = KeepCount(unused.Count, minKeep, voyagesAhead);
        var saveCount = unused.Count - keep;
        if (saveCount <= 0)
            return 0;

        return SaveIds(ctx, unused.Take(saveCount));
    }

    private static int SaveExcessTwoWay(PlacementContext ctx, int minKeep, int voyagesAhead)
    {
        var unused = ctx.Working
            .Where(p => !ctx.UsedPieceIds.Contains(p.Id) &&
                        p.Type is PieceType.Corner or PieceType.Straight)
            .Select(p => p.Id)
            .ToList();

        if (unused.Count == 0)
            return 0;

        var keep = KeepCount(unused.Count, minKeep, voyagesAhead);
        var saveCount = unused.Count - keep;
        if (saveCount <= 0)
            return 0;

        return SaveIds(ctx, unused.Take(saveCount));
    }

    
    public static int KeepCount(int available, int minKeep, int voyagesAhead)
    {
        if (available <= 0)
            return 0;

        voyagesAhead = Math.Max(1, voyagesAhead);
        var fairShare = (int)Math.Ceiling(available / (double)voyagesAhead);
        return Math.Min(available, Math.Max(minKeep, fairShare));
    }

    private static int SaveIds(PlacementContext ctx, System.Collections.Generic.IEnumerable<int> ids)
    {
        var saved = 0;
        foreach (var id in ids)
        {
            
            if (!ctx.TrySavePiece(id, force: false))
                break;
            saved++;
        }

        return saved;
    }
}
