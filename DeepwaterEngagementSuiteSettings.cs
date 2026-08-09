using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Windows.Forms;
using DeepwaterEngagementSuite.VoyagePlannerData;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using GameOffsets.Native;
using ImGuiNET;
using ItemFilterLibrary;
using Newtonsoft.Json;
using SharpDX;

namespace DeepwaterEngagementSuite;

public class DeepwaterEngagementSuiteSettings : ISettings
{
    public const MapIconsIndex DefaultOtherChestIcon = MapIconsIndex.HeistSpottedMiniBoss;
    public const MapIconsIndex DefaultBottledItemChestIcon = MapIconsIndex.QuestItem;
    public const MapIconsIndex DefaultGoldTreasureChestIcon = MapIconsIndex.LootFilterSmallYellowCircle;
    public const MapIconsIndex DefaultClamTreasureChestIcon = MapIconsIndex.LootFilterLargeYellowStar;
    public const MapIconsIndex DefaultCurrencyTreasureChestIcon = MapIconsIndex.RewardCurrency;
    public const MapIconsIndex DefaultCurrencyTreasureChestOpulentIcon = MapIconsIndex.LootFilterLargeYellowStar;
    public const MapIconsIndex DefaultCurrencyGemcuttersChestIcon = MapIconsIndex.RewardChestGems;
    public const MapIconsIndex DefaultUniqueWeaponChestIcon = MapIconsIndex.RewardWeapons;
    public const MapIconsIndex DefaultUniqueArmourChestIcon = MapIconsIndex.RewardArmour;
    public const MapIconsIndex DefaultUniqueJewelleryChestIcon = MapIconsIndex.RewardJewellery;
    public const MapIconsIndex DefaultRareRangedWeaponChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareMeleeWeaponChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareBodyArmourChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareShieldChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareJewelleryChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareHelmetsChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareGlovesChestIcon = DefaultOtherChestIcon;
    public const MapIconsIndex DefaultRareBootsChestIcon = DefaultOtherChestIcon;
    public static readonly Color UniqueItemTint = new Color(175, 96, 37);
    public const MapIconsIndex DefaultScarabChestIcon = MapIconsIndex.RewardScarabs;
    public const MapIconsIndex DefaultStackedDecksChestIcon = MapIconsIndex.RewardDivinationCards;
    public const MapIconsIndex DefaultMapsChestIcon = MapIconsIndex.RewardMaps;
    public const MapIconsIndex DefaultAllflameEmbersChestIcon = MapIconsIndex.SanctumGoldConvert;
    public const MapIconsIndex DefaultCursedDucatDropIcon = MapIconsIndex.RewardPerandus;
    public const MapIconsIndex DefaultIzaroObjectIcon = MapIconsIndex.RewardLabyrinth;
    public const MapIconsIndex DefaultAltarIcon = MapIconsIndex.LootFilterLargeWhiteHexagon;
    public const MapIconsIndex DefaultAltarCrabIcon = DefaultAltarIcon;
    public const MapIconsIndex DefaultAltarOctopusIcon = DefaultAltarIcon;
    public const MapIconsIndex DefaultAltarPufferFishIcon = DefaultAltarIcon;
    public const MapIconsIndex DefaultAltarCoralIcon = DefaultAltarIcon;
    public const MapIconsIndex DefaultAltarFishIcon = DefaultAltarIcon;
    public const MapIconsIndex DefaultAltarUnknownIcon = DefaultAltarIcon;
    public static readonly Color AltarCrabTint = new Color(255, 255, 255);
    public static readonly Color AltarPufferFishTint = new Color(255, 20, 180);
    public static readonly Color AltarOctopusTint = new Color(160, 100, 50);
    public static readonly Color AltarCoralTint = new Color(60, 140, 255);
    public static readonly Color AltarFishTint = new Color(255, 220, 40);
    public static readonly Color UnknownAltarTint = new Color(80, 220, 100);
    public const MapIconsIndex DefaultTormentedSpiritEncounterIcon = MapIconsIndex.LootFilterSmallGreenCircle;
    public const MapIconsIndex DefaultLanternReplenishEncounterIcon = MapIconsIndex.BlightPortalFire;
    public const MapIconsIndex DefaultDeadmansSulphurSmallIcon = MapIconsIndex.LootFilterSmallGreenRaindrop;
    public const MapIconsIndex DefaultDeadmansSulphurBaseIcon = MapIconsIndex.LootFilterSmallGreenRaindrop;
    public const MapIconsIndex DefaultDeadmansSulphurLargeIcon = MapIconsIndex.LootFilterMediumGreenRaindrop;
    public const MapIconsIndex DefaultDeadmansSulphurHugeIcon = MapIconsIndex.LootFilterLargeGreenRaindrop;

    public ToggleNode Enable { get; set; } = new ToggleNode(false);

    [Menu("Icons")]
    public IconSettings IconSettings { get; set; } = new IconSettings();

    [Menu("Loot Window")]
    public LootWindowSettings LootWindowSettings { get; set; } = new LootWindowSettings();

    [Menu("Trails")]
    public TrailSettings TrailSettings { get; set; } = new TrailSettings();

    [Menu("Bubbles")]
    public BubbleSettings BubbleSettings { get; set; } = new BubbleSettings();

    [Menu("Bubble Planner")]
    public PlannerSettings PlannerSettings { get; set; } = new PlannerSettings();

    [Menu("Currency Reminder")]
    public CurrencyReminderSettings CurrencyReminderSettings { get; set; } = new CurrencyReminderSettings();

    [Menu("Voyage")]
    public VoyageSettings VoyageSettings { get; set; } = new VoyageSettings();

    [IgnoreMenu]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public SleepingEntitySettings SleepingEntitySettings
    {
        get => null;
        set => _legacyParseSleepingEntities = value?.Enabled?.Value == true;
    }

    [JsonIgnore]
    private bool _legacyParseSleepingEntities;

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext _)
    {
        if (!_legacyParseSleepingEntities)
            return;

        IconSettings ??= new IconSettings();
        IconSettings.ParseSleepingEntities.Value = true;
    }

    public static MapIconsIndex GetDefaultIcon(IconPickerIndex index) => index switch
    {
        IconPickerIndex.BottledItemChest => DefaultBottledItemChestIcon,
        IconPickerIndex.GoldTreasureChest => DefaultGoldTreasureChestIcon,
        IconPickerIndex.ClamTreasureChest => DefaultClamTreasureChestIcon,
        IconPickerIndex.CurrencyTreasureChest => DefaultCurrencyTreasureChestIcon,
        IconPickerIndex.CurrencyTreasureChestOpulent => DefaultCurrencyTreasureChestOpulentIcon,
        IconPickerIndex.CurrencyGemcuttersChest => DefaultCurrencyGemcuttersChestIcon,
        IconPickerIndex.UniqueWeaponChest => DefaultUniqueWeaponChestIcon,
        IconPickerIndex.UniqueArmourChest => DefaultUniqueArmourChestIcon,
        IconPickerIndex.UniqueJewelleryChest => DefaultUniqueJewelleryChestIcon,
        IconPickerIndex.RareRangedWeaponChest => DefaultRareRangedWeaponChestIcon,
        IconPickerIndex.RareMeleeWeaponChest => DefaultRareMeleeWeaponChestIcon,
        IconPickerIndex.RareBodyArmourChest => DefaultRareBodyArmourChestIcon,
        IconPickerIndex.RareShieldChest => DefaultRareShieldChestIcon,
        IconPickerIndex.RareJewelleryChest => DefaultRareJewelleryChestIcon,
        IconPickerIndex.RareHelmetsChest => DefaultRareHelmetsChestIcon,
        IconPickerIndex.RareGlovesChest => DefaultRareGlovesChestIcon,
        IconPickerIndex.RareBootsChest => DefaultRareBootsChestIcon,
        IconPickerIndex.ScarabChest => DefaultScarabChestIcon,
        IconPickerIndex.StackedDecksChest => DefaultStackedDecksChestIcon,
        IconPickerIndex.MapsChest => DefaultMapsChestIcon,
        IconPickerIndex.AllflameEmbersChest => DefaultAllflameEmbersChestIcon,
        IconPickerIndex.CursedDucatDrop => DefaultCursedDucatDropIcon,
        IconPickerIndex.RandomDucatChest => DefaultCursedDucatDropIcon,
        IconPickerIndex.HazardBoatChest => DefaultCursedDucatDropIcon,
        IconPickerIndex.IzaroObject => DefaultIzaroObjectIcon,
        IconPickerIndex.AltarCrab => DefaultAltarCrabIcon,
        IconPickerIndex.AltarOctopus => DefaultAltarOctopusIcon,
        IconPickerIndex.AltarPufferFish => DefaultAltarPufferFishIcon,
        IconPickerIndex.AltarCoral => DefaultAltarCoralIcon,
        IconPickerIndex.AltarFish => DefaultAltarFishIcon,
        IconPickerIndex.AltarUnknown => DefaultAltarUnknownIcon,
        IconPickerIndex.TormentedSpiritEncounter => DefaultTormentedSpiritEncounterIcon,
        IconPickerIndex.LanternReplenishEncounter => DefaultLanternReplenishEncounterIcon,
        IconPickerIndex.GoldenLanternEncounter => MapIconsIndex.LabyrinthGoldKey,
        IconPickerIndex.InfusedCoralEncounter => DefaultDeadmansSulphurHugeIcon,
        IconPickerIndex.StrongboxDivination => MapIconsIndex.CorpseTypeUndead,
        IconPickerIndex.StrongboxScarab => MapIconsIndex.CorpseTypeEldritch,
        IconPickerIndex.StrongboxArcanist => MapIconsIndex.CorpseTypeBeast,
        IconPickerIndex.PointerTarget => MapIconsIndex.AncestralEnemyTotem,
        IconPickerIndex.DeadMansSulphurSmall => DefaultDeadmansSulphurSmallIcon,
        IconPickerIndex.DeadMansSulphurBase => DefaultDeadmansSulphurBaseIcon,
        IconPickerIndex.DeadMansSulphurLarge => DefaultDeadmansSulphurLargeIcon,
        IconPickerIndex.DeadMansSulphurHuge => DefaultDeadmansSulphurHugeIcon,
        _ => DefaultOtherChestIcon,
    };

    public static Color? GetDefaultTint(IconPickerIndex index) => index switch
    {
        IconPickerIndex.UniqueWeaponChest or IconPickerIndex.UniqueArmourChest or IconPickerIndex.UniqueJewelleryChest => UniqueItemTint,
        IconPickerIndex.AltarCrab => AltarCrabTint,
        IconPickerIndex.AltarPufferFish => AltarPufferFishTint,
        IconPickerIndex.AltarOctopus => AltarOctopusTint,
        IconPickerIndex.AltarCoral => AltarCoralTint,
        IconPickerIndex.AltarFish => AltarFishTint,
        IconPickerIndex.AltarUnknown => UnknownAltarTint,
        IconPickerIndex.PointerTarget => Color.White,
        _ => null,
    };

    public static float GetDefaultIconSizeScale(IconPickerIndex index) => index switch
    {
        IconPickerIndex.CurrencyTreasureChestOpulent => 2.0f,
        IconPickerIndex.InfusedCoralEncounter => 2.5f,
        IconPickerIndex.AltarCrab or IconPickerIndex.AltarOctopus or IconPickerIndex.AltarPufferFish or IconPickerIndex.AltarCoral or IconPickerIndex.AltarFish or IconPickerIndex.AltarUnknown => 3.0f,
        IconPickerIndex.DeadMansSulphurSmall => 0.5f,
        _ => 1f,
    };
}

[Submenu(CollapsedByDefault = true)]
public class LootWindowSettings
{
    [Menu("Show loot window", "Deepwater target summary. Off by default.")]
    public ToggleNode ShowLootWindow { get; set; } = new ToggleNode(false);
}

[Submenu(CollapsedByDefault = true)]
public class IconSettings
{
    public Dictionary<IconPickerIndex, IconDisplaySettings> IconMapping = new();

    [Menu("General", 100, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode GeneralHeader { get; set; }

    [Menu("Parse sleeping entities",
        "Include entities outside the network bubble. Also enable Core → Debug → CollectSleepingEntities.",
        0, 100)]
    public ToggleNode ParseSleepingEntities { get; set; } = new ToggleNode(false);

    [JsonIgnore]
    [Menu(null, 0, 100)]
    public CustomNode CoreSettingWarning { get; set; } = new CustomNode();

    [Menu("World icon size", 0, 100)]
    public RangeNode<int> WorldIconSize { get; set; } = new RangeNode<int>(50, 25, 200);

    [Menu("Map icon size", 0, 100)]
    public RangeNode<int> MapIconSize { get; set; } = new RangeNode<int>(30, 15, 200);

    [Menu("Chart labels", 105, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode ChartLabelsHeader { get; set; }

    [Menu("GhostChart", 0, 105)]
    public ToggleNode ShowGhostChartLabels { get; set; } = new ToggleNode(true);

    [Menu("BrinerotChart", 0, 105)]
    public ToggleNode ShowBrinerotChartLabels { get; set; } = new ToggleNode(true);

    [Menu("InstantChart", 0, 105)]
    public ToggleNode ShowInstantChartLabels { get; set; } = new ToggleNode(true);

    [Menu("Treasure", 110, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode TreasureHeader { get; set; }

    [Menu("Bottled Item", 0, 110)]
    public ToggleNode ShowBottledItemIcons { get; set; } = new ToggleNode(true);

    [ConditionalDisplay(nameof(ShowBottledItemIcons))]
    [Menu("Sound alert: Message in a Bottle", "Play once when first seen in the zone.", 0, 110)]
    public ToggleNode SoundAlertBottledItem { get; set; } = new ToggleNode(false);

    [Menu("Gold Treasure", 0, 110)]
    public ToggleNode ShowGoldTreasureIcons { get; set; } = new ToggleNode(true);

    [Menu("Clam Treasure", 0, 110)]
    public ToggleNode ShowClamTreasureIcons { get; set; } = new ToggleNode(true);

    [Menu("Currency chest", 0, 110)]
    public ToggleNode ShowCurrencyChestIcons { get; set; } = new ToggleNode(true);

    [Menu("Opulent Currency", 0, 110)]
    public ToggleNode ShowOpulentCurrencyIcons { get; set; } = new ToggleNode(true);

    [ConditionalDisplay(nameof(ShowOpulentCurrencyIcons))]
    [Menu("Sound alert: Opulent chests", "Play once when first seen in the zone.", 0, 110)]
    public ToggleNode SoundAlertOpulentCurrency { get; set; } = new ToggleNode(false);

    [Menu("Gemcutter chest", 0, 110)]
    public ToggleNode ShowGemcutterChestIcons { get; set; } = new ToggleNode(true);

    [Menu("Scarab chest", 0, 110)]
    public ToggleNode ShowScarabChestIcons { get; set; } = new ToggleNode(true);

    [Menu("Stacked Decks", 0, 110)]
    public ToggleNode ShowStackedDeckIcons { get; set; } = new ToggleNode(true);

    [Menu("Maps chest", "Off by default.", 0, 110)]
    public ToggleNode ShowMapsChestIcons { get; set; } = new ToggleNode(false);

    [Menu("Allflame Embers", 0, 110)]
    public ToggleNode ShowAllflameEmbersIcons { get; set; } = new ToggleNode(true);

    [Menu("Izaro", 0, 110)]
    public ToggleNode ShowIzaroIcons { get; set; } = new ToggleNode(true);

    [Menu("Uniques", 120, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode UniquesHeader { get; set; }

    [Menu("Unique Weapon", 0, 120)]
    public ToggleNode ShowUniqueWeaponIcons { get; set; } = new ToggleNode(true);

    [Menu("Unique Armour", 0, 120)]
    public ToggleNode ShowUniqueArmourIcons { get; set; } = new ToggleNode(true);

    [Menu("Unique Jewellery", 0, 120)]
    public ToggleNode ShowUniqueJewelleryIcons { get; set; } = new ToggleNode(true);

    [Menu("Rares", 125, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode RaresHeader { get; set; }

    [Menu("Bows", "Rare ranged weapon chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareRangedWeaponIcons { get; set; } = new ToggleNode(true);

    [Menu("Melee", "Rare melee weapon chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareMeleeWeaponIcons { get; set; } = new ToggleNode(true);

    [Menu("Body", "Rare body armour chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareBodyArmourIcons { get; set; } = new ToggleNode(true);

    [Menu("Shields", "Rare shield chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareShieldIcons { get; set; } = new ToggleNode(true);

    [Menu("Trinkets", "Rare jewellery chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareJewelleryIcons { get; set; } = new ToggleNode(true);

    [Menu("Helmets", "Rare helmet chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareHelmetsIcons { get; set; } = new ToggleNode(true);

    [Menu("Gloves", "Rare glove chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareGlovesIcons { get; set; } = new ToggleNode(true);

    [Menu("Boots", "Rare boot chests. Yellow text when enabled.", 0, 125)]
    public ToggleNode ShowRareBootsIcons { get; set; } = new ToggleNode(true);

    [Menu("Altars", 130, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode AltarsHeader { get; set; }

    [Menu("Crab", 0, 130)]
    public ToggleNode ShowAltarCrabIcons { get; set; } = new ToggleNode(true);

    [Menu("Octopus", 0, 130)]
    public ToggleNode ShowAltarOctopusIcons { get; set; } = new ToggleNode(true);

    [Menu("Puffer Fish", 0, 130)]
    public ToggleNode ShowAltarPufferFishIcons { get; set; } = new ToggleNode(true);

    [Menu("Coral", 0, 130)]
    public ToggleNode ShowAltarCoralIcons { get; set; } = new ToggleNode(true);

    [Menu("Fish", 0, 130)]
    public ToggleNode ShowAltarFishIcons { get; set; } = new ToggleNode(true);

    [Menu("Unknown altars", "Any DeepwaterAltar* / DeepwaterSacrificeAltarUpgrade path not listed above.", 0, 130)]
    public ToggleNode ShowAltarUnknownIcons { get; set; } = new ToggleNode(true);

    [Menu("Encounters", 140, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode EncountersHeader { get; set; }

    [Menu("Lantern Replenish", 0, 140)]
    public ToggleNode ShowLanternReplenishIcons { get; set; } = new ToggleNode(true);

    [Menu("Infused Coral", 0, 140)]
    public ToggleNode ShowInfusedCoralIcons { get; set; } = new ToggleNode(true);

    [Menu("Golden Lantern", "Off by default.", 0, 140)]
    public ToggleNode ShowGoldenLanternIcons { get; set; } = new ToggleNode(false);

    [Menu("Tormented Spirit", "Off by default.", 0, 140)]
    public ToggleNode ShowTormentedSpiritIcons { get; set; } = new ToggleNode(false);

    [Menu("Strongboxes", 150, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode StrongboxesHeader { get; set; }

    [Menu("Arcanist", "Off by default.", 0, 150)]
    public ToggleNode ShowArcanistStrongboxIcons { get; set; } = new ToggleNode(false);

    [Menu("Diviner", "Off by default.", 0, 150)]
    public ToggleNode ShowDivinerStrongboxIcons { get; set; } = new ToggleNode(false);

    [Menu("Scarab", "Off by default.", 0, 150)]
    public ToggleNode ShowScarabStrongboxIcons { get; set; } = new ToggleNode(false);

    [Menu("Sulphur", 160, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode SulphurHeader { get; set; }

    [Menu("Base / Large / Huge", "Dead Man's Sulphur chests.", 0, 160)]
    public ToggleNode ShowDeadmansSulphurIcons { get; set; } = new ToggleNode(true);

    [Menu("Small", "Off by default.", 0, 160)]
    public ToggleNode ShowDeadmansSulphurSmallIcons { get; set; } = new ToggleNode(false);

    [Menu("Other", 170, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode OtherIconsHeader { get; set; }

    [Menu("Other chests", 0, 170)]
    public ToggleNode ShowOtherChestIcons { get; set; } = new ToggleNode(true);

    [Menu("Ducats", "Ducat drops/chests/hazard boats. Off by default.", 0, 170)]
    public ToggleNode ShowDucatIcons { get; set; } = new ToggleNode(false);

    public bool IsIconEnabled(IconPickerIndex index) => index switch
    {
        IconPickerIndex.BottledItemChest => ShowBottledItemIcons.Value,
        IconPickerIndex.GoldTreasureChest => ShowGoldTreasureIcons.Value,
        IconPickerIndex.ClamTreasureChest => ShowClamTreasureIcons.Value,
        IconPickerIndex.CurrencyTreasureChest => ShowCurrencyChestIcons.Value,
        IconPickerIndex.CurrencyTreasureChestOpulent => ShowOpulentCurrencyIcons.Value,
        IconPickerIndex.CurrencyGemcuttersChest => ShowGemcutterChestIcons.Value,
        IconPickerIndex.UniqueWeaponChest => ShowUniqueWeaponIcons.Value,
        IconPickerIndex.UniqueArmourChest => ShowUniqueArmourIcons.Value,
        IconPickerIndex.UniqueJewelleryChest => ShowUniqueJewelleryIcons.Value,
        IconPickerIndex.RareRangedWeaponChest => ShowRareRangedWeaponIcons.Value,
        IconPickerIndex.RareMeleeWeaponChest => ShowRareMeleeWeaponIcons.Value,
        IconPickerIndex.RareBodyArmourChest => ShowRareBodyArmourIcons.Value,
        IconPickerIndex.RareShieldChest => ShowRareShieldIcons.Value,
        IconPickerIndex.RareJewelleryChest => ShowRareJewelleryIcons.Value,
        IconPickerIndex.RareHelmetsChest => ShowRareHelmetsIcons.Value,
        IconPickerIndex.RareGlovesChest => ShowRareGlovesIcons.Value,
        IconPickerIndex.RareBootsChest => ShowRareBootsIcons.Value,
        IconPickerIndex.ScarabChest => ShowScarabChestIcons.Value,
        IconPickerIndex.StackedDecksChest => ShowStackedDeckIcons.Value,
        IconPickerIndex.MapsChest => ShowMapsChestIcons.Value,
        IconPickerIndex.AllflameEmbersChest => ShowAllflameEmbersIcons.Value,
        IconPickerIndex.CursedDucatDrop or
            IconPickerIndex.RandomDucatChest or
            IconPickerIndex.HazardBoatChest => ShowDucatIcons.Value,
        IconPickerIndex.IzaroObject => ShowIzaroIcons.Value,
        IconPickerIndex.AltarCrab => ShowAltarCrabIcons.Value,
        IconPickerIndex.AltarOctopus => ShowAltarOctopusIcons.Value,
        IconPickerIndex.AltarPufferFish => ShowAltarPufferFishIcons.Value,
        IconPickerIndex.AltarCoral => ShowAltarCoralIcons.Value,
        IconPickerIndex.AltarFish => ShowAltarFishIcons.Value,
        IconPickerIndex.AltarUnknown => ShowAltarUnknownIcons.Value,
        IconPickerIndex.TormentedSpiritEncounter => ShowTormentedSpiritIcons.Value,
        IconPickerIndex.LanternReplenishEncounter => ShowLanternReplenishIcons.Value,
        IconPickerIndex.GoldenLanternEncounter => ShowGoldenLanternIcons.Value,
        IconPickerIndex.InfusedCoralEncounter => ShowInfusedCoralIcons.Value,
        IconPickerIndex.StrongboxDivination => ShowDivinerStrongboxIcons.Value,
        IconPickerIndex.StrongboxScarab => ShowScarabStrongboxIcons.Value,
        IconPickerIndex.StrongboxArcanist => ShowArcanistStrongboxIcons.Value,
        IconPickerIndex.PointerTarget => !ParseSleepingEntities.Value,
        IconPickerIndex.DeadMansSulphurSmall => ShowDeadmansSulphurSmallIcons.Value,
        IconPickerIndex.DeadMansSulphurBase => ShowDeadmansSulphurIcons.Value,
        IconPickerIndex.DeadMansSulphurLarge => ShowDeadmansSulphurIcons.Value,
        IconPickerIndex.DeadMansSulphurHuge => ShowDeadmansSulphurIcons.Value,
        IconPickerIndex.OtherChests => ShowOtherChestIcons.Value,
        _ => true,
    };
}

[Submenu(CollapsedByDefault = true)]
public class TrailSettings
{
    [Menu("Enable trails")]
    public ToggleNode Enabled { get; set; } = new ToggleNode(false);

    [Menu("Display", 200, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode DisplayHeader { get; set; }

    [Menu("Draw on large map", 0, 200)]
    public ToggleNode DrawOnLargeMap { get; set; } = new ToggleNode(true);

    [Menu("Draw in world", 0, 200)]
    public ToggleNode DrawInWorld { get; set; } = new ToggleNode(false);

    [Menu("Only unreachable targets", 0, 200)]
    public ToggleNode OnlyUnreachable { get; set; } = new ToggleNode(false);

    [Menu("Show labels", 0, 200)]
    public ToggleNode ShowLabels { get; set; } = new ToggleNode(true);

    [Menu("Show undiscovered targets", 0, 200)]
    public ToggleNode ShowUndiscoveredTargets { get; set; } = new ToggleNode(true);

    [Menu("Style", 210, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode StyleHeader { get; set; }

    [Menu("Max distance", 0, 210)]
    public RangeNode<int> MaxDistance { get; set; } = new RangeNode<int>(500, 10, 1000);

    [Menu("Map line width", 0, 210)]
    public RangeNode<int> MapLineWidth { get; set; } = new RangeNode<int>(3, 1, 20);

    [Menu("World line width", 0, 210)]
    public RangeNode<int> WorldLineWidth { get; set; } = new RangeNode<int>(5, 1, 20);

    [Menu("Default map color", 0, 210)]
    public ColorNode DefaultMapColor { get; set; } = new Color(255, 140, 0, 200);

    [Menu("Default world color", 0, 210)]
    public ColorNode DefaultWorldColor { get; set; } = new Color(255, 140, 0, 200);

    [Menu("Undiscovered color", 0, 210)]
    public ColorNode UndiscoveredColor { get; set; } = new Color(255, 255, 255, 220);

    [Menu("Per-target colors")]
    public TrailColorSettings Colors { get; set; } = new TrailColorSettings();
}

public class SleepingEntitySettings
{
    public ToggleNode Enabled { get; set; } = new ToggleNode(false);
}

[Submenu(CollapsedByDefault = true)]
public class TrailColorSettings
{
    public ToggleNode ShowBottledItem { get; set; } = new ToggleNode(true);
    public ColorNode BottledItem { get; set; } = new Color(255, 215, 0, 255);
    public ToggleNode ShowGoldTreasure { get; set; } = new ToggleNode(true);
    public ColorNode GoldTreasure { get; set; } = new Color(255, 215, 0, 255);
    public ToggleNode ShowClamTreasure { get; set; } = new ToggleNode(true);
    public ColorNode ClamTreasure { get; set; } = new Color(255, 255, 100, 255);
    public ToggleNode ShowCurrency { get; set; } = new ToggleNode(true);
    public ColorNode Currency { get; set; } = new Color(255, 255, 255, 255);
    public ToggleNode ShowOpulentCurrency { get; set; } = new ToggleNode(true);
    public ColorNode OpulentCurrency { get; set; } = new Color(255, 170, 0, 255);
    public ToggleNode ShowUniqueWeapon { get; set; } = new ToggleNode(true);
    public ColorNode UniqueWeapon { get; set; } = new Color(175, 96, 37, 255);
    public ToggleNode ShowUniqueArmour { get; set; } = new ToggleNode(true);
    public ColorNode UniqueArmour { get; set; } = new Color(175, 96, 37, 255);
    public ToggleNode ShowUniqueJewellery { get; set; } = new ToggleNode(true);
    public ColorNode UniqueJewellery { get; set; } = new Color(175, 96, 37, 255);
    public ToggleNode ShowRareRangedWeapon { get; set; } = new ToggleNode(true);
    public ColorNode RareRangedWeapon { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareMeleeWeapon { get; set; } = new ToggleNode(true);
    public ColorNode RareMeleeWeapon { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareBodyArmour { get; set; } = new ToggleNode(true);
    public ColorNode RareBodyArmour { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareShield { get; set; } = new ToggleNode(true);
    public ColorNode RareShield { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareJewellery { get; set; } = new ToggleNode(true);
    public ColorNode RareJewellery { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareHelmets { get; set; } = new ToggleNode(true);
    public ColorNode RareHelmets { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareGloves { get; set; } = new ToggleNode(true);
    public ColorNode RareGloves { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowRareBoots { get; set; } = new ToggleNode(true);
    public ColorNode RareBoots { get; set; } = new Color(180, 180, 180, 255);
    public ToggleNode ShowScarabs { get; set; } = new ToggleNode(true);
    public ColorNode Scarabs { get; set; } = new Color(200, 150, 255, 255);
    public ToggleNode ShowStackedDecks { get; set; } = new ToggleNode(true);
    public ColorNode StackedDecks { get; set; } = new Color(100, 200, 255, 255);
    public ToggleNode ShowMaps { get; set; } = new ToggleNode(true);
    public ColorNode Maps { get; set; } = new Color(200, 200, 200, 255);
    public ToggleNode ShowAllflameEmbers { get; set; } = new ToggleNode(true);
    public ColorNode AllflameEmbers { get; set; } = new Color(255, 100, 50, 255);
    public ToggleNode ShowCursedDucat { get; set; } = new ToggleNode(true);
    public ColorNode CursedDucat { get; set; } = new Color(255, 200, 50, 255);
    public ToggleNode ShowRandomDucat { get; set; } = new ToggleNode(true);
    public ColorNode RandomDucat { get; set; } = new Color(255, 200, 50, 255);
    public ToggleNode ShowHazardBoat { get; set; } = new ToggleNode(true);
    public ColorNode HazardBoat { get; set; } = new Color(255, 200, 50, 255);
    public ToggleNode ShowIzaro { get; set; } = new ToggleNode(true);
    public ColorNode Izaro { get; set; } = new Color(255, 255, 0, 255);
    public ToggleNode ShowAltarCrab { get; set; } = new ToggleNode(true);
    public ColorNode AltarCrab { get; set; } = new Color(50, 200, 50, 255);
    public ToggleNode ShowAltarOctopus { get; set; } = new ToggleNode(true);
    public ColorNode AltarOctopus { get; set; } = new Color(160, 100, 50, 255);
    public ToggleNode ShowAltarPufferFish { get; set; } = new ToggleNode(true);
    public ColorNode AltarPufferFish { get; set; } = new Color(255, 20, 180, 255);
    public ToggleNode ShowAltarCoral { get; set; } = new ToggleNode(true);
    public ColorNode AltarCoral { get; set; } = new Color(60, 140, 255, 255);
    public ToggleNode ShowAltarFish { get; set; } = new ToggleNode(true);
    public ColorNode AltarFish { get; set; } = new Color(255, 220, 40, 255);
    public ToggleNode ShowAltarUnknown { get; set; } = new ToggleNode(true);
    public ColorNode AltarUnknown { get; set; } = new Color(80, 220, 100, 255);
    public ToggleNode ShowTormentedSpirit { get; set; } = new ToggleNode(true);
    public ColorNode TormentedSpirit { get; set; } = new Color(0, 255, 100, 255);
    public ToggleNode ShowLanternReplenish { get; set; } = new ToggleNode(true);
    public ColorNode LanternReplenish { get; set; } = new Color(100, 200, 255, 255);
    public ToggleNode ShowGoldenLantern { get; set; } = new ToggleNode(true);
    public ColorNode GoldenLantern { get; set; } = new Color(255, 215, 0, 255);
    public ToggleNode ShowInfusedCoral { get; set; } = new ToggleNode(true);
    public ColorNode InfusedCoral { get; set; } = new Color(255, 90, 180, 255);
    public ToggleNode ShowOther { get; set; } = new ToggleNode(true);
    public ColorNode Other { get; set; } = new Color(180, 180, 180, 255);

    public bool IsEnabled(IconPickerIndex type) => type switch
    {
        IconPickerIndex.BottledItemChest => ShowBottledItem.Value,
        IconPickerIndex.GoldTreasureChest => ShowGoldTreasure.Value,
        IconPickerIndex.ClamTreasureChest => ShowClamTreasure.Value,
        IconPickerIndex.CurrencyTreasureChest => ShowCurrency.Value,
        IconPickerIndex.CurrencyTreasureChestOpulent => ShowOpulentCurrency.Value,
        IconPickerIndex.UniqueWeaponChest => ShowUniqueWeapon.Value,
        IconPickerIndex.UniqueArmourChest => ShowUniqueArmour.Value,
        IconPickerIndex.UniqueJewelleryChest => ShowUniqueJewellery.Value,
        IconPickerIndex.RareRangedWeaponChest => ShowRareRangedWeapon.Value,
        IconPickerIndex.RareMeleeWeaponChest => ShowRareMeleeWeapon.Value,
        IconPickerIndex.RareBodyArmourChest => ShowRareBodyArmour.Value,
        IconPickerIndex.RareShieldChest => ShowRareShield.Value,
        IconPickerIndex.RareJewelleryChest => ShowRareJewellery.Value,
        IconPickerIndex.RareHelmetsChest => ShowRareHelmets.Value,
        IconPickerIndex.RareGlovesChest => ShowRareGloves.Value,
        IconPickerIndex.RareBootsChest => ShowRareBoots.Value,
        IconPickerIndex.ScarabChest => ShowScarabs.Value,
        IconPickerIndex.StackedDecksChest => ShowStackedDecks.Value,
        IconPickerIndex.MapsChest => ShowMaps.Value,
        IconPickerIndex.AllflameEmbersChest => ShowAllflameEmbers.Value,
        IconPickerIndex.CursedDucatDrop => ShowCursedDucat.Value,
        IconPickerIndex.RandomDucatChest => ShowRandomDucat.Value,
        IconPickerIndex.HazardBoatChest => ShowHazardBoat.Value,
        IconPickerIndex.IzaroObject => ShowIzaro.Value,
        IconPickerIndex.AltarCrab => ShowAltarCrab.Value,
        IconPickerIndex.AltarOctopus => ShowAltarOctopus.Value,
        IconPickerIndex.AltarPufferFish => ShowAltarPufferFish.Value,
        IconPickerIndex.AltarCoral => ShowAltarCoral.Value,
        IconPickerIndex.AltarFish => ShowAltarFish.Value,
        IconPickerIndex.AltarUnknown => ShowAltarUnknown.Value,
        IconPickerIndex.TormentedSpiritEncounter => ShowTormentedSpirit.Value,
        IconPickerIndex.LanternReplenishEncounter => ShowLanternReplenish.Value,
        IconPickerIndex.GoldenLanternEncounter => ShowGoldenLantern.Value,
        IconPickerIndex.InfusedCoralEncounter => ShowInfusedCoral.Value,
        IconPickerIndex.OtherChests => ShowOther.Value,
        _ => true,
    };

    public Color Get(IconPickerIndex type, Color fallback) => type switch
    {
        IconPickerIndex.BottledItemChest => BottledItem.Value,
        IconPickerIndex.GoldTreasureChest => GoldTreasure.Value,
        IconPickerIndex.ClamTreasureChest => ClamTreasure.Value,
        IconPickerIndex.CurrencyTreasureChest => Currency.Value,
        IconPickerIndex.CurrencyTreasureChestOpulent => OpulentCurrency.Value,
        IconPickerIndex.UniqueWeaponChest => UniqueWeapon.Value,
        IconPickerIndex.UniqueArmourChest => UniqueArmour.Value,
        IconPickerIndex.UniqueJewelleryChest => UniqueJewellery.Value,
        IconPickerIndex.RareRangedWeaponChest => RareRangedWeapon.Value,
        IconPickerIndex.RareMeleeWeaponChest => RareMeleeWeapon.Value,
        IconPickerIndex.RareBodyArmourChest => RareBodyArmour.Value,
        IconPickerIndex.RareShieldChest => RareShield.Value,
        IconPickerIndex.RareJewelleryChest => RareJewellery.Value,
        IconPickerIndex.RareHelmetsChest => RareHelmets.Value,
        IconPickerIndex.RareGlovesChest => RareGloves.Value,
        IconPickerIndex.RareBootsChest => RareBoots.Value,
        IconPickerIndex.ScarabChest => Scarabs.Value,
        IconPickerIndex.StackedDecksChest => StackedDecks.Value,
        IconPickerIndex.MapsChest => Maps.Value,
        IconPickerIndex.AllflameEmbersChest => AllflameEmbers.Value,
        IconPickerIndex.CursedDucatDrop => CursedDucat.Value,
        IconPickerIndex.RandomDucatChest => RandomDucat.Value,
        IconPickerIndex.HazardBoatChest => HazardBoat.Value,
        IconPickerIndex.IzaroObject => Izaro.Value,
        IconPickerIndex.AltarCrab => AltarCrab.Value,
        IconPickerIndex.AltarOctopus => AltarOctopus.Value,
        IconPickerIndex.AltarPufferFish => AltarPufferFish.Value,
        IconPickerIndex.AltarCoral => AltarCoral.Value,
        IconPickerIndex.AltarFish => AltarFish.Value,
        IconPickerIndex.AltarUnknown => AltarUnknown.Value,
        IconPickerIndex.TormentedSpiritEncounter => TormentedSpirit.Value,
        IconPickerIndex.LanternReplenishEncounter => LanternReplenish.Value,
        IconPickerIndex.GoldenLanternEncounter => GoldenLantern.Value,
        IconPickerIndex.InfusedCoralEncounter => InfusedCoral.Value,
        IconPickerIndex.OtherChests => Other.Value,
        _ => fallback,
    };
}

[Submenu(CollapsedByDefault = true)]
public class CurrencyReminderSettings
{
    [Menu("Enable")]
    public ToggleNode Enabled { get; set; } = new ToggleNode(false);

    [Menu("Required Exalted Orbs")]
    public RangeNode<int> RequiredExaltedOrbs { get; set; } = new RangeNode<int>(20, 0, 20);

    [Menu("Required Alchemy Orbs")]
    public RangeNode<int> RequiredAlchemyOrbs { get; set; } = new RangeNode<int>(20, 0, 20);

    [Menu("Required Chaos Orbs")]
    public RangeNode<int> RequiredChaosOrbs { get; set; } = new RangeNode<int>(20, 0, 20);

    [Menu("Required Scouring Orbs")]
    public RangeNode<int> RequiredScouringOrbs { get; set; } = new RangeNode<int>(20, 0, 20);

    [Menu("Max inventory items")]
    public RangeNode<int> MaxInventoryItems { get; set; } = new RangeNode<int>(30, 0, 60);
}

[Submenu(CollapsedByDefault = true)]
public class PlannerSettings
{
    public Dictionary<IconPickerIndex, ChestSettings> ChestSettingsMap = new()
    {
        [IconPickerIndex.BottledItemChest] = new ChestSettings { Weight = 30 },
        [IconPickerIndex.ClamTreasureChest] = new ChestSettings { Weight = 2 },
        [IconPickerIndex.LanternReplenishEncounter] = new ChestSettings { Weight = 30 },
        [IconPickerIndex.CurrencyTreasureChestOpulent] = new ChestSettings { Weight = 50 },
    };

    [Menu("Controls", 300, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode ControlsHeader { get; set; }

    [Menu("Start search hotkey", 0, 300)]
    public HotkeyNodeV2 StartSearchHotkey { get; set; } = new HotkeyNodeV2(Keys.None);

    [Menu("Stop search hotkey", 0, 300)]
    public HotkeyNodeV2 StopSearchHotkey { get; set; } = new HotkeyNodeV2(Keys.None);

    [Menu("Clear search hotkey", 0, 300)]
    public HotkeyNodeV2 ClearSearchHotkey { get; set; } = new HotkeyNodeV2(Keys.None);

    [Menu("Confirm editor placement hotkey", 0, 300)]
    public HotkeyNodeV2 ConfirmEditorPlacementHotkey { get; set; } = new HotkeyNodeV2(Keys.None);

    [JsonIgnore]
    [ConditionalDisplay(nameof(IsSearchRunning), false)]
    [Menu(null, 0, 300)]
    public ButtonNode StartSearch { get; set; } = new ButtonNode();

    [JsonIgnore]
    [ConditionalDisplay(nameof(IsSearchRunning))]
    [Menu(null, 0, 300)]
    public ButtonNode StopSearch { get; set; } = new ButtonNode();

    [JsonIgnore]
    [ConditionalDisplay(nameof(HasSearchResult))]
    [Menu(null, 0, 300)]
    public ButtonNode ClearSearch { get; set; } = new ButtonNode();

    [Menu("Play sound on finish", 0, 300)]
    public ToggleNode PlaySoundOnFinish { get; set; } = new ToggleNode(false);

    [Menu("Display", 310, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode DisplayHeader { get; set; }

    [Menu("Draw planned bubbles on map", 0, 310)]
    public ToggleNode DrawPlannedBubblesOnMap { get; set; } = new ToggleNode(true);

    [Menu("Draw lines to lanterns in world", 0, 310)]
    public ToggleNode DrawLinesToLanternsInWorld { get; set; } = new ToggleNode(true);

    [Menu("Closest N lanterns", 0, 310)]
    public RangeNode<int> ClosestNLanterns { get; set; } = new RangeNode<int>(2, 0, 10);

    [Menu("Merge planned bubbles", 0, 310)]
    public ToggleNode MergePlannedBubbles { get; set; } = new ToggleNode(true);

    [Menu("Hide plan graphics for placed bubbles",
        "Hide plan graphics once a real bubble is placed on that segment.", 0, 310)]
    public ToggleNode RemoveGraphicsForPlacedBubbles { get; set; } = new ToggleNode(false);

    [Menu("Suggested bubble color", 0, 310)]
    public ColorNode BubbleColor { get; set; } = new ColorNode(Color.Purple);

    [Menu("Map line color", 0, 310)]
    public ColorNode MapLineColor { get; set; } = new ColorNode(Color.Red);

    [Menu("World line color", 0, 310)]
    public ColorNode WorldLineColor { get; set; } = new ColorNode(Color.Orange);

    [Menu("Captured entity color (world)", 0, 310)]
    public ColorNode CapturedEntityWorldFrameColor { get; set; } = new ColorNode(Color.Purple);

    [Menu("Captured entity color (map)", 0, 310)]
    public ColorNode CapturedEntityMapFrameColor { get; set; } = new ColorNode(Color.Purple);

    [Menu("Text marker scale", 0, 310)]
    public RangeNode<float> TextMarkerScale { get; set; } = new RangeNode<float>(2, 0, 5);

    [Menu("Search", 320, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode SearchHeader { get; set; }

    [Menu("Max generation time (seconds)", 0, 320)]
    public RangeNode<float> MaximumGenerationTimeSeconds { get; set; } = new RangeNode<float>(5, 0, 60);

    [Menu("Search threads", 0, 320)]
    public RangeNode<int> SearchThreads { get; set; } = new RangeNode<int>(5, 1, 10);

    [Menu("Random path injection rate", 0, 320)]
    public RangeNode<float> NewRandomPathInjectionRate { get; set; } = new RangeNode<float>(1f, 0, 2);

    [Menu("Path generation size", 0, 320)]
    public RangeNode<int> PathGenerationSize { get; set; } = new RangeNode<int>(100, 1, 1000);

    [Menu("Validated intermediate points", 0, 320)]
    public RangeNode<int> ValidatedIntermediatePoints { get; set; } = new RangeNode<int>(1, 0, 5);

    [Menu("Debug", 330, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode DebugHeader { get; set; }

    [Menu("Show score history", 0, 330)]
    public ToggleNode ShowScoreHistory { get; set; } = new ToggleNode(false);

    [Menu("Keep score history after search ends", 0, 330)]
    public ToggleNode ShowScoreHistoryAfterSearchEnds { get; set; } = new ToggleNode(false);

    internal bool HasSearchResult => SearchState != SearchState.Empty;
    internal bool IsSearchRunning => SearchState == SearchState.Searching;

    internal SearchState SearchState = SearchState.Empty;
}

[Submenu(CollapsedByDefault = true)]
public class BubbleSettings
{
    [Menu("Display", 400, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode DisplayHeader { get; set; }

    [Menu("Show bubbles on map", 0, 400)]
    public ToggleNode ShowBubblesOnMap { get; set; } = new ToggleNode(true);

    [Menu("Show bubbles in world", 0, 400)]
    public ToggleNode ShowBubblesInWorld { get; set; } = new ToggleNode(false);

    [Menu("Mark starting bubble", 0, 400)]
    public ToggleNode MarkStartingBubble { get; set; } = new ToggleNode(true);

    [Menu("Bubble color", 0, 400)]
    public ColorNode BubbleColor { get; set; } = new ColorNode(Color.Red);

    [Menu("Bubble radius override", "0 = use game radius.", 0, 400)]
    public RangeNode<int> BubbleRadiusOverride { get; set; } = new RangeNode<int>(0, 0, 1000);

    [Menu("Merge planned bubble circles", 0, 400)]
    public ToggleNode EnableBubbleRadiusMerging { get; set; } = new ToggleNode(true);

    [Menu("Captured entities", 410, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode CapturedHeader { get; set; }

    [Menu("Hide captured icons in world", 0, 410)]
    public ToggleNode HideCapturedEntitiesInWorld { get; set; } = new ToggleNode(false);

    [Menu("Hide captured icons on map", 0, 410)]
    public ToggleNode HideCapturedEntitiesOnMap { get; set; } = new ToggleNode(false);

    [Menu("Frame thickness (world)", 0, 410)]
    public RangeNode<int> CapturedEntityWorldFrameThickness { get; set; } = new RangeNode<int>(2, 1, 20);

    [Menu("Frame thickness (map)", 0, 410)]
    public RangeNode<int> CapturedEntityMapFrameThickness { get; set; } = new RangeNode<int>(2, 1, 20);
}

[Submenu(CollapsedByDefault = true)]
public class VoyageSettings
{
    public VoyageSettings()
    {
        ClearBorderModifiers = new ButtonNode() { OnPressed = () => { BorderModifiers.Content.Clear(); } };
        ClearChartModifiers = new ButtonNode() { OnPressed = () => { ChartModifiers.Content.Clear(); } };
    }

    [JsonIgnore] [IgnoreMenu] public List<VoyageProfileEntry> Profiles { get; set; } = new();

    [Menu("Enable voyage handling")]
    public ToggleNode EnableVoyageHandling { get; set; } = new ToggleNode(true);

    [Menu("Profiles", 500, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode ProfilesHeader { get; set; }

    [Menu("Active profile", 0, 500)]
    public ListNode ProfileSelector { get; set; } = new ListNode();

    [JsonIgnore]
    [Menu(null, 0, 500)]
    public ButtonNode AddProfile { get; set; } = new ButtonNode();

    [JsonIgnore]
    [Menu(null, 0, 500)]
    public ButtonNode ReloadProfiles { get; set; } = new ButtonNode();

    [JsonIgnore]
    [Menu("Delete current profile (hold shift)", 0, 500)]
    public ButtonNode DeleteCurrentProfile { get; set; } = new ButtonNode();

    [JsonIgnore]
    [Menu(null, 0, 500)]
    public CustomNode ProfileRenameNode { get; set; } = new CustomNode();

    [Menu("Solver & placement", 510, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode SolverHeader { get; set; }

    [Menu("Solver time limit (seconds)", "Stop after this many seconds and keep the best result. 0 = no limit.", 0, 510)]
    public RangeNode<int> SolverTimeLimitSeconds { get; set; } = new RangeNode<int>(5, 1, 120);

    [Menu("Chart placement delay (ms)",
        "Extra wait after each placement click (pick, place, rotate, clear). 0 = current speed.", 0, 510)]
    public RangeNode<int> ChartPlacementDelayMs { get; set; } = new RangeNode<int>(0, 0, 500);

    [Menu("Dump voyage state hotkey", "Write a board JSON snapshot to ConfigDirectory/voyage-dumps.", 0, 510)]
    public HotkeyNodeV2 DumpVoyageStateHotkey { get; set; } = new HotkeyNodeV2(Keys.None);

    [Menu("Overlay", 520, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode OverlayHeader { get; set; }

    [Menu("Show optimizer window", 0, 520)]
    public ToggleNode ShowOptimizerWindow { get; set; } = new ToggleNode(true);

    [Menu("Show score debug details", "Per-tile score breakdown and board coordinates. Off by default.", 0, 520)]
    public ToggleNode ShowScoreDebugDetails { get; set; } = new ToggleNode(false);

    [Menu("Show all border modifiers", "Show every border id on tiles. UI fallback is marked T!…!!.", 0, 520)]
    public ToggleNode ShowAllBorderModifiers { get; set; } = new ToggleNode(false);

    [Menu("Show all chart modifiers", 0, 520)]
    public ToggleNode ShowAllChartModifiers { get; set; } = new ToggleNode(false);

    [Menu("Show chart inventory information", 0, 520)]
    public ToggleNode ShowChartInventoryInformation { get; set; } = new ToggleNode(false);

    [Menu("Ignored charts", "Filter charts out of the voyage inventory.", 530, CollapsedByDefault = true)]
    public ContentNode<VoyageExcludedChartSettings> IgnoredCharts { get; set; } = new ContentNode<VoyageExcludedChartSettings>
    {
        EnableControls = true,
        EnableItemCollapsing = true,
        ItemFactory = () => new VoyageExcludedChartSettings(),
        ItemFilter = (o, s) => o.IFL.Value.Contains(s, StringComparison.OrdinalIgnoreCase),
    };

    [Menu("Scoring", 540, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode ScoringHeader { get; set; }

    [JsonIgnore]
    [Menu("Clear border modifiers", 0, 540)]
    public ButtonNode ClearBorderModifiers { get; set; }

    [Menu("Border modifiers", 0, 540)]
    [JsonIgnore]
    public ContentNode<VoyageBorderModifier> BorderModifiers { get; set; } = new ContentNode<VoyageBorderModifier>
    {
        EnableControls = true,
        EnableItemCollapsing = true,
        ItemFactory = () => new VoyageBorderModifier(),
        ItemFilter = (o, s) => o.Id.Value.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                               o.Abbreviation.Value.Contains(s, StringComparison.OrdinalIgnoreCase),
    };

    [JsonIgnore]
    [Menu("Clear chart modifiers", 0, 540)]
    public ButtonNode ClearChartModifiers { get; set; }

    [Menu("Chart modifiers", 0, 540)]
    [JsonIgnore]
    public ContentNode<VoyageChartModifier> ChartModifiers { get; set; } = new ContentNode<VoyageChartModifier>
    {
        EnableControls = true,
        EnableItemCollapsing = true,
        ItemFactory = () => new VoyageChartModifier(),
        ItemFilter = (o, s) => o.Id.Value.Contains(s, StringComparison.OrdinalIgnoreCase),
    };

    [Menu("Placement strategies", "Toggles and save holds for voyage placement.")]
    public VoyageStrategySettings Strategies { get; set; } = new VoyageStrategySettings();
}

[Submenu(CollapsedByDefault = true)]
public class VoyageStrategySettings
{
    [Menu("Strategies", 600, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode StrategiesHeader { get; set; }

    [Menu("Rare Monsters Drop X",
        "Pelagic on orb tiles; boxes/starfish/rares nearby. Divine border forces this on.", 0, 600)]
    public ToggleNode RareMonstersDrop { get; set; } = new ToggleNode(true);

    [Menu("No-consume farm",
        "Soul Eater → Anchorfield → Clam on strong no-consume tiles. Saves leftover Anchorfield.", 0, 600)]
    public ToggleNode NoConsumeAnchorfield { get; set; } = new ToggleNode(true);

    [Menu("Center specialty",
        "Prefer Operative/Lost Message/Amulet1/Belt/Ring on free center. Amulet2/Belt/Ring stay center-only.", 0, 600)]
    public ToggleNode CenterSpecialty { get; set; } = new ToggleNode(true);

    [Menu("Treasure Anchors", "Highlight strong Treasure Anchor borders only. Default on.", 0, 600)]
    public ToggleNode TreasureAnchors { get; set; } = new ToggleNode(true);

    [Menu("Infinite Lanterns", "Highlight boards with 2+ Infinite Lantern borders. Default off.", 0, 600)]
    public ToggleNode InfiniteLanterns { get; set; } = new ToggleNode(false);

    [Menu("Save holds", 610, CollapsedByDefault = true)]
    [JsonIgnore]
    public EmptyNode SaveHoldsHeader { get; set; }

    [Menu("Save Kishara", "Hold count (0 = place at most one).", 0, 610)]
    public RangeNode<int> SaveKishara { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedKishara, 0, ChartIds.MaxSaveCap);

    [Menu("Save No Equipment", "Hold count (0 = off).", 0, 610)]
    public RangeNode<int> SaveNoEquipment { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedNoEquipment, 0, ChartIds.MaxSaveCap);

    [Menu("Save Fractured", "Hold count (0 = off).", 0, 610)]
    public RangeNode<int> SaveFractured { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedFractured, 0, ChartIds.MaxSaveCap);

    [Menu("Save Golden Lanterns", "Hold count (0 = off). Prefers Tee shapes.", 0, 610)]
    public RangeNode<int> SaveGoldenLanterns { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedGoldenLanterns, 0, ChartIds.MaxSaveCap);

    [Menu("Save Pantheon", "Hold count (0 = off).", 0, 610)]
    public RangeNode<int> SavePantheon { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedPantheon, 0, ChartIds.MaxSaveCap);

    [Menu("Save Soul Eater", "Hold count (0 = off).", 0, 610)]
    public RangeNode<int> SaveSoulEater { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedSoulEater, 0, ChartIds.MaxSaveCap);

    [Menu("Save Rare Fracture", "Hold count (0 = off).", 0, 610)]
    public RangeNode<int> SaveRareFracture { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedRareFracture, 0, ChartIds.MaxSaveCap);

    [Menu("Save Rare Possessed", "Hold count (0 = off).", 0, 610)]
    public RangeNode<int> SaveRarePossessed { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedRarePossessed, 0, ChartIds.MaxSaveCap);

    [Menu("Save Starfish", "Hold count (0 = off). Lowest priority; default 2.", 0, 610)]
    public RangeNode<int> SaveStarfish { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedStarfish, 0, ChartIds.MaxSaveCap);

    [Menu("Save Unique Amulet 2", "Hold count (0 = off). Default 0.", 0, 610)]
    public RangeNode<int> SaveUniqueAmulet2 { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedUniqueAmulet2, 0, ChartIds.MaxSaveCap);

    [Menu("Save Unique Amulet 1", "Hold count (0 = off). Default 0.", 0, 610)]
    public RangeNode<int> SaveUniqueAmulet1 { get; set; } =
        new RangeNode<int>(ChartIds.MaxSavedUniqueAmulet1, 0, ChartIds.MaxSaveCap);

    public VoyageStrategyOptions ToOptions() => new(
        RareMonstersDrop: RareMonstersDrop.Value,
        NoConsumeAnchorfield: NoConsumeAnchorfield.Value,
        CenterSpecialty: CenterSpecialty.Value,
        TreasureAnchors: TreasureAnchors.Value,
        InfiniteLanterns: InfiniteLanterns.Value,
        SaveKishara: SaveKishara.Value,
        SaveNoEquipment: SaveNoEquipment.Value,
        SaveFractured: SaveFractured.Value,
        SaveGoldenLanterns: SaveGoldenLanterns.Value,
        SavePantheon: SavePantheon.Value,
        SaveSoulEater: SaveSoulEater.Value,
        SaveRareFracture: SaveRareFracture.Value,
        SaveRarePossessed: SaveRarePossessed.Value,
        SaveStarfish: SaveStarfish.Value,
        SaveUniqueAmulet2: SaveUniqueAmulet2.Value,
        SaveUniqueAmulet1: SaveUniqueAmulet1.Value);
}

[Submenu(CollapsedByDefault = true)]
public class VoyageExcludedChartSettings
{
    private static readonly ConcurrentDictionary<string, ItemQuery<ChartData>> FilterCache = [];

    public VoyageExcludedChartSettings()
    {
        Status.DrawDelegate = () =>
        {
            if (Query.FailedToCompile)
            {
                ImGui.Text($"Compilation failed: {Query.Error}");
            }
        };
    }

    [JsonIgnore]
    public CustomNode Status { get; set; } = new CustomNode();

    [Menu("IFL")]
    public TextNode IFL { get; set; } = new TextNode("false");
    public ToggleNode Enabled { get; set; } = new ToggleNode(true);

    [IgnoreMenu]
    [JsonIgnore]
    public ItemQuery<ChartData> Query => FilterCache.GetOrAdd(IFL.Value, ItemQuery.Load<ChartData>);

    public override string ToString()
    {
        return $"{(Enabled ? "" : "[Disabled]")}{IFL.Value}###";
    }
}

public class ChartData : ItemData
{
    public Vector2i Pos { get; }

    public ChartData(Entity queriedItem, GameController gc, Vector2i pos)
        : base(queriedItem, gc)
    {
        Pos = pos;
    }

    public ChartData(Entity queriedItem, Entity groundItem, GameController gameController, Vector2i pos)
        : base(queriedItem, groundItem, gameController)
    {
        Pos = pos;
    }
}

public class VoyageProfileEntry
{
    public string Name;
    public VoyageProfile Profile;
}

[Submenu(CollapsedByDefault = true)]
public class VoyageBorderModifier
{
    public TextNode Id { get; set; } = new TextNode("");
    public TextNode Abbreviation { get; set; } = new TextNode("");

    [Menu(null, "Per-connection: effective = 1 + (mult - 1) × connections.")]
    public RangeNode<float> ValueMultiplier { get; set; } = new RangeNode<float>(1, 0, 10);

    [Menu(null, "Comma-separated tags this border boosts. All / None / empty=All.")]
    public TextNode Tags { get; set; } = new TextNode("");

    [Menu("Per connection", "Scale with the chart's connection count on that tile.")]
    public ToggleNode PerConnection { get; set; } = new ToggleNode(false);

    [Menu("Affects placed chart", "Multiply the chart on the tile, not loot landing there.")]
    public ToggleNode AffectsPlacedChart { get; set; } = new ToggleNode(false);

    public ColorNode HighlightColor { get; set; } = Color.Cyan;

    public override string ToString()
    {
        var tags = ModifierTagParser.Parse(Tags.Value, ModifierTag.All);
        return $"{Id.Value} x{ValueMultiplier.Value}{(PerConnection.Value ? "/conn" : "")}{(AffectsPlacedChart.Value ? " [chart]" : "")} ({tags})###";
    }
}

[Submenu(CollapsedByDefault = true)]
public class VoyageChartModifier
{
    public TextNode Id { get; set; } = new TextNode("");
    public TextNode Label { get; set; } = new TextNode("");
    public RangeNode<float> Weight { get; set; } = new RangeNode<float>(0, 0, 100);
    public ToggleNode IsGlobal { get; set; } = new ToggleNode(false);

    [Menu(null, "Comma-separated tags for this chart reward. Empty/None = only All borders boost it.")]
    public TextNode Tags { get; set; } = new TextNode("");

    public ColorNode HighlightColor { get; set; } = Color.Violet;

    public override string ToString()
    {
        var tags = ModifierTagParser.Parse(Tags.Value, ModifierTag.None);
        return $"{Id.Value} {Weight.Value} ({tags})###";
    }
}

public enum SearchState
{
    Empty,
    Searching,
    Stopped,
}
