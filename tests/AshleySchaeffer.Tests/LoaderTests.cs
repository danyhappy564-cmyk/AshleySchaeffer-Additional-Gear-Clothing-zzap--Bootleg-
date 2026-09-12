using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

// SPT models a prefab path as a type called Path, which collides with System.IO.Path.
using IOPath = System.IO.Path;

namespace AshleySchaeffer.Tests;

/// <summary>
/// End-to-end checks: the real loader, the real shipped JSON, a stand-in database. Every assertion
/// is about what ends up in the database, so a change in behaviour shows up here even when the code
/// still compiles.
/// </summary>
/// <remarks>
/// Serialised into one collection because the loader reads config.json from its own output folder,
/// which the harness rewrites per test.
/// </remarks>
[Collection(nameof(LoaderTests))]
[CollectionDefinition(nameof(LoaderTests), DisableParallelization = true)]
public class LoaderTests
{
    private static readonly MongoId RagmanId = new("5ac3b934156ae10c4430e83c");
    private static readonly MongoId RoublesId = new("5449016a4bdc2d6f028b456f");

    [Fact]
    public async Task Registers_every_enabled_gear_item_as_a_clone_of_its_base()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        var gear = ModPayload.ItemData.Gear!.Values.Where(g => g.enabled).ToList();
        Assert.Equal(27, gear.Count);

        foreach (var g in gear)
        {
            Assert.True(h.Db.Templates.Items.ContainsKey(g.id), $"{g.name} was not added to Templates.Items");

            var added = h.Db.Templates.Items[g.id];
            Assert.Equal(g.id, added.Id);
            Assert.Equal(g.name, added.Name);

            // The clone points at the mod's bundle, not the vanilla one it was cloned from.
            Assert.Equal(g.BundlePath, added.Properties!.Prefab!.Path);
            Assert.NotEqual(h.Db.Templates.Items[g.BaseItemID].Properties!.Prefab!.Path, added.Properties.Prefab.Path);

            // ...and it is a copy, not the same object.
            Assert.NotSame(h.Db.Templates.Items[g.BaseItemID], added);
        }

        Assert.Contains(h.Infos, i => i.Contains("Gear items added: 27"));
    }

    [Fact]
    public async Task Gives_every_gear_item_a_handbook_entry()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        foreach (var g in ModPayload.ItemData.Gear!.Values.Where(g => g.enabled))
        {
            Assert.Single(h.Db.Templates.Handbook.Items.Where(i => i.Id == g.id));
        }
    }

    [Fact]
    public async Task Names_every_gear_item_in_the_locale_table()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        var locale = h.Db.Locale;
        var shipped = ModPayload.EnglishLocale;

        foreach (var g in ModPayload.ItemData.Gear!.Values.Where(g => g.enabled))
        {
            Assert.True(locale.ContainsKey($"{g.id} Name"), $"{g.name} has no locale Name");
            Assert.True(locale.ContainsKey($"{g.id} ShortName"));
            Assert.True(locale.ContainsKey($"{g.id} Description"));

            // The shipped en.json wins over the internal-name fallback.
            Assert.Equal(shipped[g.name!].Name, locale[$"{g.id} Name"]);
        }
    }

    [Fact]
    public async Task Registers_every_enabled_outfit_as_a_mesh_plus_a_suite()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        var tops = ModPayload.ItemData.Tops!.Values.Where(t => t.enabled == true).ToList();
        var bottoms = ModPayload.ItemData.Bottoms!.Values.Where(b => b.enabled == true).ToList();
        Assert.Equal(29, tops.Count);
        Assert.Equal(23, bottoms.Count);

        foreach (var c in tops.Cast<ClothingItem>().Concat(bottoms))
        {
            Assert.True(h.Db.Templates.Customization.ContainsKey(c.id), $"{c.name} mesh missing");
            Assert.True(h.Db.Templates.Customization.ContainsKey(c.SuiteId), $"{c.name} suite missing");

            // CustomizationProperties.Prefab is loosely typed; the mod writes the {Path, rcid} pair.
            var prefab = Assert.IsType<Dictionary<string, string>>(h.Db.Templates.Customization[c.id].Properties.Prefab);
            Assert.Equal(c.BundlePath, prefab["Path"]);
            Assert.Equal(string.Empty, prefab["rcid"]);

            Assert.Equal((c.name + "_suite").ToLower(), h.Db.Templates.Customization[c.SuiteId].Name);
        }

        Assert.Contains(h.Infos, i => i.Contains($"Clothing items added: {tops.Count + bottoms.Count}"));
    }

    [Fact]
    public async Task A_top_points_its_suite_at_its_own_body_and_hands()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        foreach (var t in ModPayload.ItemData.Tops!.Values.Where(t => t.enabled == true))
        {
            var suite = h.Db.Templates.Customization[t.SuiteId].Properties;
            Assert.Equal(t.id, suite.Body);
            Assert.Equal(t.HandsBundlePath is not null ? t.HandsId : t.HandsBaseID, suite.Hands);
        }
    }

    [Fact]
    public async Task A_bottom_points_its_suite_at_its_own_feet()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        foreach (var b in ModPayload.ItemData.Bottoms!.Values.Where(b => b.enabled == true))
        {
            Assert.Equal(b.id, h.Db.Templates.Customization[b.SuiteId].Properties.Feet);
        }
    }

    [Fact]
    public async Task Prices_every_suit_in_roubles_at_the_figure_from_itemData()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        var trader = h.Db.Traders[h.AshleyId];
        var bySuite = trader.Suits!.ToDictionary(s => s.SuiteId);

        foreach (var c in ModPayload.ItemData.Tops!.Values.Cast<ClothingItem>()
                     .Concat(ModPayload.ItemData.Bottoms!.Values)
                     .Where(c => c.enabled == true))
        {
            var req = bySuite[c.SuiteId].Requirements!;
            var item = Assert.Single(req.ItemRequirements!);
            Assert.Equal(RoublesId, item.Tpl);
            Assert.Equal(c.Price, item.Count);
            Assert.Equal(c.LoyaltyLevel, req.LoyaltyLevel);
            Assert.Equal(c.ProfileLevel, req.ProfileLevel);
            Assert.Equal(c.Standing, req.Standing);
            Assert.Equal(h.AshleyId, req.RequiredTid);
        }
    }

    /// <summary>
    /// The one deliberate behavioural change from the 4.0 build, pinned so it cannot regress: the
    /// 4.0 AddTopItem wrote ExtensionData["tid"] and left Tid holding Ragman's id from the clone.
    /// </summary>
    [Fact]
    public async Task Every_suit_belongs_to_the_trader_that_sells_it_tops_included()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        var trader = h.Db.Traders[h.AshleyId];
        Assert.Equal(52, trader.Suits!.Count);
        Assert.All(trader.Suits, s => Assert.Equal(h.AshleyId, s.Tid));
        Assert.DoesNotContain(trader.Suits, s => s.Tid == RagmanId);
    }

    [Fact]
    public async Task Sells_through_Ragman_when_the_trader_is_switched_off()
    {
        var h = LoaderHarness.Create(c => c.traderEnabled = false);
        await h.RunAsync();

        Assert.False(h.Db.Traders.ContainsKey(h.AshleyId));

        var ragman = h.Db.Traders[RagmanId];
        // One vanilla suit was there to begin with.
        Assert.Equal(53, ragman.Suits!.Count);
        Assert.Equal(52, ragman.Suits.Count(s => s.Tid == RagmanId) - 1);
        Assert.Contains(h.Infos, i => i.Contains("Added 52 suits to Ragman"));
    }

    [Fact]
    public async Task Registers_the_trader_with_an_assort_a_refresh_window_and_a_flea_entry()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        Assert.True(h.Db.Traders.ContainsKey(h.AshleyId));
        var trader = h.Db.Traders[h.AshleyId];

        Assert.Equal(h.AshleyId, trader.Base!.Id);
        Assert.NotNull(trader.Assort);
        Assert.NotEmpty(trader.Assort!.Items!);

        var update = Assert.Single(h.TraderConfig.UpdateTime.Where(u => u.TraderId == h.AshleyId));
        Assert.Equal(3600, update.Seconds!.Min);
        Assert.Equal(7200, update.Seconds.Max);

        Assert.True(h.RagfairConfig.Traders[h.AshleyId]);

        var locale = h.Db.Locale;
        Assert.Equal("Ashley Schaeffer", locale[$"{h.AshleyId} FirstName"]);
        Assert.Equal("Ashley Schaeffer", locale[$"{h.AshleyId} FullName"]);
    }

    [Fact]
    public async Task Copies_the_base_items_bot_spawn_chance_onto_the_clone()
    {
        var h = LoaderHarness.Create(c =>
        {
            c.botsUseGear = true;
            c.pmcsUseFactionedGearOnly = false;
        });
        await h.RunAsync();

        var assault = h.Db.Bots.Types["assault"]!;
        foreach (var g in ModPayload.ItemData.Gear!.Values.Where(g => g.enabled))
        {
            foreach (var slot in new[]
                     {
                         EquipmentSlots.ArmorVest, EquipmentSlots.TacticalVest, EquipmentSlots.Backpack,
                         EquipmentSlots.Headwear, EquipmentSlots.FaceCover,
                     })
            {
                var table = assault.BotInventory!.Equipment![slot];
                Assert.True(table.ContainsKey(g.id), $"{g.name} missing from {slot}");
                Assert.Equal(table[g.BaseItemID], table[g.id]);
            }
        }
    }

    [Fact]
    public async Task Leaves_bot_loadouts_alone_when_botsUseGear_is_off()
    {
        var h = LoaderHarness.Create(c => c.botsUseGear = false);
        await h.RunAsync();

        var assault = h.Db.Bots.Types["assault"]!;
        foreach (var g in ModPayload.ItemData.Gear!.Values.Where(g => g.enabled))
        {
            Assert.False(assault.BotInventory!.Equipment![EquipmentSlots.ArmorVest].ContainsKey(g.id));
        }
    }

    /// <summary>
    /// Pins a pre-existing bug rather than the intended behaviour, so that it is visible and cannot
    /// change by accident.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <c>pmcsUseFactionedGearOnly</c> on - which is the shipped default - a faction-tagged
    /// gear item reaches NEITHER PMC faction. The comparison is
    /// <c>botName.Equals(gearItem.side)</c>, but bot table keys are lower case ("bear", "usec")
    /// while itemData.json spells the side "Bear", so it never matches and both factions are
    /// skipped. Everyone else - scavs, bosses, raiders - still gets the gear, because the filter
    /// only applies to PMCs.
    /// </para>
    /// <para>
    /// Two of the 27 gear entries also spell the side "Bear, usec", which is not a bot name at all
    /// and so cannot match under any comparison.
    /// </para>
    /// <para>
    /// Carried over from the 4.0 build unchanged: fixing it would put gear on PMCs that players of
    /// this mod have never seen there. The one-line fix is to compare case-insensitively.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Faction_locking_keeps_tagged_gear_off_BOTH_PMC_factions_a_known_bug()
    {
        var h = LoaderHarness.Create(c =>
        {
            c.botsUseGear = true;
            c.pmcsUseFactionedGearOnly = true;
        });
        await h.RunAsync();

        var bearOnly = ModPayload.ItemData.Gear!.Values
            .Single(g => g.enabled && g.name == "item_equipment_rig_forts_emr");
        Assert.Equal("Bear", bearOnly.side);

        var vest = EquipmentSlots.TacticalVest;
        Assert.False(h.Db.Bots.Types["bear"]!.BotInventory!.Equipment![vest].ContainsKey(bearOnly.id));
        Assert.False(h.Db.Bots.Types["usec"]!.BotInventory!.Equipment![vest].ContainsKey(bearOnly.id));

        // Non-PMC bots are never faction-filtered, so they do get it.
        Assert.True(h.Db.Bots.Types["assault"]!.BotInventory!.Equipment![vest].ContainsKey(bearOnly.id));

        // All 27 shipped gear entries carry a side, so under this switch PMCs get none of them.
        var enabled = ModPayload.ItemData.Gear!.Values.Where(g => g.enabled).ToList();
        Assert.All(enabled, g => Assert.NotNull(g.side));
        Assert.All(enabled, g => Assert.False(h.Db.Bots.Types["bear"]!.BotInventory!.Equipment![vest].ContainsKey(g.id)));
        Assert.All(enabled, g => Assert.False(h.Db.Bots.Types["usec"]!.BotInventory!.Equipment![vest].ContainsKey(g.id)));
        Assert.All(enabled, g => Assert.True(h.Db.Bots.Types["assault"]!.BotInventory!.Equipment![vest].ContainsKey(g.id)));
    }

    [Fact]
    public async Task Flea_prices_land_only_when_sellGearOnFlea_is_on()
    {
        var on = LoaderHarness.Create(c => { c.sellGearOnFlea = true; c.allGearAvailableOnFlea = true; });
        await on.RunAsync();

        foreach (var (id, price) in ModPayload.Prices)
        {
            Assert.Equal(price, on.Db.Templates.Prices[id]);
        }

        Assert.All(ModPayload.ItemData.Gear!.Values.Where(g => g.enabled),
            g => Assert.True(on.Db.Templates.Items[g.id].Properties!.CanSellOnRagfair));

        var off = LoaderHarness.Create(c => c.sellGearOnFlea = false);
        await off.RunAsync();

        Assert.Empty(off.Db.Templates.Prices);
        Assert.All(ModPayload.ItemData.Gear!.Values.Where(g => g.enabled),
            g => Assert.False(off.Db.Templates.Items[g.id].Properties!.CanSellOnRagfair));
    }

    [Fact]
    public async Task Unlocking_other_factions_clothing_opens_a_locked_vanilla_outfit_to_both()
    {
        var lockedId = new MongoId("5cde95d97d6c8b647a3769b0");

        var on = LoaderHarness.Create(c => c.unlockOthersFactionsClothing = true);
        await on.RunAsync();
        Assert.Equal(["Bear", "Usec"], on.Db.Templates.Customization[lockedId].Properties.Side!);

        var off = LoaderHarness.Create(c => c.unlockOthersFactionsClothing = false);
        await off.RunAsync();
        Assert.Equal(["Usec"], off.Db.Templates.Customization[lockedId].Properties.Side!);
    }

    [Fact]
    public async Task Dresses_bots_in_at_most_half_the_new_wardrobe_per_slot()
    {
        var h = LoaderHarness.Create(c => c.botsUseClothing = true);
        await h.RunAsync();

        var usec = h.Db.Bots.Types["usec"]!.BotAppearance;
        var bear = h.Db.Bots.Types["bear"]!.BotAppearance;

        // 10 vanilla entries each to start with, so the sample size is 5.
        Assert.Equal(15, usec.Body!.Count);
        Assert.Equal(15, usec.Feet!.Count);
        Assert.Equal(15, bear.Body!.Count);
        Assert.Equal(15, bear.Feet!.Count);

        var usecTops = ModPayload.ItemData.Tops!.Values
            .Where(t => t.enabled == true && (t.Side is null || t.Side.Count == 0 || t.Side.Contains("Usec")))
            .Select(t => t.id).ToHashSet();
        Assert.All(usec.Body.Keys.Where(k => usecTops.Contains(k)), k => Assert.Equal(1.0, usec.Body[k]));
    }

    [Fact]
    public async Task Leaves_bot_appearance_alone_when_botsUseClothing_is_off()
    {
        var h = LoaderHarness.Create(c => c.botsUseClothing = false);
        await h.RunAsync();

        Assert.Equal(10, h.Db.Bots.Types["usec"]!.BotAppearance.Body!.Count);
        Assert.Equal(10, h.Db.Bots.Types["bear"]!.BotAppearance.Feet!.Count);
    }

    [Fact]
    public async Task Adds_the_flea_presets_without_clobbering_existing_ones()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        Assert.Equal(15, h.Db.Globals.ItemPresets.Count);
        foreach (var id in ModPayload.FleaPresets.Keys)
        {
            Assert.True(h.Db.Globals.ItemPresets.ContainsKey(id));
        }
    }

    [Fact]
    public async Task Routes_the_trader_avatar_image()
    {
        var h = LoaderHarness.Create();
        await h.RunAsync();

        // base.json says /files/trader/avatar/avatar.jpg; the route drops the extension.
        Assert.Equal("/files/trader/avatar/avatar", ModPayload.TraderBase.Avatar!.Replace(".jpg", ""));
    }

    [Fact]
    public async Task Falls_back_to_a_portrait_that_resolves_when_the_mod_ships_no_image()
    {
        // The public repo this port was restored from never carried res/AshleySchaeffer.jpg, so the
        // route the 4.0 build registered pointed at nothing. Rather than serve a 404, point the
        // trader at vanilla's placeholder and warn.
        var modFolder = IOPath.GetDirectoryName(typeof(AshleySchaefferLoader).Assembly.Location)!;
        var shipped = File.Exists(IOPath.Combine(modFolder, "res/AshleySchaeffer.jpg"));

        var h = LoaderHarness.Create();
        await h.RunAsync();

        var avatar = h.Db.Traders[h.AshleyId].Base!.Avatar;

        if (shipped)
        {
            Assert.Equal("/files/trader/avatar/avatar.jpg", avatar);
            Assert.DoesNotContain(h.Warnings, w => w.Contains("Portrait not found"));
        }
        else
        {
            Assert.Equal("/files/trader/avatar/unknown.png", avatar);
            Assert.Contains(h.Warnings, w => w.Contains("Portrait not found"));
        }
    }

    [Fact]
    public async Task Registered_trader_is_visible_in_PVE()
    {
        // SPT hard-codes the session to "pve" (GameController.GetGameMode), and every one of the 12
        // vanilla traders carries isAvailableInPVE: true. TraderBase.IsAvailableInPVE is a
        // non-nullable bool, so a base.json that omits the key ships "isAvailableInPVE": false to
        // the client, which then hides the trader - no server-side error anywhere. This is what
        // made the trader invisible in game.
        var h = LoaderHarness.Create();
        await h.RunAsync();

        var registered = h.Db.Traders[h.AshleyId].Base!;

        Assert.True(registered.IsAvailableInPVE);
        Assert.True(ModPayload.TraderBase.IsAvailableInPVE);
    }

    [Fact]
    public async Task Reads_its_payload_from_the_layout_it_ships_with()
    {
        var folder = IOPath.GetDirectoryName(typeof(AshleySchaefferLoader).Assembly.Location)!;

        foreach (var relative in new[]
                 {
                     "config.json", "bundles.json", "db/base.json", "db/assort.json",
                     "db/items/itemData.json", "db/items/fleaPresets.json", "db/items/prices.json",
                     "db/locales/en.json",
                 })
        {
            Assert.True(File.Exists(IOPath.Combine(folder, relative)), relative + " is not next to the dll");
        }
    }
}
