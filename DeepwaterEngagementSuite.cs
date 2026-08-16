using DeepwaterEngagementSuite.PathPlannerData;
using DeepwaterEngagementSuite.VoyagePlannerData;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Models;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Nodes;
using GameOffsets.Native;
using ImGuiNET;
using Newtonsoft.Json;
using SixLabors.PolygonClipper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chest = ExileCore.PoEMemory.Components.Chest;
using Color = SharpDX.Color;
using Direction = DeepwaterEngagementSuite.VoyagePlannerData.Direction;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace DeepwaterEngagementSuite;

public partial class DeepwaterEngagementSuite : BaseSettingsPlugin<DeepwaterEngagementSuiteSettings>
{
    private readonly ConcurrentDictionary<HashSet<(Vector2i, float)>, Polygon> _shapeCache = new(HashSet<(Vector2i,float)>.CreateSetComparer());

    private const string TextureName = "Icons.png";

    private const float GridToWorldMultiplier = 250 / 23f;

    private readonly Dictionary<uint, EntityCacheItem> _cachedEntities = new Dictionary<uint, EntityCacheItem>();
    private readonly HashSet<uint> _soundAlertedEntityIds = new();
    private readonly ConcurrentDictionary<string, ExpeditionEntityType> _entityTypeCache = new();
    private bool _largeMapOpen;
    private Vector2 _playerGridPos;
    private float _bubbleRadius;
    private PathPlannerRunner _plannerRunner;
    private bool _zoneCleared;
    private int[][] _pathfindingData;
    private Vector2i _areaDimensions;
    private List<float> _scoreHistory = [];
    private List<Vector2i> _editedPath;
    private int? _editedIndex = null;
    private PathPlanner.DetailedLootScore _editedPathEval;
    private Polygon _placedBubblePolygon;

    private PathPlanner.DetailedLootScore EditedOrNativeScore => _editedPathEval ?? _plannerRunner?.CurrentBestPath;

    private Camera Camera => GameController.Game.IngameState.Camera;

    private int PlacedLanternCount => Handler.PlacedLanternCount;
    private List<(Vector2i Position, float Radius)> Bubbles => Handler.Bubbles.Select(x=>(x.Position, x.Radius)).ToList();

    private Vector2i? PlacementIndicatorPos => Handler.PlacementIndicator?.GridPosNum.TruncateToVector2I();

    private DeepwaterHandler Handler => GameController.IngameState.ServerData.DeepwaterHandler;
    private bool _initialized;

    public DeepwaterEngagementSuite()
    {
        Order = 10_000;
    }

    public override bool Initialise()
    {
        InitOnce();
        Order = 10_000;
        _profilesDirectory = Path.Combine(ConfigDirectory, "profiles");
        Directory.CreateDirectory(_profilesDirectory);
        EnsureDefaultProfile();
        LoadProfiles();
        Graphics.InitImage(TextureName);
        Settings.PlannerSettings.StartSearch.OnPressed += StartSearch;
        Settings.PlannerSettings.StopSearch.OnPressed += StopSearch;
        Settings.PlannerSettings.ClearSearch.OnPressed += ClearSearch;
        Settings.VoyageSettings.AddProfile.OnPressed += OnAddProfile;
        Settings.VoyageSettings.ReloadProfiles.OnPressed += OnReloadProfiles;
        Settings.VoyageSettings.DeleteCurrentProfile.OnPressed += OnDeleteCurrentProfile;
        Settings.VoyageSettings.ProfileSelector.OnValueSelected += OnProfileSelected;
        Settings.VoyageSettings.ProfileSelector.Values = Settings.VoyageSettings.Profiles.Select(p => p.Name).ToList();
        if (Settings.VoyageSettings.Profiles.Count > 0)
        {
            ApplyProfile(Settings.VoyageSettings.Profiles[0].Name);
        }
        Settings.VoyageSettings.ProfileRenameNode.DrawDelegate = DrawProfileRenameNode;
        if (Settings.IconSettings?.CoreSettingWarning != null)
        {
            Settings.IconSettings.CoreSettingWarning.DrawDelegate = DrawSleepingEntityWarning;
        }
        RegisterHotkey(Settings.PlannerSettings.StartSearchHotkey);
        RegisterHotkey(Settings.PlannerSettings.StopSearchHotkey);
        RegisterHotkey(Settings.PlannerSettings.ClearSearchHotkey);
        RegisterHotkey(Settings.VoyageSettings.DumpVoyageStateHotkey);
        return base.Initialise();
    }

    public override void OnSaveSettings()
    {
        SyncCurrentProfileToMemory();
        SaveProfiles();
    }

    private static void RegisterHotkey(HotkeyNodeV2 hotkey)
    {
        Input.RegisterKey(hotkey.Value);
        hotkey.OnValueChanged += () => { Input.RegisterKey(hotkey.Value); };
    }

    private void StopSearch()
    {
        if (_plannerRunner is { } run)
        {
            run.Stop();
            Settings.PlannerSettings.SearchState = SearchState.Stopped;
        }
        else
        {
            Settings.PlannerSettings.SearchState = SearchState.Empty;
        }
    }

    private void StartSearch()
    {
        _scoreHistory = [];
        _plannerRunner?.Stop();
        var plannerRunner = new PathPlannerRunner();
        plannerRunner.Start(Settings.PlannerSettings, PlannerEnvironment, GameController.SoundController);
        _plannerRunner = plannerRunner;
        Settings.PlannerSettings.SearchState = SearchState.Searching;
    }

    private void ClearSearch()
    {
        if (_plannerRunner is { } run)
        {
            run.Stop();
            _plannerRunner = null;
            _scoreHistory = [];
            _editedPath = null;
            _editedIndex = null;
            _editedPathEval = null;
        }
    }

    public override void AreaChange(AreaInstance area)
    {
        _plannerRunner?.Stop();
        _plannerRunner = null;
        _scoreHistory = [];
        _editedPath = null;
        _editedIndex = null;
        _editedPathEval = null;
        _cachedEntities.Clear();
        _soundAlertedEntityIds.Clear();
        ResetTrailTracking();
        _zoneCleared = false;
        _pathfindingData = GameController.IngameState.Data.RawPathfindingData;
        _areaDimensions = GameController.IngameState.Data.AreaDimensions;
        _shapeCache.Clear();
    }

    private ExpeditionEntityType GetEntityType(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ExpeditionEntityType.None;
        }

        return _entityTypeCache.GetOrAdd(path, p => p switch
        {
            "Metadata/Chests/StrongBoxes/StrongboxDivination" => ExpeditionEntityType.Marker,
            "Metadata/Chests/StrongBoxes/StrongboxScarab" => ExpeditionEntityType.Marker,
            "Metadata/Chests/StrongBoxes/Arcanist" => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Chests/LeagueDeepwater/", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterIzaroObject", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterAltar", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterSacrificeAltarUpgrade", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterTormentedSpiritEncounter", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterCursedDucatDrop", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterLanternReplenishEncounter", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterGoldenLantern", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterBrineCoralEncounter", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            var a when a.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/ResourceChest", StringComparison.Ordinal) => ExpeditionEntityType.Marker,
            _ => ExpeditionEntityType.None,
        });
    }

    private static IconPickerIndex GetChestType(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return IconPickerIndex.OtherChests;
        }

        return path switch
        {
            "Metadata/Chests/StrongBoxes/StrongboxDivination" => IconPickerIndex.StrongboxDivination,
            "Metadata/Chests/StrongBoxes/StrongboxScarab" => IconPickerIndex.StrongboxScarab,
            "Metadata/Chests/StrongBoxes/Arcanist" => IconPickerIndex.StrongboxArcanist,
            var p when p.Contains("BottledItemChest", StringComparison.Ordinal) => IconPickerIndex.BottledItemChest,
            var p when p.Contains("ClamTreasureChest", StringComparison.Ordinal) => IconPickerIndex.ClamTreasureChest,
            var p when p.Contains("CurrencyTreasureChestOpulent", StringComparison.Ordinal) => IconPickerIndex.CurrencyTreasureChestOpulent,
            var p when p.Contains("CurrencyTreasureChest", StringComparison.Ordinal) => IconPickerIndex.CurrencyTreasureChest,
            var p when p.Contains("CurrencyGemcuttersChest", StringComparison.Ordinal) => IconPickerIndex.CurrencyGemcuttersChest,
            var p when p.Contains("DeepwaterAnchorUniqueWeapon", StringComparison.Ordinal) => IconPickerIndex.UniqueWeaponChest,
            var p when p.Contains("DeepwaterAnchorUniqueArmour", StringComparison.Ordinal) => IconPickerIndex.UniqueArmourChest,
            var p when p.Contains("DeepwaterAnchorUniqueJewellery", StringComparison.Ordinal) => IconPickerIndex.UniqueJewelleryChest,
            var p when p.Contains("DeepwaterChestRareRangedWeapon", StringComparison.Ordinal) => IconPickerIndex.RareRangedWeaponChest,
            var p when p.Contains("DeepwaterChestRareMeleeWeapon", StringComparison.Ordinal) => IconPickerIndex.RareMeleeWeaponChest,
            var p when p.Contains("DeepwaterChestRareBodyArmour", StringComparison.Ordinal) => IconPickerIndex.RareBodyArmourChest,
            var p when p.Contains("DeepwaterChestRareShield", StringComparison.Ordinal) => IconPickerIndex.RareShieldChest,
            var p when p.Contains("DeepwaterChestRareJewellery", StringComparison.Ordinal) => IconPickerIndex.RareJewelleryChest,
            var p when p.Contains("DeepwaterChestRareHelmets", StringComparison.Ordinal) => IconPickerIndex.RareHelmetsChest,
            var p when p.Contains("DeepwaterChestRareGloves", StringComparison.Ordinal) => IconPickerIndex.RareGlovesChest,
            var p when p.Contains("DeepwaterChestRareBoots", StringComparison.Ordinal) => IconPickerIndex.RareBootsChest,
            var p when p.Contains("DeepwaterChestScarabs", StringComparison.Ordinal) => IconPickerIndex.ScarabChest,
            var p when p.Contains("DeepwaterChestStackedDecks", StringComparison.Ordinal) => IconPickerIndex.StackedDecksChest,
            var p when p.Contains("DeepwaterChestMaps", StringComparison.Ordinal) => IconPickerIndex.MapsChest,
            var p when p.Contains("DeepwaterChestAllflameEmbers", StringComparison.Ordinal) => IconPickerIndex.AllflameEmbersChest,
            var p when p.Contains("GoldTreasureChest", StringComparison.Ordinal) => IconPickerIndex.GoldTreasureChest,
            var p when p.Contains("DeepwaterCursedDucatDrop", StringComparison.Ordinal) => IconPickerIndex.CursedDucatDrop,
            var p when p.Contains("RandomDucatChest", StringComparison.Ordinal) => IconPickerIndex.RandomDucatChest,
            var p when p.Contains("DeepwaterChestHazardBoat", StringComparison.Ordinal) => IconPickerIndex.HazardBoatChest,
            var p when p.Contains("DeepwaterIzaroObject", StringComparison.Ordinal) => IconPickerIndex.IzaroObject,
            var p when p.Contains("DeepwaterAltarCrab", StringComparison.Ordinal) => IconPickerIndex.AltarCrab,
            var p when p.Contains("DeepwaterAltarOctopus", StringComparison.Ordinal) => IconPickerIndex.AltarOctopus,
            var p when p.Contains("DeepwaterAltarPufferFish", StringComparison.Ordinal) => IconPickerIndex.AltarPufferFish,
            var p when p.Contains("DeepwaterAltarCoral", StringComparison.Ordinal) => IconPickerIndex.AltarCoral,
            var p when p.Contains("DeepwaterAltarFish", StringComparison.Ordinal) => IconPickerIndex.AltarFish,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterAltar", StringComparison.Ordinal) => IconPickerIndex.AltarUnknown,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterSacrificeAltarUpgrade", StringComparison.Ordinal) => IconPickerIndex.AltarUnknown,
            var p when p.Contains("DeepwaterTormentedSpiritEncounter", StringComparison.Ordinal) => IconPickerIndex.TormentedSpiritEncounter,
            var p when p.Contains("DeepwaterLanternReplenishEncounter", StringComparison.Ordinal) => IconPickerIndex.LanternReplenishEncounter,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterGoldenLantern", StringComparison.Ordinal) => IconPickerIndex.GoldenLanternEncounter,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/DeepwaterBrineCoralEncounter", StringComparison.Ordinal) => IconPickerIndex.InfusedCoralEncounter,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/ResourceChestSmall", StringComparison.Ordinal) => IconPickerIndex.DeadMansSulphurSmall,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/ResourceChestBase", StringComparison.Ordinal) => IconPickerIndex.DeadMansSulphurBase,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/ResourceChestLarge", StringComparison.Ordinal) => IconPickerIndex.DeadMansSulphurLarge,
            var p when p.StartsWith("Metadata/Terrain/Leagues/Deepwater/Objects/ResourceChestHuge", StringComparison.Ordinal) => IconPickerIndex.DeadMansSulphurHuge,
            _ => IconPickerIndex.OtherChests,
        };
    }

    private Vector3 ExpandWithTerrainHeight(Vector2 gridPosition)
    {
        return new Vector3(gridPosition.GridToWorld(), GameController.IngameState.Data.GetTerrainHeightAt(gridPosition));
    }

    private void DrawCirclesInWorld(List<Vector3> positions, float radius, Color color)
    {
        const int segments = 90;
        const int segmentSpan = 360 / segments;
        var playerPos = GameController.Player?.GetComponent<Positioned>()?.WorldPosNum;
        if (playerPos == null)
        {
            return;
        }

        foreach (var position in positions
                     .Where(x => playerPos.Value.Distance(new Vector2(x.X, x.Y)) < 80 * GridToWorldMultiplier + radius))
        {
            foreach (var segmentId in Enumerable.Range(0, segments))
            {
                (Vector2, Vector2) GetVector(int i)
                {
                    var (sin, cos) = MathF.SinCos(MathF.PI / 180 * i);
                    var offset = new Vector2(cos, sin) * radius;
                    var xy = position.Xy() + offset;
                    var screen = Camera.WorldToScreen(ExpandWithTerrainHeight(xy.WorldToGrid()));
                    return (xy, screen);
                }

                var segmentOrigin = segmentId * segmentSpan;
                var (w1, c1) = GetVector(segmentOrigin);
                var (w2, c2) = GetVector(segmentOrigin + segmentSpan);
                if (Settings.BubbleSettings.EnableBubbleRadiusMerging)
                {
                    if (positions
                        .Where(x => x != position)
                        .Select(x => new Vector2(x.X, x.Y))
                        .Any(x => Vector2.Distance(w1, x) < radius &&
                                  Vector2.Distance(w2, x) < radius))
                    {
                        continue;
                    }
                }

                Graphics.DrawLine(c1, c2, 1, color);
            }
        }
    }

    public override Job Tick()
    {
        if (Handler == null)
        {
            return null;
        }

        Settings.PlannerSettings.SearchState = _plannerRunner switch
        {
            { IsRunning: true } => SearchState.Searching,
            { IsRunning: false, CurrentBestPath.PerPointScore.Count: > 0 } => SearchState.Stopped,
            _ => SearchState.Empty
        };

        var playerGridPos = GameController.Player?.GetComponent<Positioned>()?.WorldPosNum.WorldToGrid();
        if (playerGridPos == null)
        {
            return null;
        }

        _playerGridPos = playerGridPos.Value;

        var ingameUi = GameController.Game.IngameState.IngameUi;
        var map = ingameUi.Map;
        var largeMap = map.LargeMap.AsObject<SubMap>();
        _largeMapOpen = largeMap.IsVisible;

        _bubbleRadius = Settings.BubbleSettings.BubbleRadiusOverride.Value is > 0 and var o ? o : Bubbles.Min(x => x.Radius);

        DropProvisionalSleepingCacheEntries();

        foreach (var (entity, sleepingOnly) in ExpeditionSourceEntitiesTagged(
                     EntityType.Chest, EntityType.Terrain, EntityType.IngameIcon))
        {
            if (entity == null || string.IsNullOrEmpty(entity.Path))
                continue;

            if (IsChartEncounterPath(entity.Path))
                continue;

            if (GetEntityType(entity.Path) == ExpeditionEntityType.None)
                continue;

            try
            {
                if (IsEntityCompleted(entity, GetChestType(entity.Path)))
                {
                    _cachedEntities.Remove(entity.Id);
                    continue;
                }

                var newValue = BuildCacheItem(entity, sleepingOnly);
                if (newValue == null)
                    continue;

                if (!_cachedEntities.TryGetValue(entity.Id, out var oldValue))
                {
                    _cachedEntities[entity.Id] = newValue;
                    MaybePlayRareChestSoundAlert(entity.Id, GetChestType(entity.Path));
                }
                else
                {
                    _cachedEntities[entity.Id] = oldValue.Merge(newValue);
                }
            }
            catch
            {
            }
        }

        UpdateTrailTracking();
        return null;
    }

    private void MaybePlayRareChestSoundAlert(uint entityId, IconPickerIndex chestType)
    {
        var icons = Settings?.IconSettings;
        if (icons == null || !_soundAlertedEntityIds.Add(entityId))
            return;

        var play = chestType switch
        {
            IconPickerIndex.BottledItemChest => icons.SoundAlertBottledItem?.Value == true,
            IconPickerIndex.CurrencyTreasureChestOpulent => icons.SoundAlertOpulentCurrency?.Value == true,
            _ => false,
        };
        if (!play)
            return;

        try
        {
            GameController?.SoundController?.PlaySound("alert");
        }
        catch
        {
            // Sound playback is best-effort; missing banks should not break entity tracking.
        }
    }

    private ExpeditionEnvironment PlannerEnvironment => BuildEnvironment();

    private ExpeditionEnvironment BuildEnvironment()
    {
        var loot = new List<(Vector2, IExpeditionLoot)>();
        foreach (var e in _cachedEntities.Values)
        {
            if (e.IsOpened)
                continue;

            switch (GetEntityType(e.Path))
            {
                case ExpeditionEntityType.Marker:
                {
                    loot.Add((e.GridPos, new PathPlannerData.Chest(GetChestType(e.Path))));
                    continue;
                }
            }
        }

        return new ExpeditionEnvironment(
            loot.FindAll(x => x.Item2 != null),
            Bubbles.Min(x => x.Radius),
            Handler.MaxLanternCount-Handler.PlacedLanternCount,
            IsValidPlacement,
            Bubbles);
    }

    private bool IsValidPlacement(Vector2 x)
    {
        return x.X >= 0 && x.Y >= 0 &&
               x.X < _areaDimensions.X &&
               x.Y < _areaDimensions.Y &&
               _pathfindingData[(int)x.Y][(int)x.X] > 3;
    }

    private int CountBaseType(BaseItemType bit)
    {
        var inventories = GameController.IngameState.ServerData.PlayerInventories.Where(x => x.TypeId == InventoryNameE.MainInventory1).Select(x => x.Inventory);
        var items = inventories.SelectMany(x => x.Items);
        return items.Where(x => x.TryGetComponent<Base>(out var @base) && bit.Equals(@base?.Info?.BaseItemTypeDat))
            .Select(x => x.TryGetComponent<Stack>(out var stack) ? stack.Size : 0)
            .Sum();
    }

    private void DrawLootWindow()
    {
        if (Handler.MaxLanternCount < 10 &&
            Handler.PlacedLanternCount == Handler.MaxLanternCount)
        {
            return;
        }

        ImGui.SetNextWindowSizeConstraints(new Vector2(500, 0), new Vector2(float.MaxValue, float.MaxValue));
        ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.FirstUseEver);
        var settingsWindowOpen = GameController.Settings.CoreSettings.Enable.Value;
        if (!ImGui.Begin("Deepwater Loot", ImGuiWindowFlags.AlwaysAutoResize |
                                           (settingsWindowOpen ? 0 : ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration)))
        {
            ImGui.End();
            return;
        }

        var maxLanterns = Handler.MaxLanternCount;
        var placedLanterns = Handler.PlacedLanternCount;
        var remainingLanterns = Math.Max(0, maxLanterns - placedLanterns);
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f),
            $"Lanterns: {placedLanterns}/{maxLanterns}  |  Remaining: {remainingLanterns}");
        ImGui.Separator();

        var entries = _cachedEntities.Values
            .Select(x => (
                Type: GetChestType(x.Path),
                Distance: Vector2.Distance(x.GridPos, _playerGridPos),
                Reachable: IsEntityInBubble(x.GridPos),
                SleepingOnly: x.SleepingOnly))
            .Concat(GetUnknownPointerTargets().Select(x => (
                Type: IconPickerIndex.PointerTarget,
                Distance: Vector2.Distance(x, _playerGridPos),
                Reachable: IsEntityInBubble(x),
                SleepingOnly: false)))
            .ToList();

        if (entries.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No targets discovered yet");
            ImGui.End();
            return;
        }

        var grouped = entries
            .GroupBy(x => x.Type)
            .Select(x => (
                Type: x.Key,
                Total: x.Count(),
                Reachable: x.Count(y => y.Reachable),
                NeedsLantern: x.Count(y => !y.Reachable),
                Nearest: x.Min(y => y.Distance),
                SleepingOnly: x.Count(y => y.SleepingOnly)))
            .OrderByDescending(x => x.NeedsLantern)
            .ThenBy(x => x.Nearest)
            .ToList();

        ImGui.Text($"Found: {entries.Count} ({entries.Count(x => x.Reachable)} reachable, {entries.Count(x => !x.Reachable)} need pylon)");

        var sleepingCount = entries.Count(x => x.SleepingOnly);
        if (sleepingCount > 0)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.6f, 1f),
                $"{sleepingCount} out of network bubble (details may be incomplete)");
        }

        ImGui.Separator();

        if (ImGui.BeginTable("DeepwaterLootTable", 4, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Out of\nbubble", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("In\nbubble", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableHeadersRow();

            foreach (var group in grouped)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var allSleeping = group.SleepingOnly > 0 && group.SleepingOnly == group.Total;
                var nameColor = allSleeping
                    ? new Vector4(0.55f, 0.55f, 0.6f, 1f)
                    : group.NeedsLantern > 0
                        ? new Vector4(1f, 0.7f, 0.2f, 1f)
                        : new Vector4(0.3f, 0.9f, 0.3f, 1f);
                var nameText = group.SleepingOnly > 0 && !allSleeping
                    ? $"{GetEntityDisplayName(group.Type)} ({group.SleepingOnly} asleep)"
                    : GetEntityDisplayName(group.Type);

                ImGui.TextColored(nameColor, nameText);

                ImGui.TableNextColumn();
                ImGui.TextColored(
                    group.NeedsLantern > 0
                        ? new Vector4(1f, 0.4f, 0.4f, 1f)
                        : new Vector4(0.3f, 0.3f, 0.3f, 1f),
                    group.NeedsLantern > 0 ? $"{group.NeedsLantern}" : "-");

                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1f),
                    group.Reachable > 0 ? $"{group.Reachable}" : "-");

                ImGui.TableNextColumn();
                ImGui.Text($"{group.Nearest:0}");
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    private IEnumerable<Vector2i> GetUnknownPointerTargets()
    {
        if (!Settings.IconSettings.IsIconEnabled(IconPickerIndex.PointerTarget))
            yield break;

        var knownEntityPositions = _cachedEntities.Values.Where(x => !x.IsOpened).Select(x => x.GridPos).ToList();
        foreach (var e in ExpeditionSourceEntities(EntityType.Terrain)
                     .Where(x => x.Path == "Metadata/Terrain/Leagues/Deepwater/Objects/Pointer"))
        {
            if (!e.TryGetComponent(out Pointer pointer))
                continue;

            foreach (var target in pointer.Targets)
            {
                const float pointerOccupiedGridRadius = 8f;
                if (knownEntityPositions.Any(p => p.DistanceLessThanOrEqual(target, pointerOccupiedGridRadius)))
                    continue;

                yield return target;
            }
        }
    }

    public override void Render()
    {
        DrawChartEncounterLabels();

        DrawVoyageHighlights();
        var largePanelsOpen = GameController.IngameState.IngameUi.FullscreenPanels.Any(x => x.IsVisible) ||
                          GameController.IngameState.IngameUi.LargePanels.Any(x => x.IsVisible);

        if (!largePanelsOpen && Settings.CurrencyReminderSettings.Enabled)
        {
            if (GameController.EntityListWrapper.ValidEntitiesByType[EntityType.IngameIcon]
                    .FirstOrDefault(x => x.Path == "Metadata/Terrain/Doodads/Leagues/Deepwater/ChartPortalLocator") is { } locator)
            {
                var bitToCount = new[]
                {
                    (GameController.Files.BaseItemTypes.Translate("Metadata/Items/Currency/CurrencyUpgradeToRare"), Settings.CurrencyReminderSettings.RequiredAlchemyOrbs),
                    (GameController.Files.BaseItemTypes.Translate("Metadata/Items/Currency/CurrencyRerollRare"), Settings.CurrencyReminderSettings.RequiredChaosOrbs),
                    (GameController.Files.BaseItemTypes.Translate("Metadata/Items/Currency/CurrencyConvertToNormal"), Settings.CurrencyReminderSettings.RequiredScouringOrbs),
                    (GameController.Files.BaseItemTypes.Translate("Metadata/Items/Currency/CurrencyAddModToRare"), Settings.CurrencyReminderSettings.RequiredExaltedOrbs),
                }.Select(x => (x.Item1, x.Item2.Value, CountBaseType(x.Item1)));

                var missing = new List<(string, int)>();
                foreach (var (bit, required, have) in bitToCount)
                {
                    if (have < required)
                    {
                        missing.Add((bit.BaseName, required - have));
                    }
                }

                var entityPos = GameController.IngameState.Data.GetGridScreenPosition(locator.GridPosNum);
                if (missing.Any())
                {
                    var size = Graphics.DrawTextWithBackground($"Don't forget your currency, missing:\n{string.Join("\n", missing.Select(x => $"{x.Item1}: {x.Item2}"))}", entityPos, FontAlign.Center, Color.DarkOrange);
                    entityPos.Y += size.Y;
                }

                if (GameController.IngameState.ServerData.PlayerInventories
                        .Where(x => x.TypeId == InventoryNameE.MainInventory1)
                        .Select(x => x.Inventory)
                        .SelectMany(x => x.Items).Count() is { } count &&
                    count > Settings.CurrencyReminderSettings.MaxInventoryItems)
                {
                    Graphics.DrawTextWithBackground($"You have so many items in your inventory... ({count} > {Settings.CurrencyReminderSettings.MaxInventoryItems.Value})", entityPos, FontAlign.Center, Color.OrangeRed);
                }
            }
        }

        if (Handler == null)
        {
            return;
        }

        if (!largePanelsOpen && Settings.LootWindowSettings.ShowLootWindow)
        {
            DrawLootWindow();
        }

        RenderTrailOverlay(largePanelsOpen);

        if (!largePanelsOpen && (Settings.BubbleSettings.ShowBubblesOnMap ||
                                 Settings.BubbleSettings.ShowBubblesInWorld))
        {
            if (Bubbles is { Count: > 0 } bubbles)
            {
                _placedBubblePolygon = _shapeCache.GetOrAdd(bubbles.ToHashSet(), a => a.Select(x => GetCirclePolygon(x.Item1, x.Item2)).Aggregate(PolygonClipper.Union));
                if (Settings.BubbleSettings.ShowBubblesOnMap)
                {
                    foreach (var cont in _placedBubblePolygon)
                    {
                        var a = cont.Select(v => Graphics.GridToMap(new Vector2((float)v.X, (float)v.Y), _playerGridPos)).ToList();
                        Graphics.DrawPolyLine(a.ToArray(), Settings.BubbleSettings.BubbleColor.Value, 2);
                    }
                }

                if (Settings.BubbleSettings.ShowBubblesInWorld)
                {
                    foreach (var cont in _placedBubblePolygon)
                    {
                        var a = cont.Select(v => Camera.WorldToScreen(GameController.IngameState.Data.ToWorldWithTerrainHeight(new Vector2((float)v.X, (float)v.Y)))).ToList();
                        bool irregular = false;
                        for (int i = 0; i < a.Count; i++)
                        {
                            if (!a[i].DistanceLessThanOrEqual(a[(i + 1) % a.Count], 200))
                            {
                                irregular = true;
                                break;
                            }
                        }

                        if (!irregular)
                        {
                            Graphics.DrawPolyLine(a.ToArray(), Settings.BubbleSettings.BubbleColor.Value, 2);
                        }
                    }
                }
            }
        }

        if (!largePanelsOpen && Settings.BubbleSettings.MarkStartingBubble)
        {
            foreach (var entity in GameController.EntityListWrapper.ValidEntitiesByType[EntityType.IngameIcon]
                         .Where(x => x.Path == "Metadata/Terrain/Leagues/Deepwater/Objects/ExtractionObject"))
            {
                var pos = Graphics.GridToMap(entity.PosNum);
                Graphics.DrawTextWithBackground("Start", pos, Color.Black);
            }
        }

        if (Settings.PlannerSettings.ClearSearchHotkey.PressedOnce())
        {
            ClearSearch();
        }

        if (Settings.PlannerSettings.StopSearchHotkey.PressedOnce())
        {
            StopSearch();
        }

        if (_zoneCleared)
        {
            return;
        }

        if (Settings.PlannerSettings.StartSearchHotkey.PressedOnce())
        {
            StartSearch();
        }

        if (!largePanelsOpen)
        {
            foreach (var e in _cachedEntities.Values)
            {
                if (e.IsOpened)
                    continue;

                switch (GetEntityType(e.Path))
                {
                    case ExpeditionEntityType.Marker:
                    {
                        var chestType = GetChestType(e.Path);
                        var icons = Settings.IconSettings;
                        if (!icons.IsIconEnabled(chestType))
                        {
                            continue;
                        }

                        var mapSettings = icons.IconMapping.GetValueOrDefault(chestType, new IconDisplaySettings());
                        var drawOnMap = mapSettings.ShowOnMap;
                        var drawInWorld = mapSettings.ShowInWorld;

                        if (IsTextOnlyChest(chestType))
                        {
                            var label = GetEntityDisplayName(chestType);
                            var textColor = GetTextOnlyChestColor(chestType);
                            if (drawOnMap && _largeMapOpen)
                            {
                                Graphics.DrawTextWithBackground(
                                    label,
                                    GetEntityPosOnMapScreen(e),
                                    textColor,
                                    FontAlign.Center,
                                    Color.Black);
                            }

                            if (drawInWorld)
                            {
                                Graphics.DrawTextWithBackground(
                                    label,
                                    Camera.WorldToScreen(e.Pos),
                                    textColor,
                                    FontAlign.Center,
                                    Color.Black);
                            }

                            continue;
                        }

                        var icon = mapSettings.Icon ?? DeepwaterEngagementSuiteSettings.GetDefaultIcon(chestType);
                        var tint = mapSettings.Tint ?? DeepwaterEngagementSuiteSettings.GetDefaultTint(chestType);
                        if (e.SleepingOnly)
                        {
                            tint = DimForSleeping(tint);
                        }

                        var sizeScale = mapSettings.SizeScale ?? DeepwaterEngagementSuiteSettings.GetDefaultIconSizeScale(chestType);

                        if (drawOnMap)
                        {
                            DrawIconOnMap(e, icon, tint, Vector2.Zero, sizeScale);
                        }

                        if (drawInWorld)
                        {
                            DrawIconInWorld(e, icon, tint, Vector2.Zero, sizeScale);
                        }

                        continue;
                    }
                }
            }

            if (_largeMapOpen && Settings.IconSettings.IsIconEnabled(IconPickerIndex.PointerTarget))
            {
                foreach (var target in GetUnknownPointerTargets())
                {
                    DrawIcon(
                        MapIconsIndex.AncestralEnemyTotem,
                        Color.White,
                        Graphics.GridToMap(target, target),
                        target.GridToWorld(),
                        hideCaptured: true,
                        plannerCapturedFrameColor: Color.White,
                        frameThickness: 1,
                        iconSize: Settings.IconSettings.MapIconSize.Value);
                }
            }
        }

        if (EditedOrNativeScore is { PerPointScore.Count: > 0 } score)
        {
            var path = score.PerPointScore;
            var placedBubblePositions = Bubbles.Select(x=>x.Position).ToHashSet();
            var usedPath = (Settings.PlannerSettings.RemoveGraphicsForPlacedBubbles
                ? path
                : path.Where(x => !placedBubblePositions.Contains(x.Point))).DistinctBy(x => x.Point).ToDictionary(x => x.Point);
            var usedPathLines = usedPath.OrderBy(x => x.Key.DistanceSqr(_playerGridPos.TruncateToVector2I())).Take(Settings.PlannerSettings.ClosestNLanterns)
                .Select(x => x.Key).ToHashSet();
            for (var i = 0; i < path.Count; i++)
            {
                var point = path[i].Point;
                if (!usedPath.ContainsKey(point))
                {
                    continue;
                }

                var worldPos = GetWorldScreenPosition(point);
                if (Settings.PlannerSettings.DrawLinesToLanternsInWorld && usedPathLines.Contains(point))
                {
                    Graphics.DrawLine(GetWorldScreenPosition(_playerGridPos), worldPos, 1, Settings.PlannerSettings.WorldLineColor);
                }

                var text = $"#{i}";
                using (Graphics.SetTextScale(Settings.PlannerSettings.TextMarkerScale))
                {
                    Graphics.DrawBox(worldPos, worldPos + Graphics.MeasureText(text), Color.Black);
                    Graphics.DrawText(text, worldPos, Settings.PlannerSettings.BubbleColor.Value);
                }
            }

            if (Settings.PlannerSettings.IsSearchRunning)
            {
                _scoreHistory.Add((float)score.TotalScore);
            }

            ShowSearchWindow(score);

            Polygon plannedPoly = null;
            if (Settings.PlannerSettings.MergePlannedBubbles)
            {
                plannedPoly = _shapeCache.GetOrAdd(usedPath.Keys.Select(x => (x, _bubbleRadius)).ToHashSet(),
                    a => a.Select(x => GetCirclePolygon(x.Item1, x.Item2)).Aggregate(PolygonClipper.Union));
                if (_placedBubblePolygon != null)
                    plannedPoly = PolygonClipper.Difference(plannedPoly, _placedBubblePolygon);
              
            }

            if (Settings.PlannerSettings.DrawPlannedBubblesOnMap)
            {
                if (plannedPoly != null)
                {
                    var excludedVertices = (_placedBubblePolygon?.SelectMany(p => p) ?? []).ToHashSet();

                    IEnumerable<List<Vertex>> Segment(Contour cont)
                    {
                        var current = new List<Vertex>();
                        foreach (var v in cont)
                        {
                            if (excludedVertices.Contains(v))
                            {
                                if (current.Any())
                                {
                                    yield return current;
                                    current = [];
                                }
                            }
                            else
                            {
                                current.Add(v);
                            }
                        }

                        if (current.Any())
                        {
                            yield return current;
                        }
                    }

                    foreach (var cont in plannedPoly.SelectMany(Segment))
                    {
                        var a = cont.Select(v => Graphics.GridToMap(new Vector2((float)v.X, (float)v.Y), _playerGridPos)).ToArray();
                        Graphics.DrawPolyLine(a, Settings.PlannerSettings.BubbleColor.Value, 2);
                    }
                }
                else foreach (var point in usedPath)
                {
                    Graphics.DrawCircleOnMap(point.Key, false, _bubbleRadius, Settings.PlannerSettings.BubbleColor.Value, 2, 100);
                }
            }

            if (PlacementIndicatorPos is { } markerPos)
            {
                if (path.Any(x => x.Point.DistanceLessThanOrEqual(markerPos, 0.01f)))
                {
                    var isDuplicate = placedBubblePositions.Any(x => x.DistanceLessThanOrEqual(markerPos, 0.01f));
                    var screenPos = GetWorldScreenPosition(markerPos);
                    var iconSize = 60;
                    var iconCenter = screenPos + new Vector2(0, -iconSize / 2);
                    Graphics.DrawBox(iconCenter - Vector2.One * iconSize / 2, iconCenter + Vector2.One * iconSize / 2, Color.Black);
                    DrawIcon(isDuplicate ? MapIconsIndex.RedFlag : MapIconsIndex.BlueFlag,
                        null, iconCenter, Vector2.Zero, false,
                        Color.Transparent, 0, iconSize);
                }
            }
        }
    }

    public static Polygon GetCirclePolygon(Vector2 center, float radius)
    {
        var vertices = Enumerable.Range(0, 100).Select(v => center + Vector2.UnitX.Rotate(v * 360 / 100.0f) * radius).ToList();
        var p = new Polygon()
        {
            new Contour()
        };
        foreach (var vertex in vertices)
        {
            p.GetLastContour().Add(new Vertex(vertex.X, vertex.Y));
        }

        return p;
    }

    private void ShowSearchWindow(PathPlanner.DetailedLootScore score)
    {
        if (Settings.PlannerSettings.ShowScoreHistory &&
            (Settings.PlannerSettings.IsSearchRunning || Settings.PlannerSettings.ShowScoreHistoryAfterSearchEnds) &&
            ImGui.Begin("Expedition planning result"))
        {
            if (ImGui.TreeNode("Detailed view"))
            {
                PathPlanner.DetailedLootScore scoreDiff = null;
                if (_editedPath != null && _editedIndex is { } editedIndex)
                {
                    var pos = GameController.IngameState.ServerData.WorldMousePositionNum.WorldToGrid().TruncateToVector2I();
                    var pp = new PathPlanner(Settings.PlannerSettings);
                    pp.Init(score.Environment);
                    var path = _editedPath.ToList();
                    path[editedIndex] = pos;
                    scoreDiff = pp.GetDetailedScore(path, score.Environment);
                    DrawCirclesInWorld([ExpandWithTerrainHeight(pos)], _bubbleRadius * GridToWorldMultiplier, Color.LightBlue);
                    Graphics.DrawLine(GetWorldScreenPosition(_editedPath[editedIndex]), GetWorldScreenPosition(pos), 1, Settings.PlannerSettings.WorldLineColor);

                    if (Settings.PlannerSettings.ConfirmEditorPlacementHotkey.UnpressedOnce())
                    {
                        _editedPath[editedIndex] = pos;
                        _editedPathEval = pp.GetDetailedScore(_editedPath, score.Environment);
                        _editedIndex = null;
                    }

                    if (Input.IsKeyDown(Keys.Escape))
                    {
                        _editedIndex = null;
                    }
                }

                if (ImGui.BeginTable("Change per lantern", 7, ImGuiTableFlags.Hideable | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Id");
                    ImGui.TableSetupColumn("Pos");
                    ImGui.TableSetupColumn("Running score");
                    ImGui.TableSetupColumn("Score diff");
                    ImGui.TableSetupColumn("New relic mods");
                    ImGui.TableSetupColumn("New loot");
                    ImGui.TableSetupColumn("Edit");
                    ImGui.TableHeadersRow();

                    var runningScore = 0.0;
                    var runningScoreAfterDiff = 0.0;
                    for (var i = 0; i < score.PerPointScore.Count; i++)
                    {
                        var perPointLootScore = score.PerPointScore[i];
                        var diffOrOld = scoreDiff?.PerPointScore[i] ?? perPointLootScore;
                        ImGui.TableNextRow();
                        ImGui.PushID(i);
                        ImGui.TableNextColumn();
                        ImGui.Text($"{i,2}");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{perPointLootScore.Point}");
                        ImGui.TableNextColumn();
                        runningScore += perPointLootScore.ScoreDiff;
                        if (scoreDiff != null)
                        {
                            runningScoreAfterDiff += scoreDiff.PerPointScore[i].ScoreDiff;
                            ImGui.Text($"{runningScoreAfterDiff,7:F2}");
                            var valueDiff = runningScoreAfterDiff - runningScore;
                            if (valueDiff != 0)
                            {
                                ImGui.SameLine();
                                ImGui.TextColored(GetCompareColor(runningScoreAfterDiff, runningScore), $"{valueDiff:(+0.00);(-0.00);''}");
                            }
                        }
                        else
                        {
                            ImGui.Text($"{runningScore,7:F2}");
                        }

                        ImGui.TableNextColumn();
                        ImGui.Text($"{diffOrOld.ScoreDiff,7:F2}");
                        if (scoreDiff != null)
                        {
                            var valueDiff = scoreDiff.PerPointScore[i].ScoreDiff - perPointLootScore.ScoreDiff;
                            if (valueDiff != 0)
                            {
                                ImGui.SameLine();
                                ImGui.TextColored(
                                    GetCompareColor(scoreDiff.PerPointScore[i].ScoreDiff, perPointLootScore.ScoreDiff),
                                    $"{valueDiff:(+0.00);(-0.00);''}");
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.Text($"{diffOrOld.NewRelics}");
                        if (scoreDiff != null)
                        {
                            var valueDiff = scoreDiff.PerPointScore[i].NewRelics - perPointLootScore.NewRelics;
                            if (valueDiff != 0)
                            {
                                ImGui.SameLine();
                                ImGui.TextColored(
                                    GetCompareColor(scoreDiff.PerPointScore[i].NewRelics, perPointLootScore.NewRelics),
                                    $"{valueDiff:(+0);(-0);''}");
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.Text($"{diffOrOld.Loot}");
                        if (scoreDiff != null)
                        {
                            var valueDiff = scoreDiff.PerPointScore[i].Loot - perPointLootScore.Loot;
                            if (valueDiff != 0)
                            {
                                ImGui.SameLine();
                                ImGui.TextColored(
                                    GetCompareColor(scoreDiff.PerPointScore[i].Loot, perPointLootScore.Loot),
                                    $"{valueDiff:(+0);(-0);''}");
                            }
                        }

                        ImGui.TableNextColumn();
                        if (i == _editedIndex)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Button, Color.Green.ToImguiVec4());
                            if (ImGui.Button("Cancel"))
                            {
                                _editedIndex = null;
                            }

                            ImGui.PopStyleColor();
                        }
                        else if (ImGui.Button(" Edit "))
                        {
                            _editedPath ??= score.PerPointScore.Select(x => x.Point).ToList();
                            var pp = new PathPlanner(Settings.PlannerSettings);
                            pp.Init(score.Environment);
                            _editedPathEval = pp.GetDetailedScore(_editedPath, score.Environment);
                            _editedIndex = i;
                        }

                        ImGui.PopID();
                    }

                    ImGui.EndTable();
                }

                if (_editedPath != null && ImGui.Button("Reset edited path"))
                {
                    _editedIndex = null;
                    _editedPath = null;
                    _editedPathEval = null;
                }
            }

            ImGui.PlotLines("Score over time", ref CollectionsMarshal.AsSpan(_scoreHistory)[0],
                _scoreHistory.Count, 0, "", 0, _scoreHistory.Max(),
                new Vector2(0, ImGui.GetContentRegionAvail().Y));
            ImGui.End();
        }
    }

    private static Vector4 GetCompareColor(double @new, double old)
    {
        return @new.CompareTo(old) switch
        {
            > 0 => Color.Green.ToImguiVec4(), 0 => Color.White.ToImguiVec4(), < 0 => Color.Red.ToImguiVec4()
        };
    }

    private void DrawIconOnMap(EntityCacheItem entity, MapIconsIndex icon, Color? color, Vector2 offset, float sizeScale = 1f)
    {
        if (_largeMapOpen)
        {
            var iconSize = Settings.IconSettings.MapIconSize.Value * sizeScale;
            var halfsize = iconSize / 2.0f;
            var point = GetEntityPosOnMapScreen(entity) + offset * halfsize * 2;
            var entityPos = entity.Pos;
            var entityPos2 = new Vector2(entityPos.X, entityPos.Y);

            DrawIcon(icon, color, point, entityPos2,
                Settings.BubbleSettings.HideCapturedEntitiesOnMap,
                Settings.PlannerSettings.CapturedEntityMapFrameColor,
                Settings.BubbleSettings.CapturedEntityMapFrameThickness,
                iconSize);
        }
    }

    private void DrawIconInWorld(EntityCacheItem entity, MapIconsIndex icon, Color? color, Vector2 offset, float sizeScale = 1f)
    {
        var iconSize = Settings.IconSettings.WorldIconSize.Value * sizeScale;
        var halfsize = iconSize / 2.0f;
        var entityPos = entity.Pos;
        var entityPos2 = new Vector2(entityPos.X, entityPos.Y);
        var point = Camera.WorldToScreen(entityPos) + offset * halfsize * 2;
        DrawIcon(icon, color, point, entityPos2,
            Settings.BubbleSettings.HideCapturedEntitiesInWorld,
            Settings.PlannerSettings.CapturedEntityWorldFrameColor,
            Settings.BubbleSettings.CapturedEntityWorldFrameThickness,
            iconSize);
    }

    private void DrawIcon(
        MapIconsIndex icon,
        Color? color,
        Vector2 displayPosition,
        Vector2 worldPosition,
        bool hideCaptured,
        Color plannerCapturedFrameColor,
        int frameThickness,
        float iconSize)
    {
        var halfsize = iconSize / 2.0f;
        var rect = new SharpDX.RectangleF(displayPosition.X, displayPosition.Y, 0, 0);
        rect.Inflate(halfsize, halfsize);
        var isInBubbleRadius = Bubbles.Any(x => Vector2.Distance(x.Position, worldPosition) < x.Radius);
        var gridPosition = worldPosition.WorldToGrid();
        var isInPlannedBubbleRadius = EditedOrNativeScore is { PerPointScore.Count: > 0 } path &&
                                         path.PerPointScore.Any(x => Vector2.Distance(x.Point, gridPosition) < _bubbleRadius);

        if (isInPlannedBubbleRadius)
        {
            var plannedRect = rect;
            Graphics.DrawFrame(plannedRect, plannerCapturedFrameColor, frameThickness);
        }

        if (!isInBubbleRadius || !hideCaptured)
        {
            Graphics.DrawImage(TextureName, rect, SpriteHelper.GetUV(icon), color ?? Color.White);
        }
    }

    private Vector2 GetWorldScreenPosition(Vector2 gridPos)
    {
        return Camera.WorldToScreen(ExpandWithTerrainHeight(gridPos));
    }

    private Vector2 GetEntityPosOnMapScreen(EntityCacheItem entity)
    {
        return Graphics.GridToMap(entity.GridPos, entity.GridPos);
    }

    private enum ExpeditionEntityType
    {
        None,
        Marker,
    }

    private record EntityCacheItem(
        string Path,
        Lazy<string> BaseAnimatedEntityMetadataCache,
        List<string> Mods,
        Vector3 Pos,
        Vector2 GridPos,
        float? RenderZ,
        float? RenderSize,
        bool? MinimapIconHide,
        bool IsOpened,
        bool SleepingOnly = false)
    {
        public string BaseAnimatedEntityMetadata => BaseAnimatedEntityMetadataCache.Value;

        public EntityCacheItem Merge(EntityCacheItem other)
        {
            return new EntityCacheItem(
                Path ?? other.Path,
                BaseAnimatedEntityMetadata == null ? other.BaseAnimatedEntityMetadataCache : BaseAnimatedEntityMetadataCache,
                Mods ?? other.Mods,
                Pos,
                GridPos,
                RenderZ ?? other.RenderZ,
                RenderSize ?? other.RenderSize,
                MinimapIconHide ?? other.MinimapIconHide,
                IsOpened || other.IsOpened,
                
                
                SleepingOnly && other.SleepingOnly);
        }
    }

    public override void EntityAdded(Entity entity)
    {
        if (entity == null || string.IsNullOrEmpty(entity.Path))
            return;

        if (IsChartEncounterPath(entity.Path))
            return;

        if ((entity.Type is EntityType.Chest or EntityType.Terrain or EntityType.IngameIcon)
            && GetEntityType(entity.Path) != ExpeditionEntityType.None
            && !IsEntityCompleted(entity, GetChestType(entity.Path)))
        {
            var item = BuildCacheItem(entity);
            if (item == null)
                return;

            var isNew = !_cachedEntities.ContainsKey(entity.Id);
            _cachedEntities[entity.Id] = item;
            if (isNew)
            {
                MaybePlayRareChestSoundAlert(entity.Id, GetChestType(entity.Path));
            }

            TrackTrailEntity(entity);
        }
    }

    public override void EntityRemoved(Entity entity)
    {
        if (entity == null)
            return;

        _cachedEntities.Remove(entity.Id);
    }

    private static EntityCacheItem BuildCacheItem(Entity entity, bool sleepingOnly = false)
    {
        if (entity == null || string.IsNullOrEmpty(entity.Path))
            return null;

        try
        {
            float? renderZ = null;
            float? renderSize = null;
            bool? minimapHide = null;
            List<string> mods = null;

            try { mods = entity.GetComponent<ObjectMagicProperties>()?.Mods; } catch { }
            try
            {
                var render = entity.GetComponent<Render>();
                renderZ = render?.Z;
                renderSize = render?.BoundsNum is { } b ? Math.Min(b.X, b.Y) : null;
            }
            catch { }
            try { minimapHide = entity.GetComponent<MinimapIcon>()?.IsHide; } catch { }

            return new EntityCacheItem(
                entity.Path,
                new Lazy<string>(() =>
                {
                    try { return entity.GetComponent<Animated>()?.BaseAnimatedObjectEntity?.Metadata; }
                    catch { return null; }
                }, LazyThreadSafetyMode.None),
                mods,
                entity.PosNum,
                entity.PosNum.WorldToGrid(),
                renderZ,
                renderSize,
                minimapHide,
                IsEntityCompleted(entity, GetChestType(entity.Path)),
                sleepingOnly);
        }
        catch
        {
            return null;
        }
    }

    private bool IsEntityInBubble(Vector2 gridPos)
    {
        var target = gridPos.TruncateToVector2I();
        return Bubbles.Any(x => x.Position.DistanceLessThanOrEqual(target, x.Radius));
    }

    private static bool IsTextOnlyChest(IconPickerIndex type) => type is
        IconPickerIndex.RareRangedWeaponChest or
        IconPickerIndex.RareMeleeWeaponChest or
        IconPickerIndex.RareBodyArmourChest or
        IconPickerIndex.RareShieldChest or
        IconPickerIndex.RareJewelleryChest or
        IconPickerIndex.RareHelmetsChest or
        IconPickerIndex.RareGlovesChest or
        IconPickerIndex.RareBootsChest;

    private static Color GetTextOnlyChestColor(IconPickerIndex type) => Color.Yellow;

    private static string GetEntityDisplayName(IconPickerIndex type) => type switch
    {
        IconPickerIndex.BottledItemChest => "Bottled Item",
        IconPickerIndex.GoldTreasureChest => "Gold Treasure",
        IconPickerIndex.ClamTreasureChest => "Clam Treasure",
        IconPickerIndex.CurrencyTreasureChest => "Currency",
        IconPickerIndex.CurrencyTreasureChestOpulent => "Opulent Currency",
        IconPickerIndex.CurrencyGemcuttersChest => "Gemcutter chest",
        IconPickerIndex.UniqueWeaponChest => "Unique Weapon",
        IconPickerIndex.UniqueArmourChest => "Unique Armour",
        IconPickerIndex.UniqueJewelleryChest => "Unique Jewellery",
        IconPickerIndex.RareRangedWeaponChest => "Bows",
        IconPickerIndex.RareMeleeWeaponChest => "Melee",
        IconPickerIndex.RareBodyArmourChest => "Body",
        IconPickerIndex.RareShieldChest => "Shields",
        IconPickerIndex.RareJewelleryChest => "Trinkets",
        IconPickerIndex.RareHelmetsChest => "Helmets",
        IconPickerIndex.RareGlovesChest => "Gloves",
        IconPickerIndex.RareBootsChest => "Boots",
        IconPickerIndex.ScarabChest => "Scarabs",
        IconPickerIndex.StackedDecksChest => "Stacked Decks",
        IconPickerIndex.MapsChest => "Maps",
        IconPickerIndex.AllflameEmbersChest => "Allflame Embers",
        IconPickerIndex.CursedDucatDrop => "Cursed Ducat",
        IconPickerIndex.RandomDucatChest => "Random Ducat",
        IconPickerIndex.HazardBoatChest => "Hazard Boat",
        IconPickerIndex.IzaroObject => "Izaro",
        IconPickerIndex.AltarCrab => "Altar (Crab)",
        IconPickerIndex.AltarOctopus => "Altar (Octopus)",
        IconPickerIndex.AltarPufferFish => "Altar (Puffer Fish)",
        IconPickerIndex.AltarCoral => "Altar (Coral)",
        IconPickerIndex.AltarFish => "Altar (Fish)",
        IconPickerIndex.AltarUnknown => "Altar (Unknown)",
        IconPickerIndex.TormentedSpiritEncounter => "Tormented Spirit",
        IconPickerIndex.LanternReplenishEncounter => "Lantern Replenish",
        IconPickerIndex.GoldenLanternEncounter => "Golden Lantern",
        IconPickerIndex.InfusedCoralEncounter => "Infused Coral",
        IconPickerIndex.StrongboxDivination => "Card box",
        IconPickerIndex.StrongboxScarab => "Scarab box",
        IconPickerIndex.StrongboxArcanist => "Currency box",
        IconPickerIndex.PointerTarget => "Undiscovered Target",
        _ => "Other",
    };

    private const string GhostChartPath = "Metadata/Chests/LeagueDeepwater/CursedTreasureChestEncounter";
    private const string BrinerotChartPath = "Metadata/Chests/LeagueDeepwater/BrinerotStoresChestEncounter";
    private const string InstantChartPath = "Metadata/Chests/LeagueDeepwater/GiantCoralChest";

    private static bool IsChartEncounterPath(string path) =>
        TryGetChartEncounterLabel(path, out _);

    private static bool TryGetChartEncounterLabel(string path, out string label)
    {
        label = null;
        if (string.IsNullOrEmpty(path))
            return false;

        if (IsExactChestMetadata(path, GhostChartPath))
        {
            label = "GhostChart";
            return true;
        }

        if (IsExactChestMetadata(path, BrinerotChartPath))
        {
            label = "BrinerotChart";
            return true;
        }

        if (IsExactChestMetadata(path, InstantChartPath))
        {
            label = "InstantChart";
            return true;
        }

        return false;
    }

    private static bool IsExactChestMetadata(string path, string chestPath) =>
        path.Equals(chestPath, StringComparison.Ordinal)
        || path.StartsWith(chestPath + "/", StringComparison.Ordinal)
        || path.StartsWith(chestPath + "@", StringComparison.Ordinal);

    private bool IsChartEncounterLabelEnabled(string label) => label switch
    {
        "GhostChart" => Settings.IconSettings.ShowGhostChartLabels.Value,
        "BrinerotChart" => Settings.IconSettings.ShowBrinerotChartLabels.Value,
        "InstantChart" => Settings.IconSettings.ShowInstantChartLabels.Value,
        _ => false,
    };

    private void DrawChartEncounterLabels()
    {
        try
        {
            if (GameController.Area.CurrentArea == null
                || GameController.Area.CurrentArea.IsTown
                || GameController.Area.CurrentArea.IsHideout
                || GameController.IsLoading
                || !GameController.InGame
                || GameController.Game.IngameState.IngameUi.StashElement.IsVisibleLocal
                || !GameController.Game.IngameState.IngameUi.Map.LargeMap.IsVisible)
            {
                return;
            }

            var icons = Settings?.IconSettings;
            if (icons == null)
                return;

            if (!icons.ShowGhostChartLabels.Value
                && !icons.ShowBrinerotChartLabels.Value
                && !icons.ShowInstantChartLabels.Value)
            {
                return;
            }

            var awakeIds = CollectEntityIds(GameController.EntityListWrapper);
            DrawChartEncounterLabelsFrom(GameController.EntityListWrapper, excludeIds: null);

            if (SleepingEntityParsingActive && GameController.SleepingEntityListWrapper != null)
                DrawChartEncounterLabelsFrom(GameController.SleepingEntityListWrapper, excludeIds: awakeIds);
        }
        catch
        {
        }
    }

    private static HashSet<uint> CollectEntityIds(EntityListWrapper wrapper)
    {
        var ids = new HashSet<uint>();
        var list = wrapper?.OnlyValidEntities;
        if (list == null)
            return ids;

        foreach (var entity in list)
        {
            if (entity != null)
                ids.Add(entity.Id);
        }

        return ids;
    }

    private void DrawChartEncounterLabelsFrom(EntityListWrapper wrapper, HashSet<uint> excludeIds)
    {
        if (wrapper?.OnlyValidEntities == null)
            return;

        foreach (var entity in wrapper.OnlyValidEntities)
        {
            if (entity == null || (excludeIds != null && excludeIds.Contains(entity.Id)))
                continue;

            if (!TryGetChartEncounterLabel(entity.Path, out var label) || !IsChartEncounterLabelEnabled(label))
                continue;

            var mapPos = GameController.IngameState.Data.GetGridMapScreenPosition(entity.GridPosNum);
            Graphics.DrawTextWithBackground(label, mapPos, Color.Cyan, FontAlign.Center, Color.Black);
        }
    }

    private static bool IsEntityCompleted(Entity entity, IconPickerIndex type)
    {
        if (entity == null)
            return false;

        try
        {
            if (entity.IsOpened)
                return true;

            if (entity.TryGetComponent(out Chest chest) && chest.IsOpened)
                return true;

            var softCompletedType = type is
                IconPickerIndex.CursedDucatDrop or
                IconPickerIndex.LanternReplenishEncounter or
                IconPickerIndex.InfusedCoralEncounter or
                IconPickerIndex.AltarOctopus or
                IconPickerIndex.AltarCrab or
                IconPickerIndex.AltarPufferFish or
                IconPickerIndex.AltarCoral or
                IconPickerIndex.AltarFish or
                IconPickerIndex.AltarUnknown or
                IconPickerIndex.DeadMansSulphurSmall or
                IconPickerIndex.DeadMansSulphurBase or
                IconPickerIndex.DeadMansSulphurLarge or
                IconPickerIndex.DeadMansSulphurHuge
                ;

            return softCompletedType &&
                   entity.TryGetComponent(out StateMachine stateMachine) &&
                   stateMachine.States.Any(x => (x.Name == "activated" || x.Name == "collected") && x.Value == 1);
        }
        catch
        {
            return false;
        }
    }
}
