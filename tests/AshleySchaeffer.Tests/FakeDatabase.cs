using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

// SPT models a prefab path as a type called Path, which collides with System.IO.Path.
using IOPath = System.IO.Path;

namespace AshleySchaeffer.Tests;

/// <summary>
/// The smallest slice of a real SPT database the mod can run against: the vanilla items it clones,
/// the four customisation templates, Ragman with the suit tops are cloned from, and two PMC bot
/// types. Everything the mod does lands in here and can be asserted on afterwards.
/// </summary>
internal sealed class FakeDatabase
{
    internal static readonly MongoId RagmanId = new("5ac3b934156ae10c4430e83c");
    internal static readonly MongoId TopSuitTemplateId = new("5d1f65e586f7744bce0ef714");
    internal static readonly MongoId BottomTemplateId = new("5cc085bb14c02e000e67a5c5");
    internal static readonly MongoId BottomSuiteTemplateId = new("5cd946231388ce000d572fe3");
    internal static readonly MongoId TopTemplateId = new("5d28adcb86f77429242fc893");
    internal static readonly MongoId TopSuiteTemplateId = new("5d1f623e86f7744bce0ef705");

    internal TemplateTable Templates { get; }
    internal LocaleTable Locales { get; }
    internal BotTable Bots { get; }
    internal TradersTable Traders { get; }
    internal GlobalTable Globals { get; }

    /// <summary>The single locale the mod writes into, resolved on demand like the real thing.</summary>
    internal GlobalLocaleDictionary Locale => Locales.Global["en"].Value!;

    internal FakeDatabase(IEnumerable<MongoId> baseItemIds, IEnumerable<MongoId> handsBaseIds)
    {
        var items = new Dictionary<MongoId, TemplateItem>();
        var handbook = new List<HandbookItem>();

        foreach (var id in baseItemIds.Distinct())
        {
            items[id] = new TemplateItem
            {
                Id = id,
                Name = $"base_{id}",
                Parent = new MongoId("5448e53e4bdc2d60728b4567"),
                Properties = new TemplateItemProperties
                {
                    Prefab = new Prefab { Path = $"vanilla/{id}.bundle", Rcid = string.Empty },
                    CanSellOnRagfair = true,
                    CanRequireOnRagfair = true,
                    ConflictingItems = [],
                    Slots = [],
                },
            };

            handbook.Add(new HandbookItem { Id = id, ParentId = new MongoId("5b5f78dc86f77409407a7f8e"), Price = 1000 });
        }

        var customization = new Dictionary<MongoId, CustomizationItem>();
        foreach (var id in new[] { BottomTemplateId, BottomSuiteTemplateId, TopTemplateId, TopSuiteTemplateId }.Concat(handsBaseIds.Distinct()))
        {
            customization[id] = new CustomizationItem
            {
                Id = id,
                Name = $"vanilla_{id}",
                Parent = new MongoId("5cc085a914c02e000c6bea67"),
                Type = "Item",
                Properties = new CustomizationProperties
                {
                    Prefab = $"vanilla/{id}.bundle",
                    Side = ["Usec"],
                    Feet = new MongoId("5cc085bb14c02e000e67a5c5"),
                },
            };
        }

        // One vanilla item that is faction-locked and has a Feet mesh, so unlockOthersFactionsClothing
        // has something to act on.
        var lockedId = new MongoId("5cde95d97d6c8b647a3769b0");
        customization[lockedId] = new CustomizationItem
        {
            Id = lockedId,
            Name = "usec_only_trousers",
            Parent = new MongoId("5cc085a914c02e000c6bea67"),
            Type = "Item",
            Properties = new CustomizationProperties
            {
                Prefab = "vanilla/locked.bundle",
                Side = ["Usec"],
                Feet = new MongoId("5cc085bb14c02e000e67a5c5"),
            },
        };

        Templates = new TemplateTable
        {
            Items = items,
            Handbook = new HandbookBase { Items = handbook, Categories = [] },
            Customization = customization,
            Prices = [],
            Character = [],
            CustomisationStorage = [],
            Prestige = null!,
            Quests = [],
            RepeatableQuests = null!,
            Dialogue = null!,
            Profiles = [],
            Achievements = [],
            CustomAchievements = [],
            DefaultEquipmentPresets = [],
            LocationServices = null!,
        };

        var locale = new GlobalLocaleDictionary();
        Locales = new LocaleTable
        {
            Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>>
            {
                ["en"] = new(() => locale, cacheValue: true),
            },
            Menu = [],
            Languages = [],
        };

        Bots = new BotTable
        {
            Types = new Dictionary<string, BotType?>
            {
                ["usec"] = MakeBot(baseItemIds),
                ["bear"] = MakeBot(baseItemIds),
                ["assault"] = MakeBot(baseItemIds),
            },
            Base = null!,
            Core = null!,
        };

        Traders = [];
        Traders[RagmanId] = new Trader
        {
            Base = new TraderBase { Id = RagmanId, Name = "Ragman", Nickname = "Ragman", Location = "Interchange" },
            Assort = new TraderAssort { Items = [], BarterScheme = [], LoyalLevelItems = [] },
            QuestAssort = [],
            Dialogue = [],
            Suits =
            [
                new Suit
                {
                    Id = TopSuitTemplateId,
                    Tid = RagmanId,
                    SuiteId = new MongoId("5d1f623e86f7744bce0ef705"),
                    IsActive = true,
                },
            ],
        };

        Globals = new GlobalTable
        {
            ItemPresets = [],
            Configuration = null!,
            LocationInfection = [],
            BotPresets = [],
            BotWeaponScatterings = [],
        };
    }

    private static BotType MakeBot(IEnumerable<MongoId> baseItemIds)
    {
        // Give every base item a spawn chance in every slot the mod looks at, so the clone has
        // something to inherit no matter which slot the real item belongs to.
        var equipment = new Dictionary<EquipmentSlots, Dictionary<MongoId, double>>();
        foreach (var slot in new[]
                 {
                     EquipmentSlots.ArmorVest, EquipmentSlots.TacticalVest, EquipmentSlots.Backpack,
                     EquipmentSlots.Headwear, EquipmentSlots.FaceCover,
                 })
        {
            equipment[slot] = baseItemIds.Distinct().ToDictionary(id => id, _ => 5.0);
        }

        return new BotType
        {
            BotInventory = new BotTypeInventory
            {
                Equipment = equipment,
                Mods = [],
                Items = null!,
                Ammo = [],
            },
            BotAppearance = new Appearance
            {
                Body = Enumerable.Range(0, 10).ToDictionary(i => new MongoId(), _ => 1.0),
                Feet = Enumerable.Range(0, 10).ToDictionary(i => new MongoId(), _ => 1.0),
                Hands = [],
                Head = [],
                Voice = [],
            },
            BotDifficulty = null!,
            BotExperience = null!,
            BotHealth = null!,
            BotSkills = null!,
        };
    }
}
