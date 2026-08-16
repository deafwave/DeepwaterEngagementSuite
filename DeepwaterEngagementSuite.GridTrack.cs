using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Color = SharpDX.Color;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace DeepwaterEngagementSuite;

public partial class DeepwaterEngagementSuite
{
    // Grid tracker: a top-down view of the playable region, drawn over Radar's terrain texture.
    // In a voyage the region is the 3x3 stitch of charts, so it also records where you have been —
    // the visit order and dwell time per cell — and shades cells by the score the solver expected
    // from them. Debug mode draws it in any normal map, which validates the region detection
    // without spending a voyage.
    private readonly List<Vector2> _pathBreadcrumbs = new();
    private DateTime _lastBreadcrumb = DateTime.MinValue;
    private readonly double[] _cellSeconds = new double[9];
    private readonly int[] _cellFirstOrder = new int[9];
    private int _cellOrderCounter;
    private int _lastCellIndex = -1;
    private DateTime _lastCellSample = DateTime.MinValue;

    private Vector2 _gridOrigin;
    private Vector2 _gridSize;
    private bool _regionComputed;
    private volatile bool _regionComputing;

    /// <summary>Bumped on every area change so a region scan still in flight cannot commit stale bounds.</summary>
    private volatile int _regionGeneration;

    /// <summary>Radar publishes its walkable-terrain texture under this name on the shared Graphics.</summary>
    private const string RadarTextureName = "radar_minimap";

    private const double BreadcrumbIntervalMs = 500;
    private const float BreadcrumbMinDistance = 10f;
    private const float BreadcrumbTeleportDistance = 250f;
    private const int MaxBreadcrumbs = 2000;
    private const int MaxDrawnBreadcrumbs = 300;

    private GridTrackerSettings GridTracker => Settings.GridTrackerSettings;

    /// <summary>Deepwater zones have a handler; debug mode opts every other area in.</summary>
    private bool GridTrackerActive =>
        GridTracker.Enabled && (Handler != null || GridTracker.DebugMode);

    private void GridTrackTick()
    {
        if (!GridTrackerActive)
        {
            return;
        }

        var playerPos = GameController.Player?.GetComponent<Positioned>()?.WorldPosNum.WorldToGrid();
        if (playerPos == null)
        {
            return;
        }

        // The boat and the sea floor are the same instance, so time spent on the boat would
        // otherwise land in whichever cell the boat happens to overlap.
        if (Handler != null && !IsPlayerInField(playerPos.Value))
        {
            _lastCellIndex = -1;
            _lastCellSample = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastBreadcrumb).TotalMilliseconds >= BreadcrumbIntervalMs)
        {
            _lastBreadcrumb = now;
            if (_pathBreadcrumbs.Count == 0 ||
                Vector2.Distance(_pathBreadcrumbs[^1], playerPos.Value) > BreadcrumbMinDistance)
            {
                _pathBreadcrumbs.Add(playerPos.Value);
                if (_pathBreadcrumbs.Count > MaxBreadcrumbs)
                {
                    _pathBreadcrumbs.RemoveAt(0);
                }
            }
        }

        UpdateGridBounds();
        var cell = GridRegion.CellIndex(playerPos.Value, _gridOrigin, _gridSize);
        if (cell < 0)
        {
            return;
        }

        if (_lastCellSample != DateTime.MinValue && _lastCellIndex == cell)
        {
            _cellSeconds[cell] += (now - _lastCellSample).TotalSeconds;
        }

        if (_lastCellIndex != cell && _cellFirstOrder[cell] == 0)
        {
            _cellFirstOrder[cell] = ++_cellOrderCounter;
        }

        _lastCellIndex = cell;
        _lastCellSample = now;
    }

    /// <summary>
    /// "In the field" means inside one of the handler's air bubbles, with slack so the edge of a
    /// bubble does not stutter the tracking. Before the first lantern goes down there are no
    /// bubbles to test against, so tracking stays on.
    /// </summary>
    private bool IsPlayerInField(Vector2 playerGridPos)
    {
        try
        {
            if (Bubbles is { Count: > 0 } bubbles)
            {
                return bubbles.Any(b =>
                    Vector2.Distance(playerGridPos, new Vector2(b.Position.X, b.Position.Y)) <= b.Radius * 1.5f);
            }
        }
        catch
        {
            // no bubble data — do not hide the trail over it
        }

        return true;
    }

    /// <summary>
    /// Computes the playable region once per area, off the render thread. Until it lands, the
    /// whole area stands in so the window has something to draw.
    /// </summary>
    private void UpdateGridBounds()
    {
        if (_regionComputed || _regionComputing)
        {
            return;
        }

        var dims = AreaDimensionsOrDefault();
        if (dims.X <= 0 || dims.Y <= 0)
        {
            return;
        }

        if (_gridSize == default)
        {
            _gridOrigin = default;
            _gridSize = new Vector2(dims.X, dims.Y);
        }

        var pathfinding = _pathfindingData ?? GameController.IngameState.Data.RawPathfindingData;
        if (pathfinding == null)
        {
            return;
        }

        var generation = _regionGeneration;
        _regionComputing = true;
        Task.Run(() =>
        {
            try
            {
                if (GridRegion.TryComputeLargestWalkableBounds(pathfinding, out var origin, out var size) &&
                    generation == _regionGeneration)
                {
                    _gridOrigin = origin;
                    _gridSize = size;
                    _regionComputed = true;
                }
            }
            catch
            {
                // terrain unreadable mid-transition; retried on the next tick
            }
            finally
            {
                _regionComputing = false;
            }
        });
    }

    private GameOffsets.Native.Vector2i AreaDimensionsOrDefault()
    {
        if (_areaDimensions.X > 0 && _areaDimensions.Y > 0)
        {
            return _areaDimensions;
        }

        try
        {
            return GameController.IngameState.Data.AreaDimensions;
        }
        catch
        {
            return default;
        }
    }

    private void GridTrackReset()
    {
        _regionGeneration++;
        _gridOrigin = default;
        _gridSize = default;
        _regionComputed = false;
        _pathBreadcrumbs.Clear();
        Array.Clear(_cellSeconds);
        Array.Clear(_cellFirstOrder);
        _cellOrderCounter = 0;
        _lastCellIndex = -1;
        _lastCellSample = DateTime.MinValue;
        _lastBreadcrumb = DateTime.MinValue;
    }

    private void DrawGridTracker()
    {
        if (!GridTrackerActive)
        {
            return;
        }

        var dims = AreaDimensionsOrDefault();
        var playerPos = GameController.Player?.GetComponent<Positioned>()?.WorldPosNum.WorldToGrid();
        if (dims.X <= 0 || dims.Y <= 0 || playerPos == null)
        {
            return;
        }

        UpdateGridBounds();
        var gridOrigin = _gridOrigin;
        var gridSize = _gridSize;
        if (gridSize.X <= 0 || gridSize.Y <= 0)
        {
            return;
        }

        if (!ImGui.Begin("Grid Tracker", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        var canvasW = (float)GridTracker.WindowSize.Value;
        var canvasH = canvasW * gridSize.Y / gridSize.X;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        // World Y grows north, so the canvas flips it: high grid Y draws at the top.
        Vector2 ToCanvas(Vector2 grid) =>
            origin + new Vector2(
                (grid.X - gridOrigin.X) / gridSize.X * canvasW,
                (1f - (grid.Y - gridOrigin.Y) / gridSize.Y) * canvasH);

        var gridCol = ImGui.ColorConvertFloat4ToU32(GridTracker.GridColor.Value.ToImguiVec4());
        var pathCol = ImGui.ColorConvertFloat4ToU32(GridTracker.PathColor.Value.ToImguiVec4());
        var markerCol = ImGui.ColorConvertFloat4ToU32(Color.Gold.ToImguiVec4());
        var playerCol = ImGui.ColorConvertFloat4ToU32(Color.White.ToImguiVec4());
        var bgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.45f));

        drawList.AddRectFilled(origin, origin + new Vector2(canvasW, canvasH), bgCol);

        // Radar's terrain texture covers the whole area, so crop it to the region by UV. V is
        // inverted to match the canvas flip.
        try
        {
            if (Graphics.HasImage(RadarTextureName))
            {
                var u0 = gridOrigin.X / dims.X;
                var u1 = (gridOrigin.X + gridSize.X) / dims.X;
                var vTop = (gridOrigin.Y + gridSize.Y) / dims.Y;
                var vBottom = gridOrigin.Y / dims.Y;
                drawList.AddImage(Graphics.GetTextureId(RadarTextureName),
                    origin, origin + new Vector2(canvasW, canvasH),
                    new Vector2(u0, vTop), new Vector2(u1, vBottom));
            }
        }
        catch
        {
            // Radar not loaded, or it has not generated a texture for this area yet
        }

        // The 3x3 grid only means something for a stitched nine-chart voyage. Debug mode forces it
        // in normal maps; a solo chart is one room, so it gets a plain frame instead.
        var showCells = Handler == null && GridTracker.DebugMode.Value;
        try
        {
            showCells |= (Handler?.MaxLanternCount ?? 0) > 7;
        }
        catch
        {
            // handler unreadable
        }

        if (showCells)
        {
            DrawGridCells(drawList, origin, canvasW, canvasH, gridCol);
        }
        else
        {
            drawList.AddRect(origin, origin + new Vector2(canvasW, canvasH), gridCol);
        }

        // Known markers still worth walking to.
        foreach (var cached in _cachedEntities.Values)
        {
            if (cached.IsOpened)
            {
                continue;
            }

            drawList.AddCircleFilled(ToCanvas(cached.GridPos), 2.5f, markerCol);
        }

        // Where you have walked. Large jumps are portals, not steps, so they draw no line.
        var step = Math.Max(1, _pathBreadcrumbs.Count / MaxDrawnBreadcrumbs);
        for (var i = step; i < _pathBreadcrumbs.Count; i += step)
        {
            if (Vector2.Distance(_pathBreadcrumbs[i - step], _pathBreadcrumbs[i]) > BreadcrumbTeleportDistance)
            {
                continue;
            }

            drawList.AddLine(ToCanvas(_pathBreadcrumbs[i - step]), ToCanvas(_pathBreadcrumbs[i]), pathCol, 1.5f);
        }

        drawList.AddCircleFilled(ToCanvas(playerPos.Value), 4f, playerCol);

        ImGui.Dummy(new Vector2(canvasW, canvasH));
        ImGui.End();
    }

    private void DrawGridCells(ImDrawListPtr drawList, Vector2 origin, float canvasW, float canvasH, uint gridCol)
    {
        // Board rows count from the south, the canvas from the north. The snapshot outlives the
        // voyage that produced it, so heat is confined to deepwater — in a debug-mode normal map
        // it would be shading cells with scores from an unrelated run.
        var cellScores = GridTracker.ShowPlanHeat && Handler != null ? _plannedCellScores : null;
        var maxScore = 0d;
        if (cellScores != null)
        {
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
            {
                maxScore = Math.Max(maxScore, cellScores[r, c]);
            }
        }

        for (var rowFromTop = 0; rowFromTop < 3; rowFromTop++)
        {
            for (var c = 0; c < 3; c++)
            {
                var boardRow = 2 - rowFromTop;
                var cellMin = origin + new Vector2(canvasW * c / 3f, canvasH * rowFromTop / 3f);
                var cellMax = origin + new Vector2(canvasW * (c + 1) / 3f, canvasH * (rowFromTop + 1) / 3f);

                if (maxScore > 0)
                {
                    var alpha = (float)(0.08 + 0.30 * Math.Max(0, cellScores[boardRow, c]) / maxScore);
                    var heatCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.9f, 0.2f, alpha));
                    drawList.AddRectFilled(cellMin, cellMax, heatCol);
                }

                var idx = boardRow * 3 + c;
                var label = $"({boardRow},{c})";
                if (_cellFirstOrder[idx] > 0)
                {
                    label += $" #{_cellFirstOrder[idx]} {(int)(_cellSeconds[idx] / 60)}:{(int)(_cellSeconds[idx] % 60):D2}";
                }

                drawList.AddText(cellMin + new Vector2(3, 3), gridCol, label);
            }
        }

        for (var i = 0; i <= 3; i++)
        {
            var x = canvasW * i / 3f;
            var y = canvasH * i / 3f;
            drawList.AddLine(origin + new Vector2(x, 0), origin + new Vector2(x, canvasH), gridCol, 1f);
            drawList.AddLine(origin + new Vector2(0, y), origin + new Vector2(canvasW, y), gridCol, 1f);
        }
    }
}
