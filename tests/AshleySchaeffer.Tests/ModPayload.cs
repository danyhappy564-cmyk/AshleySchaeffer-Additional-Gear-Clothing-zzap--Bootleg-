using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

// SPT models a prefab path as a type called Path, which collides with System.IO.Path.
using IOPath = System.IO.Path;

namespace AshleySchaeffer.Tests;

/// <summary>
/// The mod's real shipped JSON, read straight out of mod/ at the repo root and through SPT's own
/// JsonUtil - a plain JsonSerializer cannot read a MongoId.
/// </summary>
internal static class ModPayload
{
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);

    private static T Read<T>(string relative) => Json.Deserialize<T>(File.ReadAllText(IOPath.Combine(SourceFolder, relative)))!;

    internal static string SourceFolder { get; } = Locate();

    internal static string ShippedConfigPath { get; } = IOPath.Combine(SourceFolder, "config.json");

    internal static CustomItemData ItemData { get; } =
        Read<CustomItemData>("db/items/itemData.json");

    internal static Dictionary<string, LocaleContent> EnglishLocale { get; } =
        Read<Dictionary<string, LocaleContent>>("db/locales/en.json");

    internal static Dictionary<MongoId, double> Prices { get; } =
        Read<Dictionary<MongoId, double>>("db/items/prices.json");

    internal static Dictionary<MongoId, Preset> FleaPresets { get; } =
        Read<Dictionary<MongoId, Preset>>("db/items/fleaPresets.json");

    /// <summary>A fresh copy each call, so a test can tweak it without leaking into the next.</summary>
    internal static ModConfig ShippedConfig() => Read<ModConfig>("config.json");

    internal static TraderBase TraderBase { get; } =
        Read<TraderBase>("db/base.json");

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = IOPath.Combine(dir.FullName, "mod");
            if (File.Exists(IOPath.Combine(candidate, "config.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the mod/ payload folder above " + AppContext.BaseDirectory);
    }
}
