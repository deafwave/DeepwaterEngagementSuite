using System.Collections.Generic;
using System.Linq;
using DeepwaterEngagementSuite;
using DeepwaterEngagementSuite.VoyagePlannerData;
using DeepwaterEngagementSuite.VoyagePlannerData.Strategies;
using Xunit;

namespace DeepwaterEngagementSuite.Tests;

public class ShortestPathStrategyTests
{
    private static MapPiece Chart(int id, PieceType type, Direction connections, string name = "Chart",
        params Modifier[] extraMods) =>
        new(id, type, connections,
            [new Modifier("Default", 1), ..extraMods],
            name);

    private static IReadOnlyList<BorderEffect>[,] EmptyBorders()
    {
        var borders = new IReadOnlyList<BorderEffect>[3, 3];
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
            borders[r, c] = [];
        return borders;
    }

    private static IReadOnlyList<BorderEffect>[,] CurrencyBorders()
    {
        var borders = EmptyBorders();
        
        borders[1, 1] =
        [
            new BorderEffect("DeepwaterBorderMoreCurrency3", ModifierTag.Currency, 50.0, false, false),
        ];
        return borders;
    }

    [Fact]
    public void Path_metrics_prefer_zero_dead_ends_over_snake()
    {
        
        var fullGrid = new int[9];
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
        {
            var cell = r * 3 + c;
            Direction conn = 0;
            if (r > 0) conn |= Direction.Down;
            if (r < 2) conn |= Direction.Up;
            if (c > 0) conn |= Direction.Left;
            if (c < 2) conn |= Direction.Right;
            fullGrid[cell] = (int)conn;
        }

        
        var snake = new int[9];
        void Link(int a, int b)
        {
            var ar = a / 3;
            var ac = a % 3;
            var br = b / 3;
            var bc = b % 3;
            if (br == ar + 1)
            {
                snake[a] |= (int)Direction.Up;
                snake[b] |= (int)Direction.Down;
            }
            else if (br == ar - 1)
            {
                snake[a] |= (int)Direction.Down;
                snake[b] |= (int)Direction.Up;
            }
            else if (bc == ac + 1)
            {
                snake[a] |= (int)Direction.Right;
                snake[b] |= (int)Direction.Left;
            }
            else if (bc == ac - 1)
            {
                snake[a] |= (int)Direction.Left;
                snake[b] |= (int)Direction.Right;
            }
        }

        
        Link(0, 1); Link(1, 2);
        Link(2, 5); Link(5, 4); Link(4, 3);
        Link(3, 6); Link(6, 7); Link(7, 8);

        var full = VoyagePathMetrics.AnalyzeTopology(fullGrid);
        var path = VoyagePathMetrics.AnalyzeTopology(snake);

        Assert.True(full.IsConnected);
        Assert.True(path.IsConnected);
        Assert.Equal(8, full.VisitPathLength);
        Assert.Equal(8, path.VisitPathLength);
        Assert.Equal(0, full.InternalDeadEnds);
        Assert.Equal(2, path.InternalDeadEnds);
        Assert.True(VoyagePathMetrics.ScoreTopology(fullGrid) > VoyagePathMetrics.ScoreTopology(snake));
    }

    [Fact]
    public void Strategy_strips_currency_modifiers_and_labels_active()
    {
        var pieces = new List<MapPiece>
        {
            Chart(0, PieceType.Cross, Direction.All, "A",
                new Modifier("MapDeepwaterChartAdjacentStrongboxes3", 35, Tags: ModifierTag.None)),
            Chart(1, PieceType.Tee, Direction.Up | Direction.Left | Direction.Right, "B",
                new Modifier("MapDeepwaterChartVoyageIncreasedRareMonsters", 70, true, ModifierTag.RareMonsters)),
        };

        var ctx = PlacementPipeline.Run(
            pieces,
            EmptyBorders(),
            new VoyageStrategyOptions(
                RareMonstersDrop: false,
                NoConsumeAnchorfield: false,
                CenterSpecialty: false,
                ShortestPath: true));

        Assert.Contains("Shortest Path", ctx.ActiveStrategies);
        Assert.All(ctx.Working, p =>
        {
            Assert.Single(p.Modifiers);
            Assert.Equal("Default", p.Modifiers[0].Name);
            Assert.Equal(1, p.Modifiers[0].Weight);
            Assert.Equal(0, p.LocalModifier + p.GlobalModifier - 1);
        });
    }

    [Fact]
    public void Solver_shortest_path_ignores_currency_borders()
    {
        
        var pieces = new List<MapPiece>();
        for (var i = 0; i < 9; i++)
            pieces.Add(Chart(i, PieceType.Cross, Direction.All, $"Cross{i}"));

        var currencyBorders = CurrencyBorders();

        var currencyPuzzle = new VoyagePuzzle(pieces, currencyBorders, LockedPlacements: null);
        var pathPuzzle = new VoyagePuzzle(pieces, currencyBorders, LockedPlacements: null,
            OptimizeShortestPath: true);

        var currencySolution = new VoyagePlannerFast()
            .Solve(currencyPuzzle, new VoyagePlannerSettings(TopN: 1))
            .Last().Solutions[0];
        var pathSolution = new VoyagePlannerFast()
            .Solve(pathPuzzle, new VoyagePlannerSettings(TopN: 1))
            .Last().Solutions[0];

        var pathMetrics = VoyagePathMetrics.AnalyzeTopology(
            VoyagePathMetrics.BuildInGridMask(pathSolution.Grid));

        Assert.True(pathMetrics.IsConnected);
        Assert.Equal(8, pathMetrics.VisitPathLength);
        Assert.Equal(0, pathMetrics.InternalDeadEnds);

        
        Assert.Equal(
            VoyagePathMetrics.ScoreGrid(pathSolution.Grid),
            pathSolution.TotalScore,
            6);

        
        Assert.NotEqual(currencySolution.TotalScore, pathSolution.TotalScore);
    }

    [Fact]
    public void VoyageSolve_passes_shortest_path_flag()
    {
        var pieces = new List<MapPiece>();
        for (var i = 0; i < 12; i++)
            pieces.Add(Chart(i, PieceType.Cross, Direction.All, $"C{i}",
                new Modifier("MapDeepwaterChartAdjacentStrongboxes3", 99)));

        var session = new VoyageSolve();
        var last = session.Run(
                pieces,
                EmptyBorders(),
                new VoyagePlannerSettings(TopN: 3),
                new VoyageStrategyOptions(
                    RareMonstersDrop: false,
                    NoConsumeAnchorfield: false,
                    CenterSpecialty: false,
                    ShortestPath: true))
            .LastOrDefault();

        Assert.NotNull(last);
        Assert.True(session.Puzzle.OptimizeShortestPath);
        Assert.True(last.Solutions.Count > 0);
        Assert.Contains("Shortest Path", session.Placement.ActiveStrategies);
        Assert.True(session.Placement.SavedShortestPathPremiumCount > 0,
            "Surplus Crosses should be banked for later voyages.");
        Assert.True(session.Placement.Pieces.Count >= 9);
        Assert.True(session.Placement.Pieces.Count < 12,
            "Not all inventory pieces should stay available for this voyage.");

        foreach (var piece in session.Placement.Pieces)
        {
            Assert.Single(piece.Modifiers);
            Assert.Equal("Default", piece.Modifiers[0].Name);
        }

        var metrics = VoyagePathMetrics.AnalyzeTopology(
            VoyagePathMetrics.BuildInGridMask(last.Solutions[0].Grid));
        Assert.Equal(0, metrics.InternalDeadEnds);
        Assert.Equal(8, metrics.VisitPathLength);
    }

    [Theory]
    [InlineData(12, 3, 1, 4)] 
    [InlineData(2, 3, 1, 1)]  
    [InlineData(5, 1, 1, 5)]  
    [InlineData(9, 2, 2, 5)]  
    public void KeepCount_fair_shares_across_voyages(
        int available, int voyages, int minKeep, int expectedKeep)
    {
        Assert.Equal(expectedKeep, ShortestPathStrategy.KeepCount(available, minKeep, voyages));
    }

    [Fact]
    public void Strategy_banks_surplus_crosses_instead_of_burning_them()
    {
        
        var pieces = new List<MapPiece>();
        for (var i = 0; i < 12; i++)
            pieces.Add(Chart(i, PieceType.Cross, Direction.All, $"Cross{i}"));
        for (var i = 12; i < 24; i++)
            pieces.Add(Chart(i, PieceType.Single, Direction.Up, $"Single{i}"));

        var result = VoyagePlacementRules.Apply(
            pieces,
            EmptyBorders(),
            new VoyageStrategyOptions(
                RareMonstersDrop: false,
                NoConsumeAnchorfield: false,
                CenterSpecialty: false,
                ShortestPath: true));

        
        Assert.Equal(4, result.Pieces.Count(p => p.Type == PieceType.Cross));
        Assert.Equal(8, result.SavedShortestPathPremiumCount);
        Assert.Equal(12, result.Pieces.Count(p => p.Type == PieceType.Single));
        Assert.Contains("Shortest Path", result.ActiveStrategies);
    }

    [Fact]
    public void Strategy_does_not_save_when_inventory_is_only_one_voyage()
    {
        var pieces = new List<MapPiece>();
        for (var i = 0; i < 9; i++)
            pieces.Add(Chart(i, PieceType.Cross, Direction.All, $"C{i}"));

        var result = VoyagePlacementRules.Apply(
            pieces,
            EmptyBorders(),
            new VoyageStrategyOptions(
                RareMonstersDrop: false,
                NoConsumeAnchorfield: false,
                CenterSpecialty: false,
                ShortestPath: true));

        Assert.Equal(0, result.SavedShortestPathPremiumCount);
        Assert.Equal(9, result.Pieces.Count);
    }
}
