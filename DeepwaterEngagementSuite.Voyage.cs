using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeepwaterEngagementSuite.VoyagePlannerData;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using GameOffsets.Native;
using ImGuiNET;
using SharpDX;
using Direction = DeepwaterEngagementSuite.VoyagePlannerData.Direction;
using Vector2 = System.Numerics.Vector2;

namespace DeepwaterEngagementSuite;

public partial class DeepwaterEngagementSuite
{
    private VoyageSolutionResult _result;
    private Task _run;
    private SyncTask<bool> _voyagePlaceTask;
    private VoyageSolve _voyageSolve;
    private VoyageScorer _uiScorer;
    private VoyagePlacementRules.Result _lastPlacement;
    private int _selectedSolutionIndex = 0;
    private bool _voyageSolving;
    private bool _voyageTimedOut;
    private bool _voyageWindowWasOpen;
    private bool _voyageAutoSolvePending;
    private bool _voyageInventoryPrimePending;
    private int _voyageLastReadyChartCount = -1;
    private int _voyageReadyChartStableFrames;
    private long _voyageNodesExplored;
    private long _voyageNodesPruned;
    private double _voyageElapsed;
    private System.Diagnostics.Stopwatch _voyageStopwatch;
    private string _voyageBoardFingerprint;
    private int _voyageSolveGeneration;

    private const int VoyageChartStableFramesRequired = 12;

    public List<NormalInventoryItem> GetAvailableCharts()
    {
        if (GameController.IngameState.IngameUi.VoyageWindow is { IsValid: true, IsVisible: true } voyageWindow)
        {

            var charts = voyageWindow.AvailableCharts;
            if (!charts.Any())
            {
                return [];
            }
            var filters = Settings.VoyageSettings.IgnoredCharts.Content.Where(x => x.Enabled).Select(x => x.Query).ToList();
            if (!filters.Any())
            {
                return charts;
            }

            var chartSize = charts[0].GetClientRectCache.Size;
            var containerRect = voyageWindow.ChartContainer.GetClientRectCache;
            var containerSize = containerRect.Size;
            var inventorySize = new Vector2i(
                (int)Math.Round(containerSize.Width/chartSize.Width),
                (int)Math.Round(containerSize.Height / chartSize.Height));
            var filtered = charts.Select(x =>
                {
                    var coord = ((x.GetClientRectCache.TopLeft - containerRect.TopLeft).ToVector2Num()
                                 / new Vector2(containerSize.Width, containerSize.Height)
                                 * inventorySize)
                        .RoundToVector2I();
                    return (x, new ChartData(x.Item, GameController, coord));
                })
                .Where(x => !filters.Any(f => f.Matches(x.Item2)))
                .Select(x => x.x)
                .ToList();
            return filtered;
        }

        return [];
    }

    private static bool TileHasChart(VoyageTileElement tile) =>
        tile?.ItemContainer?.Entity?.GetComponent<DeepwaterChart>() != null;

    private static bool BoardIsClear(VoyageWindow tree) =>
        tree.Tiles.All(t => !TileHasChart(t));

    private static Element GetVoyageChartInventoryTabBar(VoyageWindow tree) =>
        tree?.GetChildFromIndices(3, 11, 0);

    private static Element GetVoyageChartInventoryInactiveTab(VoyageWindow tree) =>
        tree?.GetChildFromIndices(3, 11, 0, 0);

    private static int? GetActiveVoyageChartInventoryTabIndex(VoyageWindow tree)
    {
        var bar = GetVoyageChartInventoryTabBar(tree);
        if (bar is not { IsValid: true })
            return null;

        foreach (var child in bar.Children)
        {
            if (child is { IsValid: true, IsActive: true })
                return child.IndexInParent;
        }

        foreach (var child in bar.Children)
        {
            if (child is { IsValid: true, isHighlighted: true })
                return child.IndexInParent;
        }

        return null;
    }

    private string GetVoyageChartInventoryTabFingerprint(VoyageWindow tree)
    {
        var activeTab = GetActiveVoyageChartInventoryTabIndex(tree);
        var chartIds = GetAvailableCharts()
            .Where(IsChartItemInteractable)
            .Select(GetChartIdentity)
            .Where(id => id != 0)
            .OrderBy(id => id);
        return $"tab:{activeTab?.ToString() ?? "-"}|charts:{string.Join(",", chartIds)}";
    }

    private async SyncTask<bool> ClickVoyageChartInventoryInactiveTab(VoyageWindow tree, bool needsFocusWiggle)
    {
        var inactiveTab = GetVoyageChartInventoryInactiveTab(tree)
            ?? throw new InvalidOperationException(
                "Voyage chart inventory inactive tab not found at (VoyageWindow)3->11->0->0");

        var winOrigin = GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
        var tabPos = winOrigin + inactiveTab.GetClientRectCache.Center.ToVector2Num();
        Input.SetCursorPos(tabPos);
        if (needsFocusWiggle)
            await WiggleCursorToFocus(tabPos);

        await TaskUtils.NextFrame();
        Input.LeftDown();
        await TaskUtils.NextFrame();
        Input.LeftUp();
        return true;
    }

    private async SyncTask<bool> WaitForVoyageChartInventoryTabChange(
        VoyageWindow tree,
        string beforeFingerprint,
        TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            await TaskUtils.NextFrame();
            if (tree is not { IsValid: true, IsVisible: true })
                return false;
            if (!string.Equals(
                    GetVoyageChartInventoryTabFingerprint(tree),
                    beforeFingerprint,
                    StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private async SyncTask<bool> PrimeVoyageChartInventory(VoyageWindow tree)
    {
        try
        {
            var inactiveTab = GetVoyageChartInventoryInactiveTab(tree);
            if (inactiveTab is not { IsValid: true })
                return true;

            var rect = inactiveTab.GetClientRectCache;
            if (rect.Width <= 1 || rect.Height <= 1)
                return true;

            var beforeFingerprint = GetVoyageChartInventoryTabFingerprint(tree);
            const int maxAttempts = 12;
            var switched = false;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                await ClickVoyageChartInventoryInactiveTab(tree, needsFocusWiggle: attempt == 0);
                if (await WaitForVoyageChartInventoryTabChange(
                        tree,
                        beforeFingerprint,
                        TimeSpan.FromMilliseconds(300)))
                {
                    switched = true;
                    break;
                }
            }

            if (!switched)
            {
                DebugWindow.LogError(
                    "Voyage inventory prime: active tab did not change after repeated swaps; " +
                    "other-page chart data may still be unloaded.");
            }

            return switched;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Voyage inventory prime failed: {ex.Message}");
            return false;
        }
        finally
        {
            _voyageInventoryPrimePending = false;
            _voyageLastReadyChartCount = -1;
            _voyageReadyChartStableFrames = 0;
        }
    }

    private static bool IsChartItemInteractable(NormalInventoryItem item) =>
        item is { IsValid: true, IsVisible: true } &&
        item.GetClientRectCache is { Width: > 1, Height: > 1 };

    private static long GetChartIdentity(NormalInventoryItem item) =>
        item?.Item?.Address ?? item?.Entity?.Address ?? item?.Address ?? 0;

    private NormalInventoryItem FindChartByIdentity(long identity)
    {
        if (identity == 0)
            return null;
        return GetAvailableCharts().FirstOrDefault(c => GetChartIdentity(c) == identity);
    }

    private async SyncTask<(NormalInventoryItem Item, bool NeedsFocusWiggle)> EnsureChartItemOnActiveTab(
        VoyageWindow tree,
        NormalInventoryItem pieceElem,
        Vector2 winOrigin,
        bool needsFocusWiggle)
    {
        if (IsChartItemInteractable(pieceElem))
            return (pieceElem, needsFocusWiggle);

        var identity = GetChartIdentity(pieceElem);
        await ClickVoyageChartInventoryInactiveTab(tree, needsFocusWiggle);
        needsFocusWiggle = false;

        await TaskUtils.CheckEveryFrameWithThrow(
            () =>
            {
                var current = identity != 0 ? FindChartByIdentity(identity) ?? pieceElem : pieceElem;
                return IsChartItemInteractable(current);
            },
            () => "Chart item still not visible after switching inventory tab",
            TimeSpan.FromSeconds(2));

        var resolved = identity != 0 ? FindChartByIdentity(identity) ?? pieceElem : pieceElem;
        if (!IsChartItemInteractable(resolved))
            throw new InvalidOperationException("Chart item not interactable after inventory tab switch");

        return (resolved, needsFocusWiggle);
    }

    private static DeepwaterChart TryGetTileChart(VoyageTileElement tile) =>
        tile?.ItemContainer?.Entity?.GetComponent<DeepwaterChart>();

    private static int? TryGetTileRotation(VoyageTileElement tile) =>
        TryGetTileChart(tile)?.Rotation;

    
    private static Direction? TryGetTileConnections(VoyageTileElement tile)
    {
        var chart = TryGetTileChart(tile);
        if (chart == null)
            return null;
        return ((Direction)chart.Room.Path).RotateCcw(chart.Rotation);
    }

    private static bool TileMatchesPlacement(VoyageTileElement tile, MapPiecePlacement expected)
    {
        if (expected?.Piece == null)
            return !TileHasChart(tile);

        
        return TryGetTileConnections(tile) == expected.Connections;
    }

    private static async SyncTask<bool> WiggleCursorToFocus(Vector2 screenPos)
    {
        const float delta = 4f;
        Input.SetCursorPos(screenPos + new Vector2(delta, 0));
        await TaskUtils.NextFrame();
        Input.SetCursorPos(screenPos + new Vector2(-delta, 0));
        await TaskUtils.NextFrame();
        Input.SetCursorPos(screenPos + new Vector2(0, delta));
        await TaskUtils.NextFrame();
        Input.SetCursorPos(screenPos);
        await TaskUtils.NextFrame();
        return true;
    }

    private async SyncTask<bool> WaitChartPlacementDelay()
    {
        var ms = Settings.VoyageSettings.ChartPlacementDelayMs.Value;
        if (ms <= 0)
            return true;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
            await TaskUtils.NextFrame();
        return true;
    }

    private async SyncTask<bool> RotateTileToMatch(
        VoyageTileElement tile,
        MapPiecePlacement expected,
        Vector2 winOrigin)
    {
        await TaskUtils.CheckEveryFrameWithThrow(
            () => TileHasChart(tile),
            () => "Tile has no chart to rotate",
            TimeSpan.FromSeconds(1));

        if (TileMatchesPlacement(tile, expected))
            return true;

        
        for (var click = 0; click < 4; click++)
        {
            if (TileMatchesPlacement(tile, expected))
                return true;

            var beforeRot = TryGetTileRotation(tile);
            if (beforeRot is null)
                throw new InvalidOperationException("Chart rotation unavailable while rotating");

            DebugWindow.LogMsg(
                $"Voyage Place rotate: tile rot {beforeRot} → target rot {expected.Rotation} " +
                $"(conn {TryGetTileConnections(tile)} → {expected.Connections})");

            var clickPos = winOrigin + tile.GetClientRectCache.Center.ToVector2Num();
            Input.SetCursorPos(clickPos);
            await TaskUtils.CheckEveryFrameWithThrow(
                () => GameController.IngameState.UIHover?.Address.Equals(tile.ItemContainer.Address) ?? false,
                TimeSpan.FromSeconds(1));
            Input.RightDown();
            await TaskUtils.NextFrame();
            Input.RightUp();
            await WaitChartPlacementDelay();
            await TaskUtils.CheckEveryFrameWithThrow(
                () =>
                {
                    if (TileMatchesPlacement(tile, expected))
                        return true;
                    var now = TryGetTileRotation(tile);
                    return now is { } rot && rot != beforeRot;
                },
                () => $"Rotation did not change after right-click (was {beforeRot})",
                TimeSpan.FromSeconds(1));
        }

        if (!TileMatchesPlacement(tile, expected))
        {
            throw new InvalidOperationException(
                $"Tile still wrong after 4 rotations: got conn {TryGetTileConnections(tile)}/rot {TryGetTileRotation(tile)}, " +
                $"expected conn {expected.Connections}/rot {expected.Rotation}");
        }

        return true;
    }

    
    private async SyncTask<bool> EnsureAllRotations(
        VoyageSolution solution,
        VoyageWindow tree,
        Vector2 winOrigin)
    {
        for (var i = 0; i < 9; i++)
        {
            var tile = tree.Tiles[i];
            var p = solution.Grid[i / 3, i % 3];
            if (p?.Piece == null)
                continue;

            if (TileMatchesPlacement(tile, p))
                continue;

            DebugWindow.LogMsg(
                $"Voyage Place: final pass fixing tile {i} " +
                $"(got {TryGetTileConnections(tile)}/{TryGetTileRotation(tile)}, " +
                $"want {p.Connections}/{p.Rotation})");
            await RotateTileToMatch(tile, p, winOrigin);
        }

        for (var i = 0; i < 9; i++)
        {
            var tile = tree.Tiles[i];
            var p = solution.Grid[i / 3, i % 3];
            if (p?.Piece == null)
                continue;
            if (!TileMatchesPlacement(tile, p))
            {
                throw new InvalidOperationException(
                    $"Board orientation check failed at tile {i}: " +
                    $"got conn {TryGetTileConnections(tile)}/rot {TryGetTileRotation(tile)}, " +
                    $"expected conn {p.Connections}/rot {p.Rotation}");
            }
        }

        return true;
    }

    private async SyncTask<bool> PlacePieces(VoyageSolution solution)
    {
        try
        {
            var tree = GameController.IngameState.IngameUi.VoyageWindow;
            var winOrigin = GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
            var needsFocusWiggle = true;

            if (!BoardIsClear(tree))
            {
                var clearPos = winOrigin + tree.ClearButton.GetClientRectCache.Center.ToVector2Num();
                Input.SetCursorPos(clearPos);
                if (needsFocusWiggle)
                {
                    await WiggleCursorToFocus(clearPos);
                    needsFocusWiggle = false;
                }

                await TaskUtils.CheckEveryFrameWithThrow(
                    () => tree.ClearButton.HasShinyHighlight,
                    () => "Clear button never highlighted (board may already be empty?)",
                    TimeSpan.FromSeconds(2));
                Input.LeftDown();
                await TaskUtils.NextFrame();
                Input.LeftUp();
                await WaitChartPlacementDelay();
                await TaskUtils.CheckEveryFrameWithThrow(
                    () => BoardIsClear(tree),
                    () => "Board still has charts after Clear",
                    TimeSpan.FromSeconds(3));
            }

            var availableCharts = GetAvailableCharts();
            for (var i = 0; i < 9; i++)
            {
                var tile = tree.Tiles[i];
                var p = solution.Grid[i / 3, i % 3];
                if (p?.Piece == null)
                    continue;
                if (p.Piece.Id < 0 || p.Piece.Id >= availableCharts.Count)
                {
                    DebugWindow.LogError($"Voyage Place: piece id {p.Piece.Id} out of range ({availableCharts.Count} charts)");
                    continue;
                }

                var ensureResult = await EnsureChartItemOnActiveTab(
                    tree, availableCharts[p.Piece.Id], winOrigin, needsFocusWiggle);
                var pieceElem = ensureResult.Item;
                needsFocusWiggle = ensureResult.NeedsFocusWiggle;
                availableCharts[p.Piece.Id] = pieceElem;
                var click1Pos = winOrigin + pieceElem.GetClientRectCache.Center.ToVector2Num();
                var click2Pos = winOrigin + tile.GetClientRectCache.Center.ToVector2Num();
                Input.SetCursorPos(click1Pos);
                if (needsFocusWiggle)
                {
                    await WiggleCursorToFocus(click1Pos);
                    needsFocusWiggle = false;
                }

                await TaskUtils.CheckEveryFrameWithThrow(
                    () => GameController.IngameState.UIHover?.Address.Equals(pieceElem.Address) ?? false,
                    () => $"Hover address was {GameController.IngameState.UIHover?.Address:X} not {pieceElem.Address:X}",
                    TimeSpan.FromSeconds(1));
                Input.LeftDown();
                await TaskUtils.NextFrame();
                Input.LeftUp();
                await WaitChartPlacementDelay();
                await TaskUtils.CheckEveryFrameWithThrow(
                    () => GameController.IngameState.IngameUi.Cursor.Action == MouseActionType.HoldItemForSell,
                    TimeSpan.FromSeconds(1));
                Input.SetCursorPos(click2Pos);
                await TaskUtils.CheckEveryFrameWithThrow(
                    () => GameController.IngameState.UIHoverElement?.Address.Equals(tile.Address) ?? false,
                    () => $"Hover address was {GameController.IngameState.UIHoverElement?.Address:X} not {tile.Address:X}",
                    TimeSpan.FromSeconds(1));
                Input.LeftDown();
                await TaskUtils.NextFrame();
                Input.LeftUp();
                await WaitChartPlacementDelay();
                await TaskUtils.CheckEveryFrameWithThrow(
                    () => GameController.IngameState.IngameUi.Cursor.Action == MouseActionType.Free &&
                          TileHasChart(tile),
                    TimeSpan.FromSeconds(1));

                await RotateTileToMatch(tile, p, winOrigin);
            }

            
            await EnsureAllRotations(solution, tree, winOrigin);
            return true;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Voyage Place failed: {ex.Message}");
            return false;
        }
    }

    private void DrawVoyageHighlights()
    {
        var settings = Settings.VoyageSettings;
        if (!settings.EnableVoyageHandling)
            return;

        if (Input.IsKeyDown(Keys.Escape) && _voyagePlaceTask != null)
        {
            _voyagePlaceTask = null;
        }

        VoyageWindow tree;
        try
        {
            tree = GameController?.IngameState?.IngameUi?.VoyageWindow;
        }
        catch (Exception ex)
        {
            _voyagePlaceTask = null;
            DebugWindow.LogError(ex.ToString());
            return;
        }

        if (tree is not { IsValid: true, IsVisible: true })
        {
            _voyagePlaceTask = null;
            _voyageWindowWasOpen = false;
            _voyageAutoSolvePending = false;
            _voyageInventoryPrimePending = false;
            _voyageLastReadyChartCount = -1;
            _voyageReadyChartStableFrames = 0;
            _voyageBoardFingerprint = null;
            InvalidateVoyageSolveState(clearResults: true);
            return;
        }

        if (!_voyageWindowWasOpen)
        {
            _voyageWindowWasOpen = true;
            _voyageAutoSolvePending = true;
            _voyageInventoryPrimePending = true;
            _voyageLastReadyChartCount = -1;
            _voyageReadyChartStableFrames = 0;
            _voyageBoardFingerprint = null;
        }

        if (_voyageInventoryPrimePending && _voyagePlaceTask == null)
            _voyagePlaceTask = PrimeVoyageChartInventory(tree);

        TaskUtils.RunOrRestart(ref _voyagePlaceTask, () => null);

        if (Settings.VoyageSettings.DumpVoyageStateHotkey.PressedOnce())
            DumpVoyageStateToFile(tree, "hotkey dump");

        var boardFingerprint = BuildVoyageBoardFingerprint(tree);
        if (!string.Equals(boardFingerprint, _voyageBoardFingerprint, StringComparison.Ordinal))
        {
            _voyageBoardFingerprint = boardFingerprint;
            InvalidateVoyageSolveState(clearResults: true);
            _voyageAutoSolvePending = true;
            _voyageLastReadyChartCount = -1;
            _voyageReadyChartStableFrames = 0;
        }

        if (!_voyageInventoryPrimePending &&
            _voyageAutoSolvePending &&
            _run is not { IsCompleted: false })
            TryStartAutoVoyageSolve(tree);

        var modsPerTileIndex = GetTileMods(tree);

        var tiles = tree.Tiles;
        var boardHasStrategyOrb = modsPerTileIndex.Values
            .Any(tileMods => VoyagePlacementRules.HasStrategyOrb(tileMods.Select(m => m.Id)));
        var allBorderNames = modsPerTileIndex.Values
            .SelectMany(tileMods => tileMods.Select(m => m.Id))
            .ToList();
        var boardStrongTreasureAnchors = Settings.VoyageSettings.Strategies.TreasureAnchors.Value
            && VoyagePlacementRules.IsStrongTreasureAnchors(allBorderNames);
        var boardStrongInfiniteLanterns = Settings.VoyageSettings.Strategies.InfiniteLanterns.Value
            && VoyagePlacementRules.IsStrongInfiniteLanterns(allBorderNames);

        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var mods = modsPerTileIndex.GetValueOrDefault(index) ?? [];
            if (settings.ShowScoreDebugDetails.Value)
            {
                var tileTopLeft = tile.GetClientRectCache.TopLeft.ToVector2Num();
                Graphics.DrawTextWithBackground($"({index / 3}, {index % 3})", tileTopLeft, Color.Black);
            }
            var tileCenter = tile.GetClientRectCache.Center.ToVector2Num();
            var chart = tile.ItemContainer?.Entity?.GetComponent<DeepwaterChart>();
            if (chart != null)
            {
                var chartModOffset = -10f;

                if (VoyagePlacementRules.TrySpecialtyRoomLabel(chart.Room.Name, out var roomLabel))
                {
                    var roomText = roomLabel;
                    var roomSize = Graphics.MeasureText(roomText);
                    chartModOffset -= roomSize.Y;
                    Graphics.DrawTextWithBackground(roomText, tileCenter + new Vector2(0, chartModOffset),
                        Color.Orange, FontAlign.Center, Color.Black);
                }

                var chartMods = tile.ItemContainer.Entity.GetComponent<Mods>()?.ImplicitMods ?? [];
                foreach (var im in chartMods)
                {
                    var isCombo = VoyagePlacementRules.IsSpecialtyComboModifier(im.RawName)
                                  || (boardHasStrategyOrb &&
                                      VoyagePlacementRules.IsIncreasedRareStrategyModifier(im.RawName));
                    var show = Settings.VoyageSettings.ShowAllChartModifiers || isCombo;
                    if (!show)
                        continue;

                    var chartMod = Settings.VoyageSettings.ChartModifiers.Content
                        .FirstOrDefault(cm => cm.Id.Value.Equals(im.RawName, StringComparison.OrdinalIgnoreCase));
                    if (!isCombo && chartMod?.HighlightColor.Value is not { A: > 0 })
                        continue;

                    var displayName = chartMod?.Label.Value is { Length: > 0 } label
                        ? label
                        : TrimChartPrefix(im.RawName);
                    var prefix = chartMod?.IsGlobal.Value == true ? "[G] " : "";
                    var weight = chartMod?.Weight.Value ?? 0;
                    var chartName = chartMod != null
                        ? $"{prefix}{displayName}\n({weight:F1})"
                        : displayName;
                    var textSize = Graphics.MeasureText(chartName);
                    chartModOffset -= textSize.Y;
                    var color = isCombo
                        ? Color.Orange
                        : chartMod.HighlightColor.Value;
                    Graphics.DrawTextWithBackground(chartName, tileCenter + new Vector2(0, chartModOffset),
                        color, FontAlign.Center, Color.Black);
                }
            }

            tileCenter = tileCenter + new Vector2(0, 10);
            foreach (var borderMod in mods)
            {
                var isStrategy = VoyagePlacementRules.IsStrategyBorder(borderMod.Id);
                var isTreasure = boardStrongTreasureAnchors &&
                                 VoyagePlacementRules.IsTreasureAnchorsBorder(borderMod.Id);
                var isInfiniteLanterns = boardStrongInfiniteLanterns &&
                                        VoyagePlacementRules.IsInfiniteLanternsBorder(borderMod.Id);
                var isCombo = isStrategy || isTreasure || isInfiniteLanterns;
                if (!Settings.VoyageSettings.ShowAllBorderModifiers && !isCombo)
                    continue;

                var matchingSetting = FindBorderSetting(borderMod.Id, borderMod.DisplayText);
                var text = FormatBorderOverlayLabel(borderMod);
                var color = ChartPredicates.BorderIdOrDisplayMatches(
                        borderMod.Id, ChartIds.RareDivine, ChartIds.RareDivineDisplayHint)
                    ? Color.HotPink
                    : isCombo
                        ? Color.Orange
                        : matchingSetting?.HighlightColor.Value is { A: > 0 } c ? c : Color.Cyan;
                var size = Graphics.DrawTextWithBackground(text, tileCenter, color, FontAlign.Center, Color.Black);
                tileCenter.Y += size.Y;
            }
        }

        var charts = GetAvailableCharts();
        var specialtyIndices = GetInventorySpecialtyIndices(charts);

        for (int i = 0; i < charts.Count; i++)
        {
            if (!IsChartItemInteractable(charts[i]))
                continue;

            var pos = charts[i].GetClientRectCache.TopLeft.ToVector2Num();
            if (specialtyIndices.Contains(i))
            {
                var exclSize = Graphics.DrawTextWithBackground("!", pos, Color.Orange, Color.Black);
                pos.Y += exclSize.Y;
            }

            if (Settings.VoyageSettings.ShowChartInventoryInformation)
            {
                var size = Graphics.DrawTextWithBackground($"#{i}", pos, Color.Black);
                var chartMods = charts[i].Entity.GetComponent<Mods>()?.ImplicitMods ?? [];

                foreach (var chartMod in chartMods)
                {
                    var chartSettings = Settings.VoyageSettings.ChartModifiers.Content
                        .FirstOrDefault(cm => cm.Id.Value.Equals(chartMod.RawName, StringComparison.OrdinalIgnoreCase));
                    if (chartSettings != null && !string.IsNullOrEmpty(chartSettings.Label.Value))
                    {
                        pos.Y += size.Y;
                        Graphics.DrawTextWithBackground(chartSettings.Label.Value, pos, chartSettings.HighlightColor, Color.Black);
                    }
                }
            }
        }

        DrawActiveStrategyLabels(tree);

        if (settings.ShowOptimizerWindow.Value)
        {
            ShowVoyageOptimizerWindow(tree,tiles);
        }
    }

    private void DrawActiveStrategyLabels(VoyageWindow tree)
    {
        var placement = _lastPlacement ?? _voyageSolve?.Placement;
        if (placement == null)
            return;

        var names = DescribeActiveStrategies(placement);
        if (names.Count == 0)
            return;

        DrawStrategyLabelsOnCompass(tree, names);
        DrawStrategyLabelsAboveClear(tree, names);
    }

    private void DrawStrategyLabelsOnCompass(VoyageWindow tree, IReadOnlyList<string> names)
    {
        Element target;
        try
        {
            target = tree.GetChildFromIndices(3, 7);
        }
        catch
        {
            return;
        }

        if (target is not { IsValid: true })
            return;

        var rect = target.GetClientRectCache;
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var pos = rect.Center.ToVector2Num();
        foreach (var name in names)
        {
            var size = Graphics.DrawTextWithBackground(
                name, pos, StrategyDisplayColor(name), FontAlign.Center, Color.Black);
            pos.Y += size.Y;
        }
    }

    private void DrawStrategyLabelsAboveClear(VoyageWindow tree, IReadOnlyList<string> names)
    {
        var clear = tree.ClearButton;
        if (clear == null)
            return;

        var rect = clear.GetClientRectCache;
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var pos = new Vector2(rect.Center.X, rect.Top);
        foreach (var name in names)
        {
            var size = Graphics.MeasureText(name);
            pos.Y -= size.Y;
            Graphics.DrawTextWithBackground(
                name, pos, StrategyDisplayColor(name), FontAlign.Center, Color.Black);
        }
    }

    
    private static readonly Dictionary<int, int[]> BorderSlotToTile =
        new()
        {
            [0] = [0, 11],
            [1] = [1],
            [2] = [2, 3],
            [3] = [10],
            [4] = [],
            [5] = [4],
            [6] = [8, 9],
            [7] = [7],
            [8] = [5, 6],
        };

    
    private static IReadOnlyList<string> ReadBorderModRawNames(VoyageWindow tree) =>
        (tree?.Data?.BorderMods ?? [])
        .Select(m => m?.RawName ?? "")
        .ToList();

    
    private static Element GetBorderModsUiRoot(VoyageWindow tree) =>
        tree?.GetChildFromIndices(3, 10);

    private static IReadOnlyList<string> ReadBorderModUiTexts(VoyageWindow tree)
    {
        var root = GetBorderModsUiRoot(tree);
        if (root is not { IsValid: true })
            return [];

        var texts = new List<string>(12);
        var childCount = (int)root.ChildCount;
        var limit = Math.Max(12, childCount);
        for (var i = 0; i < limit; i++)
        {
            var slot = root.GetChildAtIndex(i);
            if (slot is not { IsValid: true })
            {
                if (i < 12)
                    texts.Add("");
                continue;
            }

            
            var tooltip = slot.Tooltip;
            var raw = tooltip?.TextNoTags ?? tooltip?.Text ?? "";
            texts.Add(NormalizeBorderUiText(raw));
        }

        while (texts.Count < 12)
            texts.Add("");

        return texts;
    }


    private static string NormalizeBorderUiText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Trim();
        
        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            text = text.Substring(braceStart + 1, braceEnd - braceStart - 1);

        text = text.Replace("<augmented>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("</augmented>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("<default>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("</default>", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return text;
    }

    
    private static bool BorderModsFromDataUsable(IReadOnlyList<string> rawNames) =>
        rawNames != null &&
        rawNames.Count >= 12 &&
        rawNames.Count(n => !string.IsNullOrWhiteSpace(n)) >= 1;

    private static bool IsUiBorderSource(BorderModRef border) =>
        border?.Source != null &&
        border.Source.StartsWith("UI", StringComparison.OrdinalIgnoreCase);

    
    private static string FormatBorderOverlayLabel(BorderModRef border)
    {
        var text = !string.IsNullOrEmpty(border?.Id) ? border.Id : border?.Label ?? "";
        if (text.StartsWith("DeepwaterBorder", StringComparison.OrdinalIgnoreCase))
            text = text["DeepwaterBorder".Length..];
        if (IsUiBorderSource(border))
            text = $"T!{text}!!";
        return text;
    }

    
    private string ResolveBorderId(string rawName, string displayText)
    {
        if (!string.IsNullOrWhiteSpace(rawName) &&
            rawName.StartsWith("DeepwaterBorder", StringComparison.OrdinalIgnoreCase))
            return rawName;

        var text = !string.IsNullOrWhiteSpace(displayText) ? displayText : rawName ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return rawName ?? "";

        var fromMap = BorderDisplayMap.TryResolveId(text);
        if (!string.IsNullOrEmpty(fromMap))
            return fromMap;

        var setting = FindBorderSetting(text, text);
        if (setting != null && !string.IsNullOrWhiteSpace(setting.Id.Value))
            return setting.Id.Value;

        return text;
    }

    private VoyageBorderModifier FindBorderSetting(string id, string displayText)
    {
        var content = Settings.VoyageSettings.BorderModifiers.Content;
        if (!string.IsNullOrWhiteSpace(id))
        {
            var byId = content.FirstOrDefault(c =>
                c.Id.Value.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        if (string.IsNullOrWhiteSpace(displayText))
            return null;

        
        VoyageBorderModifier best = null;
        var bestScore = 0;
        foreach (var setting in content)
        {
            var abbv = setting.Abbreviation?.Value;
            if (string.IsNullOrWhiteSpace(abbv) || abbv.Length < 4)
                continue;

            var words = System.Text.RegularExpressions.Regex
                .Split(abbv, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|([0-9]+)")
                .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 1)
                .ToList();
            if (words.Count == 0)
                continue;

            var hits = words.Count(w => displayText.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (hits == words.Count && hits > bestScore)
            {
                best = setting;
                bestScore = hits;
            }
        }

        return best;
    }

    private Dictionary<int, List<BorderModRef>> GetTileMods(VoyageWindow tree)
    {
        var fromData = ReadBorderModRawNames(tree);
        var fromUi = ReadBorderModUiTexts(tree);

        var slots = new BorderModRef[12];
        var any = false;
        for (var i = 0; i < 12; i++)
        {
            var raw = i < fromData.Count ? fromData[i] : "";
            var display = i < fromUi.Count ? fromUi[i] : "";
            if (string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(display))
            {
                slots[i] = null;
                continue;
            }

            any = true;
            var fromApi = !string.IsNullOrWhiteSpace(raw) &&
                          raw.StartsWith("DeepwaterBorder", StringComparison.OrdinalIgnoreCase);
            var id = ResolveBorderId(raw, display);
            slots[i] = new BorderModRef
            {
                Id = id,
                DisplayText = display,
                Source = fromApi ? "Data.BorderMods" : "UI 3->10->i.Tooltip",
            };
        }

        if (!any)
            return new Dictionary<int, List<BorderModRef>>();

        return BorderSlotToTile.ToDictionary(
            kv => kv.Key,
            kv => kv.Value
                .Select(slot => slot >= 0 && slot < slots.Length ? slots[slot] : null)
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id))
                .ToList());
    }

    private static int CountReadyCharts(VoyageWindow tree)
    {
        var charts = tree?.AvailableCharts;
        if (charts == null || charts.Count == 0)
            return 0;

        var ready = 0;
        foreach (var chart in charts)
        {
            if (chart?.Item != null && chart.Item.TryGetComponent(out DeepwaterChart _))
                ready++;
        }

        return ready;
    }

    private void TryStartAutoVoyageSolve(VoyageWindow tree)
    {
        var ready = CountReadyCharts(tree);
        var total = tree.AvailableCharts?.Count ?? 0;

        if (ready <= 0 || ready != total)
        {
            _voyageLastReadyChartCount = ready;
            _voyageReadyChartStableFrames = 0;
            return;
        }

        if (ready != _voyageLastReadyChartCount)
        {
            _voyageLastReadyChartCount = ready;
            _voyageReadyChartStableFrames = 0;
            return;
        }

        _voyageReadyChartStableFrames++;
        if (_voyageReadyChartStableFrames < VoyageChartStableFramesRequired)
            return;

        _voyageAutoSolvePending = false;
        StartVoyageSolve(tree);
    }

    private void StartVoyageSolve(VoyageWindow tree)
    {
        if (tree is not { IsValid: true, IsVisible: true })
            return;
        if (_voyageInventoryPrimePending)
        {
            _voyageAutoSolvePending = true;
            if (_voyagePlaceTask == null)
                _voyagePlaceTask = PrimeVoyageChartInventory(tree);
            return;
        }

        if (_run is { IsCompleted: false })
        {
            _voyageSolve?.Cancel();
            _voyageAutoSolvePending = true;
            return;
        }

        _voyageAutoSolvePending = false;
        _voyageSolve?.Cancel();
        _result = null;
        _lastPlacement = null;
        _selectedSolutionIndex = 0;
        _voyageNodesExplored = 0;
        _voyageNodesPruned = 0;
        _voyageElapsed = 0;
        _voyageTimedOut = false;
        _voyageSolving = true;
        _voyageStopwatch = System.Diagnostics.Stopwatch.StartNew();

        var pieces = BuildMapPiecesFromAvailableCharts();
        var tileBorders = BuildTileBorders(tree);
        var timeLimitSetting = Settings.VoyageSettings.SolverTimeLimitSeconds.Value;
        var strategyOptions = Settings.VoyageSettings.Strategies.ToOptions();
        var generation = ++_voyageSolveGeneration;

        _run = Task.Run(() =>
        {
            try
            {
                var session = new VoyageSolve();
                if (generation != _voyageSolveGeneration)
                    return;

                _voyageSolve = session;

                foreach (var r in session.Run(
                             pieces,
                             tileBorders,
                             settings: new VoyagePlannerSettings(TimeLimitSeconds: timeLimitSetting),
                             strategyOptions: strategyOptions))
                {
                    if (generation != _voyageSolveGeneration)
                        return;
                    _result = r;
                    _voyageNodesExplored = r.NodesExplored;
                    _voyageNodesPruned = r.NodesPruned;
                    _uiScorer = session.Scorer;
                }

                if (generation != _voyageSolveGeneration)
                    return;

                _uiScorer = session.Scorer;
                _lastPlacement = session.Placement;
                LogPlacement(session.Placement);

                if (_voyageStopwatch.Elapsed.TotalSeconds >= timeLimitSetting)
                    _voyageTimedOut = true;
            }
            finally
            {
                if (generation == _voyageSolveGeneration)
                    _voyageSolving = false;
            }
        });
    }

    private void InvalidateVoyageSolveState(bool clearResults)
    {
        _voyageSolveGeneration++;
        _voyageSolve?.Cancel();
        if (!clearResults)
            return;

        _result = null;
        _lastPlacement = null;
        _uiScorer = null;
        _selectedSolutionIndex = 0;
        _voyageNodesExplored = 0;
        _voyageNodesPruned = 0;
        _voyageElapsed = 0;
        _voyageTimedOut = false;
        if (_run is not { IsCompleted: false })
            _voyageSolving = false;
    }

    private static string BuildVoyageBoardFingerprint(VoyageWindow tree)
    {
        var parts = new List<string>();
        var fromData = ReadBorderModRawNames(tree);
        var fromUi = ReadBorderModUiTexts(tree);
        if (BorderModsFromDataUsable(fromData))
        {
            foreach (var name in fromData)
                parts.Add(name ?? "");
        }
        else
        {
            foreach (var text in fromUi)
                parts.Add(text ?? "");
        }

        var charts = tree?.AvailableCharts;
        if (charts != null)
        {
            foreach (var chart in charts)
            {
                if (chart?.Item == null)
                {
                    parts.Add("-");
                    continue;
                }

                var room = chart.Item.TryGetComponent(out DeepwaterChart dc) ? dc.Room.Name ?? "" : "";
                var mods = chart.Item.GetComponent<Mods>()?.ImplicitMods;
                var modNames = mods == null
                    ? ""
                    : string.Join(',', mods.Select(m => m.RawName));
                parts.Add(room + "|" + modNames);
            }
        }

        return string.Join('\n', parts);
    }

    private void ShowVoyageOptimizerWindow(VoyageWindow tree, List<VoyageTileElement> tiles)
    {
        if (!ImGui.Begin("Voyage Optimizer"))
        {
            ImGui.End();
            return;
        }

        _voyageSolving = _run is { IsCompleted: false };

        if (ImGui.Button("Solve"))
            StartVoyageSolve(tree);

        if (_voyageSolving)
        {
            if (_voyageStopwatch != null)
                _voyageElapsed = _voyageStopwatch.Elapsed.TotalSeconds;
            ImGui.SameLine();
            var timeLimitSetting = Settings.VoyageSettings.SolverTimeLimitSeconds.Value;
            var progress = timeLimitSetting > 0 ? Math.Min(1f, (float)(_voyageElapsed / timeLimitSetting)) : 0.5f;
            ImGui.ProgressBar(progress, default, $"{_voyageElapsed:F1}s");
        }

        if (_result != null && _result.Solutions.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Place"))
            {
                if (_selectedSolutionIndex >= _result.Solutions.Count)
                    _selectedSolutionIndex = 0;
                var sol = _result.Solutions[_selectedSolutionIndex];
                _voyagePlaceTask = PlacePieces(sol);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Dump State"))
            DumpVoyageStateToFile(tree, "manual dump from optimizer window");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Writes a replayable JSON snapshot of this board\n" +
                             "(borders, charts, mods, strategy options, locks, solution)\n" +
                             "to ConfigDirectory/voyage-dumps.");

        if (_lastVoyageDumpError != null)
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), $"Dump failed: {_lastVoyageDumpError}");
        }
        else if (_lastVoyageDumpPath != null)
        {
            ImGui.TextColored(Color.Lime.ToImguiVec4(), $"Dumped: {_lastVoyageDumpPath}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy path"))
                ImGui.SetClipboardText(_lastVoyageDumpPath);
        }

        ImGui.Spacing();

        if (_voyageSolving || _result != null || _lastPlacement != null)
        {
            ImGui.Text($"Nodes: {_voyageNodesExplored:N0} explored, {_voyageNodesPruned:N0} pruned");
            if (_voyageSolve is { DroppedLockCount: > 0 } solve)
            {
                var detail = solve.DroppedLocks is { Count: > 0 }
                    ? string.Join("; ", solve.DroppedLocks)
                    : "unknown";
                ImGui.TextColored(Color.Orange.ToImguiVec4(),
                    $"Dropped {solve.DroppedLockCount} strategy lock(s) — no board satisfied all of them:");
                ImGui.TextColored(Color.Orange.ToImguiVec4(), detail);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        "Lowest-priority locks are dropped first so Divine Pelagic/support " +
                        "survive longer than Divine fill / other soft locks.\n" + detail);
            }

            DrawStrategyStatus();
        }

        if (_result == null || _result.Solutions.Count == 0)
        {
            if (_voyageInventoryPrimePending)
            {
                ImGui.TextColored(Color.Yellow.ToImguiVec4(),
                    "Loading chart inventory tabs...");
            }
            else if (_voyageAutoSolvePending)
            {
                var ready = CountReadyCharts(tree);
                var total = tree.AvailableCharts?.Count ?? 0;
                ImGui.TextColored(Color.Yellow.ToImguiVec4(),
                    $"Waiting for charts... ({ready}/{total})");
            }
            else if (_voyageSolving)
            {
                ImGui.TextColored(Color.Yellow.ToImguiVec4(), "Searching...");
            }
            else if (_voyageTimedOut)
            {
                ImGui.TextColored(Color.Orange.ToImguiVec4(), "Time limit reached - no valid solution found.");
                DrawStrategyReservationHint();
            }
            else if (_lastPlacement != null || _result != null)
            {
                ImGui.TextColored(Color.Orange.ToImguiVec4(), "No solutions found.");
                DrawStrategyReservationHint();
            }
            else
            {
                ImGui.TextColored(Color.Gray.ToImguiVec4(), "No solutions yet. Opening voyage auto-solves.");
            }

            ImGui.End();
            return;
        }

        if (_voyageTimedOut)
        {
            ImGui.TextColored(Color.Orange.ToImguiVec4(), $"Time limit reached - showing best solutions found so far (may not be optimal).");
        }

        _selectedSolutionIndex = Math.Clamp(_selectedSolutionIndex, 0, _result.Solutions.Count - 1);
        var currentSolution = _result.Solutions[_selectedSolutionIndex];

        var asciiArt = BuildAsciiGrid(currentSolution.Grid, tiles);

        using (ImGuiHelpers.UseStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0)))
            foreach (var line in asciiArt)
            {
                ImGui.TextUnformatted(line);
            }

        ImGui.Spacing();

        ImGui.Text($"Score: {currentSolution.TotalScore:F2}");
        ImGui.Text($"Valid: {(currentSolution.IsValid ? "Yes" : "No")}");

        if (_result.Solutions.Count > 0)
        {
            ImGui.Spacing();
            if (ImGui.BeginTable("SolutionsList", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("#");
                ImGui.TableSetupColumn("Score");
                ImGui.TableSetupColumn("Valid");
                ImGui.TableSetupColumn("Select");
                ImGui.TableHeadersRow();

                for (int i = 0; i < _result.Solutions.Count; i++)
                {
                    var sol = _result.Solutions[i];
                    ImGui.TableNextRow();
                    ImGui.PushID(i);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{i + 1}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{sol.TotalScore:F2}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{sol.IsValid}");
                    ImGui.TableNextColumn();
                    var isSelected = i == _selectedSolutionIndex;
                    if (isSelected)
                        ImGui.PushStyleColor(ImGuiCol.Button, Color.Green.ToImguiVec4());
                    if (ImGui.Button(isSelected ? "Selected" : "Select"))
                    {
                        _selectedSolutionIndex = i;
                    }

                    if (isSelected)
                        ImGui.PopStyleColor();
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }

        if (Settings.VoyageSettings.ShowScoreDebugDetails.Value)
        {
            var cellScores = _uiScorer?.CellScores(currentSolution.Grid);

            if (ImGui.BeginTable("ScoreBreakdown", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableSetupColumn("Tile", ImGuiTableColumnFlags.WidthFixed, 25);
                ImGui.TableSetupColumn("Piece", ImGuiTableColumnFlags.WidthFixed, 20);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Score", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Mods");
                ImGui.TableHeadersRow();

                for (int i = 0; i < 9; i++)
                {
                    var r = i / 3;
                    var c = i % 3;
                    var placement = currentSolution.Grid[r, c];

                    ImGui.TableNextRow();
                    ImGui.PushID($"tile{i}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{r},{c}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"#{placement.Piece.Id}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{placement.Piece.Type}");
                    ImGui.TableNextColumn();
                    ImGui.Text(cellScores != null ? $"{cellScores[r, c]:F1}" : "-");
                    ImGui.TableNextColumn();
                    var modText = string.Join(", ", placement.Piece.Modifiers.Where(m => m.Name != "Default").Select(m =>
                    {
                        var displayName = TrimChartPrefix(m.Name);
                        var prefix = m.IsGlobal ? "[Global] " : "";
                        return $"{prefix}{displayName}({m.Weight:F1})";
                    }));
                    ImGui.Text(string.IsNullOrEmpty(modText) ? "-" : modText);
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            DrawScoreDetails(currentSolution);
        }

        ImGui.End();
    }

    private void DrawScoreDetails(VoyageSolution solution)
    {
        if (_uiScorer == null)
            return;

        ImGui.Spacing();
        if (!ImGui.TreeNode("Score details"))
            return;

        var explanation = _uiScorer.Explain(solution.Grid);
        for (int i = 0; i < 9; i++)
        {
            var r = i / 3;
            var c = i % 3;
            var placement = solution.Grid[r, c];
            var rows = explanation[r, c];
            var total = rows.Sum(x => x.Value);

            ImGui.PushID($"detail{i}");
            var open = ImGui.TreeNode("node", $"({r},{c}) #{placement.Piece.Id} {placement.Piece.Type} — {total:F1}");
            if (open)
            {
                var borders = _uiScorer.BordersAt(r, c);
                ImGui.TextDisabled(borders.Count > 0
                    ? "Borders: " + string.Join(",  ", borders.Select(FormatBorderEffect))
                    : "No borders touch this tile");

                if (rows.Count == 0)
                {
                    ImGui.TextDisabled("No score contributions");
                }
                else if (ImGui.BeginTable("details", 6,
                             ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Mod");
                    ImGui.TableSetupColumn("From", ImGuiTableColumnFlags.WidthFixed, 75);
                    ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("Mult", ImGuiTableColumnFlags.WidthFixed, 130);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 65);
                    ImGui.TableSetupColumn("Applied borders");
                    ImGui.TableHeadersRow();

                    foreach (var row in rows)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"{(row.IsGlobal ? "[G] " : "")}{TrimChartPrefix(row.ModName)}");
                        ImGui.TableNextColumn();
                        ImGui.Text(row.SourcePieceId < 0
                            ? "-"
                            : row.IsGlobal
                                ? "self"
                                : $"#{row.SourcePieceId} ({row.SourceRow},{row.SourceCol})");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{row.Weight:F1}");
                        ImGui.TableNextColumn();
                        ImGui.Text(row.SourcePieceId < 0
                            ? $"x{row.TileFactor:F2}"
                            : row.IsGlobal
                                ? $"x{row.ChartMultiplier:F2} sum{row.TileFactor:F2}"
                                : $"x{row.ChartMultiplier:F2} x{row.TileFactor:F2}");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{row.Value:F1}");
                        ImGui.TableNextColumn();
                        var applied = row.TileBorders
                            .Select(b => $"{TrimBorderPrefix(b.Name)} x{b.Multiplier:0.##}")
                            .Concat(row.ChartBorders
                                .Select(b => $"{TrimBorderPrefix(b.Name)} x{b.Multiplier:0.##} (boosts chart at ({row.SourceRow},{row.SourceCol}))"))
                            .ToList();
                        ImGui.Text(applied.Count > 0 ? string.Join(", ", applied) : "-");
                    }

                    ImGui.EndTable();
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        ImGui.TreePop();
    }

    private string FormatBorderEffect(BorderEffect border)
    {
        return $"{TrimBorderPrefix(border.Name)} x{border.Multiplier:0.##}{(border.PerConnection ? "/conn" : "")}" +
               $"{(border.AffectsPlacedChart ? " (boosts this tile's chart, value lands where its mods point)" : "")} [{border.Tags}]";
    }

    private static string TrimBorderPrefix(string name)
    {
        return name.StartsWith("DeepwaterBorder", StringComparison.Ordinal)
            ? name["DeepwaterBorder".Length..]
            : name;
    }

    private static string[] BuildAsciiGrid(MapPiecePlacement[,] grid, List<VoyageTileElement> tiles)
    {
        const int H = 5;
        const int W = 7;
        const int GH = H * 3 + 2;
        const int GW = W * 3 + 2;

        var buf = new char[GH, GW];
        for (int y = 0; y < GH; y++)
        for (int x = 0; x < GW; x++)
            buf[y, x] = ' ';

        FillBox(buf, '+', '+', '+', '+', '-', '|', 0, 0, GH - 1, GW - 1);

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var left = c * W + 1;
                var right = left + W - 1;
                var top = r * H + 1;
                var bot = top + H - 1;
                var cx = left + W / 2;
                var cy = top + H / 2;

                var p = grid[2 - r, c];
                var conn = p.Connections;

                for (int y = top; y <= bot; y++)
                for (int x = left; x <= right; x++)
                    buf[y, x] = ' ';

                if (conn.HasFlag(Direction.Up))
                    for (int y = top; y < cy; y++)
                        buf[y, cx] = '|';
                if (conn.HasFlag(Direction.Down))
                    for (int y = cy + 1; y <= bot; y++)
                        buf[y, cx] = '|';
                if (conn.HasFlag(Direction.Left))
                    for (int x = left; x < cx; x++)
                        buf[cy, x] = '-';
                if (conn.HasFlag(Direction.Right))
                    for (int x = cx + 1; x <= right; x++)
                        buf[cy, x] = '-';

                buf[cy, cx] = conn switch
                {
                    Direction.Up | Direction.Down => '|',
                    Direction.Left | Direction.Right => '-',
                    Direction.All => '+',
                    _ => '.',
                };

                var tileIdx = (2 - r) * 3 + c;
                bool matches = false;
                if (tileIdx < tiles.Count)
                {
                    var t = tiles[tileIdx];
                    if (t.ItemContainer?.Address != null)
                    {
                        var placed = t.ItemContainer.Entity.GetComponent<DeepwaterChart>();
                        if (placed != null)
                        {
                            var actualRot = ((Direction)placed.Room.Path).RotateCcw(placed.Rotation);
                            var expectedRot = p.Connections;
                            matches = actualRot == expectedRot;
                        }
                    }
                }

                buf[cy + 1, cx + 2] = matches ? 'O' : 'X';
            }
        }

        var lines = new string[GH];
        for (int y = 0; y < GH; y++)
        {
            var row = new char[GW];
            for (int x = 0; x < GW; x++)
                row[x] = buf[y, x];
            lines[y] = new string(row);
        }

        return lines;
    }

    private static void FillBox(char[,] buf, char tl, char tr, char bl, char br, char h, char v, int y1, int x1, int y2, int x2)
    {
        buf[y1, x1] = tl;
        buf[y1, x2] = tr;
        buf[y2, x1] = bl;
        buf[y2, x2] = br;
        for (int x = x1 + 1; x < x2; x++)
        {
            buf[y1, x] = h;
            buf[y2, x] = h;
        }

        for (int y = y1 + 1; y < y2; y++)
        {
            buf[y, x1] = v;
            buf[y, x2] = v;
        }
    }

    private List<MapPiece> BuildMapPiecesFromAvailableCharts()
    {
        var pieces = new List<MapPiece>();
        var i = 0;
        foreach (var chart in GetAvailableCharts())
        {
            if (chart.Item.TryGetComponent(out DeepwaterChart c))
            {
                var rotation = (Direction)c.Room.Path;
                var chartName = c.Room.Name ?? "";
                pieces.Add(new MapPiece(i,
                    int.PopCount((int)rotation) switch
                    {
                        4 => PieceType.Cross,
                        3 => PieceType.Tee,
                        1 => PieceType.Single,
                        2 => rotation.HasFlag(Direction.Left) == rotation.HasFlag(Direction.Right)
                            ? PieceType.Straight
                            : PieceType.Corner,
                        _ => PieceType.Single
                    }, rotation, [
                        new Modifier("Default", 1), ..chart.Item.GetComponent<Mods>()?.ImplicitMods.Select(im =>
                        {
                            var chartMod = Settings.VoyageSettings.ChartModifiers.Content
                                .FirstOrDefault(cm => cm.Id.Value.Equals(im.RawName, StringComparison.OrdinalIgnoreCase));
                            return new Modifier(im.RawName, chartMod?.Weight.Value ?? 0, chartMod?.IsGlobal.Value ?? false,
                                ModifierTagParser.Parse(chartMod?.Tags.Value, ModifierTag.None), im.Value1);
                        }) ?? []
                    ], chartName));
            }

            i++;
        }

        return pieces;
    }

    private IReadOnlyList<BorderEffect>[,] BuildTileBorders(VoyageWindow tree)
    {
        var modsPerTileIndex = GetTileMods(tree);
        var tileBorders = new IReadOnlyList<BorderEffect>[3, 3];
        for (var tileIndex = 0; tileIndex < 9; tileIndex++)
        {
            var borderMods = modsPerTileIndex.GetValueOrDefault(tileIndex) ?? [];
            tileBorders[tileIndex / 3, tileIndex % 3] = borderMods.Select(m =>
            {
                var setting = FindBorderSetting(m.Id, m.DisplayText);
                return new BorderEffect(
                    m.Id,
                    ModifierTagParser.Parse(setting?.Tags.Value, ModifierTag.All),
                    setting?.ValueMultiplier.Value ?? 1,
                    setting?.PerConnection.Value ?? false,
                    setting?.AffectsPlacedChart.Value ?? false);
            }).ToList();
        }

        return tileBorders;
    }

    private void DrawStrategyStatus()
    {
        var placement = _lastPlacement ?? _voyageSolve?.Placement;
        if (placement == null)
            return;

        var names = DescribeActiveStrategies(placement);
        if (names.Count == 0)
        {
            ImGui.TextColored(Color.Gray.ToImguiVec4(), "Free");
            return;
        }

        foreach (var name in names)
            ImGui.TextColored(StrategyDisplayColor(name).ToImguiVec4(), name);
    }

    private static Color StrategyDisplayColor(string name) =>
        name.Equals("Divine", StringComparison.OrdinalIgnoreCase) ? Color.HotPink : Color.Orange;

    private static List<string> DescribeActiveStrategies(VoyagePlacementRules.Result placement)
    {
        if (placement?.ActiveStrategies is { Count: > 0 } active)
            return active.ToList();
        return [];
    }

    private void DrawStrategyReservationHint()
    {
        var placement = _lastPlacement ?? _voyageSolve?.Placement;
        var savedBits = FormatSavedChartBits(placement);
        if (savedBits.Count == 0)
            return;

        ImGui.TextColored(Color.Yellow.ToImguiVec4(), "Reserving:");
        foreach (var bit in savedBits)
            ImGui.TextColored(Color.Yellow.ToImguiVec4(), $"- {bit}");
    }

    private static List<string> FormatSavedChartBits(VoyagePlacementRules.Result placement)
    {
        var savedBits = new List<string>();
        if (placement == null)
            return savedBits;

        if (placement.SavedKisharaCount > 0)
            savedBits.Add($"{placement.SavedKisharaCount} Kishara");
        if (placement.SavedNoEquipmentCount > 0)
            savedBits.Add($"{placement.SavedNoEquipmentCount} No Equipment");
        if (placement.SavedFracturedCount > 0)
            savedBits.Add($"{placement.SavedFracturedCount} Fractured");
        if (placement.SavedGoldenLanternsCount > 0)
            savedBits.Add($"{placement.SavedGoldenLanternsCount} Golden Lanterns");
        if (placement.SavedPantheonCount > 0)
            savedBits.Add($"{placement.SavedPantheonCount} Pantheon");
        if (placement.SavedSoulEaterCount > 0)
            savedBits.Add($"{placement.SavedSoulEaterCount} Soul Eater");
        if (placement.SavedRareFractureCount > 0)
            savedBits.Add($"{placement.SavedRareFractureCount} Rare Fracture");
        if (placement.SavedRarePossessedCount > 0)
            savedBits.Add($"{placement.SavedRarePossessedCount} Rare Possessed");
        if (placement.SavedPelagicCount > 0)
            savedBits.Add($"{placement.SavedPelagicCount} Pelagic");
        if (placement.SavedFarmCount > 0)
            savedBits.Add($"{placement.SavedFarmCount} Anchorfield");
        if (placement.SavedUniqueBeltCount > 0)
            savedBits.Add($"{placement.SavedUniqueBeltCount} Unique Belt");
        if (placement.SavedUniqueRingCount > 0)
            savedBits.Add($"{placement.SavedUniqueRingCount} Unique Ring");
        if (placement.SavedUniqueAmulet2Count > 0)
            savedBits.Add($"{placement.SavedUniqueAmulet2Count} Unique Amulet2");
        if (placement.SavedUniqueAmulet1Count > 0)
            savedBits.Add($"{placement.SavedUniqueAmulet1Count} Unique Amulet1");
        if (placement.SavedStrongboxCount > 0)
            savedBits.Add($"{placement.SavedStrongboxCount} boxes");
        if (placement.SavedOperativeBoxCount > 0)
            savedBits.Add($"{placement.SavedOperativeBoxCount} Operative");
        if (placement.SavedStarfishCount > 0)
            savedBits.Add($"{placement.SavedStarfishCount} Starfish");
        if (placement.SavedAdjacentRareCount > 0)
            savedBits.Add($"{placement.SavedAdjacentRareCount} adj. rare T2");
        if (placement.SavedRareVoyageCount > 0)
            savedBits.Add($"{placement.SavedRareVoyageCount} voyage rares");
        if (placement.SavedLostMessageCount > 0)
            savedBits.Add($"{placement.SavedLostMessageCount} Lost Message");
        if (placement.SavedShortestPathPremiumCount > 0)
            savedBits.Add($"{placement.SavedShortestPathPremiumCount} path shapes (later voyages)");
        return savedBits;
    }

    private static void LogPlacement(VoyagePlacementRules.Result placement)
    {
        if (placement == null)
            return;

        var savedBits = FormatSavedChartBits(placement);
        if (savedBits.Count > 0)
            DebugWindow.LogMsg($"Voyage: saved {string.Join(", ", savedBits)} for later voyages / better boards", 5);
        if (placement.Locks.Count > 0)
            DebugWindow.LogMsg($"Voyage: {placement.Locks.Count} strategy lock(s), solver fills the rest", 5);
    }

    private static HashSet<int> GetInventorySpecialtyIndices(List<NormalInventoryItem> charts)
    {
        var roomNames = new List<string>(charts.Count);
        var modsPerChart = new List<IReadOnlyList<(string RawName, int Value1)>>(charts.Count);

        foreach (var chart in charts)
        {
            var room = "";
            if (chart?.Entity != null && chart.Entity.TryGetComponent(out DeepwaterChart c))
                room = c.Room.Name ?? "";
            roomNames.Add(room);

            var mods = chart?.Entity?.GetComponent<Mods>()?.ImplicitMods;
            if (mods == null || mods.Count == 0)
            {
                modsPerChart.Add([]);
                continue;
            }

            modsPerChart.Add(mods.Select(m => (m.RawName, m.Value1)).ToList());
        }

        return VoyagePlacementRules.SelectInventorySpecialtyIndices(roomNames, modsPerChart);
    }

    private static string TrimChartPrefix(string name)
    {
        if (name.StartsWith("MapDeepwaterChartVoyage", StringComparison.Ordinal))
            return name["MapDeepwaterChartVoyage".Length..];
        if (name.StartsWith("MapDeepwaterChartAdjacent", StringComparison.Ordinal))
            return name["MapDeepwaterChartAdjacent".Length..];
        return name;
    }
}
