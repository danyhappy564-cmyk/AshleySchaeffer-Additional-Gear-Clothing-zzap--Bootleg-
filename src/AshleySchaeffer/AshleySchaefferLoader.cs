using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;

// SPT models a prefab path as a type called Path, which collides with System.IO.Path.
using IOPath = System.IO.Path;

namespace AshleySchaeffer;

/// <summary>
/// Adds Ashley Schaeffer's gear and clothing to the database, and optionally the trader who sells it.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the 4.0 build. The 4.0 original ran at <c>OnLoadOrder.PostDBModLoader + 1</c>, which
/// was "as soon as the database exists". 4.1 renamed that stage and, more importantly, added a hard
/// cutoff: <c>DatabaseIntegrityService</c> snapshots <c>Templates.Items</c> when profiles load and
/// throws <c>DatabaseModifiedAfterCutoffException</c> for anything added afterwards. This mod clones
/// 27 gear items into that table, so it has to run at <see cref="OnLoadOrder.Preload"/> - which is
/// also where the server's own error message points. The trailing <c>+ 1</c> is the original's.
/// </para>
/// <para>
/// The whole database, traders included, is already loaded by the time Preload runs, so reading
/// Ragman's suit list from here is safe.
/// </para>
/// </remarks>
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class AshleySchaefferLoader(
    ModHelper modHelper,
    ISptLogger<AshleySchaefferLoader> logger,
    ImageRouter imageRouter,
    TimeUtil timeUtil,
    ICloner cloner,
    TraderConfig traderConfig,
    RagfairConfig ragfairConfig,
    WeatherConfig weatherConfig,
    TemplateTable templates,
    LocaleTable localeTable,
    BotTable botTable,
    TradersTable traders,
    GlobalTable globals
) : IOnLoad
{
    /// <summary>Ragman. Owns every vanilla suit, and the fallback seller when the trader is off.</summary>
    private static readonly MongoId RagmanId = new("5ac3b934156ae10c4430e83c");

    /// <summary>Roubles - what suits are priced in.</summary>
    private static readonly MongoId RoublesId = new("5449016a4bdc2d6f028b456f");

    /// <summary>Vanilla customisation entries the new ones are cloned from.</summary>
    private static readonly MongoId BottomTemplateId = new("5cc085bb14c02e000e67a5c5");
    private static readonly MongoId BottomSuiteTemplateId = new("5cd946231388ce000d572fe3");
    private static readonly MongoId TopTemplateId = new("5d28adcb86f77429242fc893");
    private static readonly MongoId TopSuiteTemplateId = new("5d1f623e86f7744bce0ef705");

    /// <summary>The Ragman suit whose shape every new top copies.</summary>
    private static readonly MongoId TopSuitTemplateId = new("5d1f65e586f7744bce0ef714");

    /// <summary>Slots a cloned gear item can inherit a bot spawn chance for.</summary>
    private static readonly EquipmentSlots[] GearSlots =
    [
        EquipmentSlots.ArmorVest,
        EquipmentSlots.TacticalVest,
        EquipmentSlots.Backpack,
        EquipmentSlots.Headwear,
        EquipmentSlots.FaceCover,
    ];

    private ModConfig config = null!;
    private TraderBase traderBase = null!;
    private readonly Dictionary<string, Dictionary<string, LocaleContent>> locales = [];
    private int currentSeason = -1;

    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var itemData = modHelper.GetJsonDataFromFile<CustomItemData>(modFolder, "db/items/itemData.json");
        traderBase = modHelper.GetJsonDataFromFile<TraderBase>(modFolder, "db/base.json");
        config = modHelper.GetJsonDataFromFile<ModConfig>(modFolder, "config.json");

        var fleaPresets = modHelper.GetJsonDataFromFile<Dictionary<MongoId, Preset>>(modFolder, "db/items/fleaPresets.json");
        var prices = modHelper.GetJsonDataFromFile<Dictionary<MongoId, double>>(modFolder, "db/items/prices.json");

        var localeFolder = IOPath.Combine(modFolder, "db/locales");
        foreach (var path in Directory.GetFiles(localeFolder, "*.json"))
        {
            locales.Add(
                IOPath.GetFileNameWithoutExtension(path),
                modHelper.GetJsonDataFromFile<Dictionary<string, LocaleContent>>(localeFolder, IOPath.GetFileName(path)));
        }

        foreach (var (presetId, preset) in fleaPresets)
        {
            if (!globals.ItemPresets.ContainsKey(presetId))
            {
                globals.ItemPresets.Add(presetId, preset);
            }
        }

        currentSeason = GetCurrentSeason();

        if (config.traderEnabled)
        {
            var traderId = traderBase.Id;
            var avatarPath = IOPath.Combine(modFolder, "res/AshleySchaeffer.jpg");
            imageRouter.AddRoute(traderBase.Avatar!.Replace(".jpg", ""), avatarPath);
            SetTraderUpdateTime(traderConfig, traderBase, timeUtil.GetHoursAsSeconds(1), timeUtil.GetHoursAsSeconds(2));
            ragfairConfig.Traders.TryAdd(traderId, true);
            AddTraderWithEmptyAssortToDb(traderBase);
            AddTraderToLocales(
                traderBase,
                "Ashley Schaeffer",
                "Son, if you die wearing my merchandise, that's a skill issue. The outfit did its job.");
        }

        var suits = new List<Suit>();

        // Which of the new suits each faction's bots may roll, once season filtering is applied.
        var botWardrobe = new Dictionary<string, Dictionary<string, List<MongoId>>>
        {
            ["Bear"] = new() { ["Tops"] = [], ["Bottoms"] = [] },
            ["Usec"] = new() { ["Tops"] = [], ["Bottoms"] = [] },
        };

        var gearCount = 0;

        if (itemData.Gear is not null)
        {
            foreach (var (_, gearItem) in itemData.Gear)
            {
                if (!gearItem.enabled)
                {
                    continue;
                }

                gearCount++;
                AddGearItem(gearItem);
                AddGearLocales(gearItem);

                if (config.botsUseGear && InSeason(gearItem.season))
                {
                    GiveGearToBots(gearItem);
                }

                if (config.sellGearOnFlea && prices.TryGetValue(gearItem.id, out var price))
                {
                    templates.Prices[gearItem.id] = price;
                }
            }
        }
        else
        {
            logger.Warning("No gear items found in itemData.json");
        }

        if (itemData.Bottoms is not null)
        {
            foreach (var (_, clothingItem) in itemData.Bottoms)
            {
                if (clothingItem.enabled != true)
                {
                    continue;
                }

                suits.Add(AddBottomItem(clothingItem, config.traderEnabled));

                if (InSeason(clothingItem.season))
                {
                    AddToWardrobe(botWardrobe, "Bottoms", clothingItem.Side, clothingItem.id);
                }
            }
        }

        if (itemData.Tops is not null)
        {
            foreach (var (_, clothingItem) in itemData.Tops)
            {
                if (clothingItem.enabled != true)
                {
                    continue;
                }

                suits.Add(AddTopItem(clothingItem, config.traderEnabled));

                if (InSeason(clothingItem.season))
                {
                    AddToWardrobe(botWardrobe, "Tops", clothingItem.Side, clothingItem.id);
                }
            }
        }

        if (config.unlockOthersFactionsClothing)
        {
            UnlockFactionLockedClothing();
        }

        if (config.botsUseClothing)
        {
            DressBots(botWardrobe);
        }

        AttachSuitsToTrader(suits, modFolder);

        logger.Info($"[Ashley Schaeffer] Clothing items added: {suits.Count}");
        logger.Info($"[Ashley Schaeffer] Gear items added: {gearCount}");

        return Task.CompletedTask;
    }

    /// <summary>True when the piece has no season restriction, or the current season is one of them.</summary>
    private bool InSeason(List<int>? season) =>
        season is null || currentSeason == -1 || season.Contains(currentSeason);

    private static void AddToWardrobe(
        Dictionary<string, Dictionary<string, List<MongoId>>> wardrobe,
        string slot,
        List<string>? sides,
        MongoId id)
    {
        if (sides is not null && sides.Count > 0)
        {
            foreach (var side in sides)
            {
                wardrobe[side][slot].Add(id);
            }

            return;
        }

        wardrobe["Bear"][slot].Add(id);
        wardrobe["Usec"][slot].Add(id);
    }

    public void SetTraderUpdateTime(
        TraderConfig traderConfig,
        TraderBase baseJson,
        int refreshTimeSecondsMin,
        int refreshTimeSecondsMax)
    {
        traderConfig.UpdateTime.Add(new UpdateTime
        {
            TraderId = baseJson.Id,
            Seconds = new MinMax<int>(refreshTimeSecondsMin, refreshTimeSecondsMax),
        });
    }

    public void AddTraderWithEmptyAssortToDb(TraderBase traderDetailsToAdd)
    {
        var trader = new Trader
        {
            Assort = new TraderAssort
            {
                Items = [],
                BarterScheme = [],
                LoyalLevelItems = [],
            },
            Base = cloner.Clone(traderDetailsToAdd)!,
            QuestAssort = new Dictionary<string, Dictionary<MongoId, MongoId>>
            {
                ["Started"] = [],
                ["Success"] = [],
                ["Fail"] = [],
            },
            Dialogue = [],
        };

        traders.TryAdd(traderDetailsToAdd.Id, trader);
    }

    public void AddTraderToLocales(TraderBase baseJson, string firstName, string description)
    {
        var newTraderId = baseJson.Id;
        var fullName = baseJson.Name;
        var nickName = baseJson.Nickname;
        var location = baseJson.Location;

        foreach (var (_, lazyLocale) in localeTable.Global)
        {
            lazyLocale.AddTransformer(lazyloadedLocaleData =>
            {
                lazyloadedLocaleData ??= [];
                lazyloadedLocaleData.Add($"{newTraderId} FullName", fullName!);
                lazyloadedLocaleData.Add($"{newTraderId} FirstName", firstName);
                lazyloadedLocaleData.Add($"{newTraderId} Nickname", nickName!);
                lazyloadedLocaleData.Add($"{newTraderId} Location", location!);
                lazyloadedLocaleData.Add($"{newTraderId} Description", description);
                return lazyloadedLocaleData;
            });
        }
    }

    public void OverwriteTraderAssort(MongoId traderId, TraderAssort newAssorts)
    {
        if (!traders.TryGetValue(traderId, out var trader))
        {
            logger.Warning($"Unable to update assorts for trader: {traderId}, they couldn't be found on the server");
            return;
        }

        trader.Assort = newAssorts;
    }

    /// <summary>
    /// Hands the finished suits to whoever is selling them: the mod's own trader, or Ragman when
    /// the trader is switched off.
    /// </summary>
    private void AttachSuitsToTrader(List<Suit> suits, string modFolder)
    {
        if (config.traderEnabled)
        {
            var assort = modHelper.GetJsonDataFromFile<TraderAssort>(modFolder, "db/assort.json");
            OverwriteTraderAssort(traderBase.Id, assort);

            if (traders.TryGetValue(traderBase.Id, out var trader))
            {
                trader.Suits ??= [];
                trader.Suits.AddRange(suits);
            }

            return;
        }

        if (!traders.TryGetValue(RagmanId, out var ragman))
        {
            return;
        }

        ragman.Suits ??= [];
        foreach (var suit in suits)
        {
            suit.Tid = RagmanId;
            ragman.Suits.Add(suit);
        }

        logger.Info($"[Ashley Schaeffer] Added {suits.Count} suits to Ragman");
    }

    private void AddGearItem(GearItem gearItem)
    {
        var items = templates.Items;
        var baseItemId = gearItem.BaseItemID;

        var item = cloner.Clone(items[baseItemId])!;
        item.Id = gearItem.id;
        item.Name = gearItem.name;

        if (config.allGearAvailableOnFlea && config.sellGearOnFlea)
        {
            item.Properties!.CanSellOnRagfair = true;
            item.Properties.CanRequireOnRagfair = true;
        }
        else if (!config.sellGearOnFlea)
        {
            item.Properties!.CanSellOnRagfair = false;
            item.Properties.CanRequireOnRagfair = false;
        }

        item.Properties!.Prefab!.Path = gearItem.BundlePath;
        items[gearItem.id] = item;

        if (gearItem.needsFilterUpdate == true)
        {
            UpdateFilters(gearItem.id, baseItemId);
        }

        var handbookEntry = cloner.Clone(templates.Handbook.Items.Find(i => i.Id == baseItemId))!;
        handbookEntry.Id = gearItem.id;
        templates.Handbook.Items.Add(handbookEntry);
    }

    /// <summary>
    /// Registers the item's name/short name/description in every locale. Falls back to the internal
    /// name when a locale has no entry for it, so a missing translation never shows a blank item.
    /// </summary>
    private void AddGearLocales(GearItem gearItem)
    {
        foreach (var (localeKey, lazyLocale) in localeTable.Global)
        {
            lazyLocale.AddTransformer(lazyloadedLocaleData =>
            {
                var content =
                    locales.TryGetValue(localeKey, out var localeTable)
                    && localeTable.TryGetValue(gearItem.name ?? string.Empty, out var found)
                        ? found
                        : new LocaleContent
                        {
                            Name = gearItem.name ?? "Unknown",
                            ShortName = gearItem.name ?? "Unknown",
                            Description = "No description available.",
                        };

                lazyloadedLocaleData ??= [];
                lazyloadedLocaleData.Add($"{gearItem.id} Name", content.Name ?? gearItem.name ?? "Unknown");
                lazyloadedLocaleData.Add($"{gearItem.id} ShortName", content.ShortName ?? gearItem.name ?? "Unknown");
                lazyloadedLocaleData.Add($"{gearItem.id} Description", content.Description ?? "No description available.");
                return lazyloadedLocaleData;
            });
        }
    }

    /// <summary>
    /// Gives every bot that already rolls the base item the same chance to roll the clone, in each
    /// of the five slots a clone can live in. Mod compatibility comes along for free: the mod copies
    /// whatever spawn chance is in the table when it runs, so another mod's bots are covered too.
    /// </summary>
    private void GiveGearToBots(GearItem gearItem)
    {
        foreach (var (botName, botType) in botTable.Types)
        {
            if (botType is null)
            {
                continue;
            }

            var isPmc = botName.Equals("usec") || botName.Equals("bear");
            if (isPmc && config.pmcsUseFactionedGearOnly && (gearItem.side is null || !botName.Equals(gearItem.side)))
            {
                continue;
            }

            foreach (var slot in GearSlots)
            {
                if (botType.BotInventory?.Equipment is null
                    || !botType.BotInventory.Equipment.TryGetValue(slot, out var slotItems)
                    || !slotItems.ContainsKey(gearItem.BaseItemID))
                {
                    continue;
                }

                slotItems[gearItem.id] = Math.Max(1, (int)Math.Round(slotItems[gearItem.BaseItemID]));

                if (botType.BotInventory.Mods is not null
                    && botType.BotInventory.Mods.TryGetValue(gearItem.BaseItemID, out var mods))
                {
                    botType.BotInventory.Mods[gearItem.id] = mods;
                }
            }
        }
    }

    /// <summary>Lets BEAR wear USEC-only vanilla clothing and vice versa.</summary>
    private void UnlockFactionLockedClothing()
    {
        foreach (var (_, customization) in templates.Customization)
        {
            var properties = customization.Properties;
            if (properties?.Side is null
                || properties.Side.Count != 1
                || (!properties.Feet.HasValue && !properties.Hands.HasValue))
            {
                continue;
            }

            if (properties.Side[0] is "Usec" or "Bear")
            {
                properties.Side = ["Bear", "Usec"];
            }
        }
    }

    /// <summary>
    /// Puts a random half of the new wardrobe into each PMC faction's appearance table, so bots wear
    /// the new outfits without drowning out the vanilla ones.
    /// </summary>
    private void DressBots(Dictionary<string, Dictionary<string, List<MongoId>>> wardrobe)
    {
        if (!botTable.Types.TryGetValue("usec", out var usec) || usec is null
            || !botTable.Types.TryGetValue("bear", out var bear) || bear is null)
        {
            return;
        }

        Apply(usec.BotAppearance.Feet, RandomSample(wardrobe["Usec"]["Bottoms"], usec.BotAppearance.Feet.Count / 2));
        Apply(usec.BotAppearance.Body, RandomSample(wardrobe["Usec"]["Tops"], usec.BotAppearance.Body.Count / 2));
        Apply(bear.BotAppearance.Feet, RandomSample(wardrobe["Bear"]["Bottoms"], bear.BotAppearance.Feet.Count / 2));
        Apply(bear.BotAppearance.Body, RandomSample(wardrobe["Bear"]["Tops"], bear.BotAppearance.Body.Count / 2));

        static void Apply(Dictionary<MongoId, double> table, List<MongoId> picks)
        {
            foreach (var id in picks)
            {
                if (!table.ContainsKey(id))
                {
                    table.Add(id, 1.0);
                }
            }
        }
    }

    private Suit AddBottomItem(ClothingItem clothingItem, bool traderEnabled)
    {
        var sellerId = traderEnabled ? traderBase.Id : RagmanId;

        var mesh = cloner.Clone(templates.Customization[BottomTemplateId])!;
        mesh.Id = clothingItem.id;
        mesh.Name = clothingItem.name!;
        mesh.Properties!.Prefab = new Dictionary<string, string>
        {
            ["Path"] = clothingItem.BundlePath!,
            ["rcid"] = string.Empty,
        };
        templates.Customization.Add(clothingItem.id, mesh);

        var suite = cloner.Clone(templates.Customization[BottomSuiteTemplateId])!;
        suite.Id = clothingItem.SuiteId;
        suite.Name = (clothingItem.name + "_suite").ToLower();
        suite.Properties!.Feet = clothingItem.id;
        suite.Properties.Side = clothingItem.Side is { Count: > 0 } ? clothingItem.Side : ["Usec", "Bear"];
        templates.Customization.Add(clothingItem.SuiteId, suite);

        var suit = new Suit
        {
            Id = clothingItem.id,
            Tid = sellerId,
            SuiteId = clothingItem.SuiteId,
            IsActive = true,
            IsHiddenInPVE = false,
            ExternalObtain = false,
            InternalObtain = true,
            Requirements = BuildRequirements(clothingItem, sellerId),
        };

        AddSuiteLocales(clothingItem, "Ashley Schaeffer's bottom");

        return suit;
    }

    private Suit AddTopItem(ClothingItemTop clothingItem, bool traderEnabled)
    {
        var sellerId = traderEnabled ? traderBase.Id : RagmanId;

        var mesh = cloner.Clone(templates.Customization[TopTemplateId])!;
        mesh.Id = clothingItem.id;
        mesh.Name = clothingItem.name!;
        mesh.Properties!.Prefab = new Dictionary<string, string>
        {
            ["Path"] = clothingItem.BundlePath!,
            ["rcid"] = string.Empty,
        };
        templates.Customization.Add(clothingItem.id, mesh);

        // Sleeves are part of the hands mesh, so a top with its own bundle needs its own hands too.
        if (!string.IsNullOrEmpty(clothingItem.BundlePath) && clothingItem.HandsId != clothingItem.HandsBaseID)
        {
            var hands = cloner.Clone(templates.Customization[clothingItem.HandsBaseID])!;
            hands.Id = clothingItem.HandsId;
            hands.Name = clothingItem.HandsId.ToString();
            hands.Properties!.Prefab = new Dictionary<string, string>
            {
                ["Path"] = clothingItem.HandsBundlePath!,
                ["rcid"] = string.Empty,
            };

            if (!templates.Customization.ContainsKey(clothingItem.HandsId))
            {
                templates.Customization.Add(clothingItem.HandsId, hands);
            }
        }

        var suite = cloner.Clone(templates.Customization[TopSuiteTemplateId])!;
        suite.Id = clothingItem.SuiteId;
        suite.Name = (clothingItem.name + "_suite").ToLower();
        suite.Properties!.Body = clothingItem.id;
        suite.Properties.Hands = clothingItem.HandsBundlePath is not null ? clothingItem.HandsId : clothingItem.HandsBaseID;
        suite.Properties.Side = clothingItem.Side is { Count: > 0 } ? clothingItem.Side : ["Usec", "Bear"];
        templates.Customization.Add(clothingItem.SuiteId, suite);

        // Cloned from a Ragman suit rather than built from scratch, so any field EFT reads that the
        // mod does not know about is carried over.
        var suit = cloner.Clone(traders[RagmanId].Suits!.Find(s => s.Id == TopSuitTemplateId))!;
        suit.Id = clothingItem.id;

        // Deliberate fix, and the only behavioural difference from the 4.0 build. That build wrote
        //     suit.ExtensionData["tid"] = sellerId;
        // here, while AddBottomItem right above wrote suit.Tid. Suit.Tid already carries
        // [JsonPropertyName("tid")], so the extension-data write produced a second "tid" key and
        // left Tid itself holding the id it was cloned with - Ragman's. With the trader enabled,
        // every top therefore claimed to belong to Ragman while its requirements pointed at Ashley.
        // Setting Tid is what AddBottomItem does and what the rest of the method assumes.
        suit.Tid = sellerId;
        suit.SuiteId = clothingItem.SuiteId;
        suit.IsActive = true;
        suit.IsHiddenInPVE = false;
        suit.ExternalObtain = false;
        suit.InternalObtain = true;
        suit.Requirements = BuildRequirements(clothingItem, sellerId);

        AddSuiteLocales(clothingItem, "Ashley Schaeffer's top");

        return suit;
    }

    private static SuitRequirements BuildRequirements(ClothingItem clothingItem, MongoId sellerId) =>
        new()
        {
            LoyaltyLevel = clothingItem.LoyaltyLevel,
            ProfileLevel = clothingItem.ProfileLevel,
            Standing = clothingItem.Standing,
            SkillRequirements = [],
            QuestRequirements = [],
            AchievementRequirements = [],
            ItemRequirements =
            [
                new ItemRequirement
                {
                    Count = clothingItem.Price,
                    Tpl = RoublesId,
                    OnlyFunctional = true,
                },
            ],
            RequiredTid = sellerId,
        };

    private void AddSuiteLocales(ClothingItem clothingItem, string shortNameAndDescription)
    {
        foreach (var (_, lazyLocale) in localeTable.Global)
        {
            lazyLocale.AddTransformer(lazyloadedLocaleData =>
            {
                lazyloadedLocaleData ??= [];
                lazyloadedLocaleData.Add($"{clothingItem.SuiteId} Name", clothingItem.displayName ?? "Unknown");
                lazyloadedLocaleData.Add($"{clothingItem.SuiteId} ShortName", shortNameAndDescription);
                lazyloadedLocaleData.Add($"{clothingItem.SuiteId} Description", shortNameAndDescription);
                return lazyloadedLocaleData;
            });
        }
    }

    /// <summary>
    /// Copies the base item's place in the item graph onto the clone: anything that conflicts with
    /// the base conflicts with the clone, and any slot that accepts the base accepts the clone.
    /// </summary>
    private void UpdateFilters(MongoId itemId, MongoId baseItemId)
    {
        foreach (var item in templates.Items.Values)
        {
            if (item.Properties is null)
            {
                continue;
            }

            if (item.Properties.ConflictingItems is { } conflicting && conflicting.Contains(baseItemId))
            {
                conflicting.Add(itemId);
            }

            if (item.Properties.Slots is null)
            {
                continue;
            }

            foreach (var slot in item.Properties.Slots)
            {
                var filters = slot.Properties?.Filters;
                if (filters is null || !filters.Any())
                {
                    continue;
                }

                var filter = filters.First().Filter;
                if (filter is not null && filter.Contains(baseItemId))
                {
                    filter.Add(itemId);
                }
            }
        }
    }

    /// <summary>Partial Fisher-Yates: shuffles only the first N, which is all that gets returned.</summary>
    private static List<T> RandomSample<T>(List<T> arr, int N)
    {
        if (N <= 0)
        {
            return [];
        }

        if (arr.Count <= N)
        {
            return new List<T>(arr);
        }

        var pool = new List<T>(arr);
        var random = new Random();
        for (var i = 0; i < N; i++)
        {
            var j = i + random.Next(pool.Count - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(N).ToList();
    }

    /// <summary>
    /// The in-game season, as an <c>EFT.Season</c> ordinal, or -1 when the server has no season
    /// table to answer from. Season windows may wrap the new year, hence the two-branch compare.
    /// </summary>
    private int GetCurrentSeason(DateTime? date = null)
    {
        var now = date ?? DateTime.Now;

        if (weatherConfig.OverrideSeason.HasValue)
        {
            return (int)weatherConfig.OverrideSeason.Value;
        }

        var seasonDates = weatherConfig.SeasonDates;
        if (seasonDates is null || seasonDates.Count == 0)
        {
            return -1;
        }

        var today = now.Month * 100 + now.Day;

        foreach (var season in seasonDates)
        {
            var start = season.StartMonth.GetValueOrDefault() * 100 + season.StartDay.GetValueOrDefault();
            var end = season.EndMonth.GetValueOrDefault() * 100 + season.EndDay.GetValueOrDefault();

            var inWindow = end >= start
                ? today >= start && today <= end     // ordinary window inside one year
                : today >= start || today <= end;    // window that wraps 31 Dec

            if (inWindow)
            {
                return (int)season.SeasonType.GetValueOrDefault();
            }
        }

        return -1;
    }
}
