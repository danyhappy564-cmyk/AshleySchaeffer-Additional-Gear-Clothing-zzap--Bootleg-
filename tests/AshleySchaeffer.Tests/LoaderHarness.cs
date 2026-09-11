using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

// SPT models a prefab path as a type called Path, which collides with System.IO.Path.
using IOPath = System.IO.Path;

namespace AshleySchaeffer.Tests;

/// <summary>
/// Runs the real <see cref="AshleySchaefferLoader"/> against a <see cref="FakeDatabase"/> and the
/// mod's real shipped JSON, so the assertions are about what actually lands in the database rather
/// than about the shape of the source.
/// </summary>
internal sealed class LoaderHarness
{
    internal FakeDatabase Db { get; }
    internal TraderConfig TraderConfig { get; } = new() { UpdateTime = [], Fence = null! };

    internal RagfairConfig RagfairConfig { get; } = new()
    {
        Traders = [],
        RunIntervalValues = null!,
        Sell = null!,
        Dynamic = null!,
        TieredFlea = null!,
    };

    /// <summary>No season table, so GetCurrentSeason returns -1 and nothing is season-filtered.</summary>
    internal WeatherConfig WeatherConfig { get; } = new() { Weather = null!, SeasonDates = [] };
    internal ModConfig Config { get; }
    internal MongoId AshleyId { get; }

    internal List<string> Warnings { get; } = [];
    internal List<string> Infos { get; } = [];

    private LoaderHarness(ModConfig config, FakeDatabase db, MongoId ashleyId)
    {
        Config = config;
        Db = db;
        AshleyId = ashleyId;
    }

    /// <summary>
    /// Writes the config under test into the output folder the loader reads from, and builds a
    /// database seeded from the real itemData.json.
    /// </summary>
    /// <remarks>
    /// The loader resolves its own folder through <c>ModHelper.GetAbsolutePathToModFolder</c>, which
    /// is not virtual, so there is nothing to stub: it will read from wherever AshleySchaeffer.dll
    /// sits. Under the test runner that is the test output folder, and the build already copies the
    /// whole mod/ payload there - so the tests exercise the real ModHelper against the real files.
    /// Rewriting config.json in place is what makes these tests single-threaded.
    /// </remarks>
    internal static LoaderHarness Create(Action<ModConfig>? tweak = null)
    {
        var folder = IOPath.GetDirectoryName(typeof(AshleySchaefferLoader).Assembly.Location)!;
        var configPath = IOPath.Combine(folder, "config.json");

        var config = ModPayload.ShippedConfig();
        tweak?.Invoke(config);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));

        var itemData = ModPayload.ItemData;
        var baseIds = itemData.Gear!.Values.Select(g => g.BaseItemID);
        var handsIds = itemData.Tops!.Values.Select(t => t.HandsBaseID);

        return new LoaderHarness(config, new FakeDatabase(baseIds, handsIds), ModPayload.TraderBase.Id);
    }

    internal async Task RunAsync()
    {
        var fileUtil = new FileUtil();
        var jsonUtil = new JsonUtil([new SPTarkov.Server.Core.Utils.Json.SptJsonConverterRegistrator()]);

        var loader = new AshleySchaefferLoader(
            new ModHelper(fileUtil, jsonUtil),
            new CapturingLogger(Warnings, Infos),
            new ImageRouter(fileUtil, new SPTarkov.Server.Core.Services.Image.ImageRouterService(), new HttpFileUtil(null!)),
            new TimeUtil(),
            new SPTarkov.Server.Core.Utils.Cloners.FastCloner(),
            TraderConfig,
            RagfairConfig,
            WeatherConfig,
            Db.Templates,
            Db.Locales,
            Db.Bots,
            Db.Traders,
            Db.Globals);

        await loader.OnLoadAsync(CancellationToken.None);
    }

    private sealed class CapturingLogger(List<string> warnings, List<string> infos)
        : ISptLogger<AshleySchaefferLoader>
    {
        public void Warning(string data, Exception? ex = null) => warnings.Add(data);
        public void Info(string data, Exception? ex = null) => infos.Add(data);
        public void Error(string data, Exception? ex = null) { }
        public void Critical(string data, Exception? ex = null) { }
        public void Debug(string data, Exception? ex = null) { }
        public void Success(string data, Exception? ex = null) { }
        public void Log(LogLevel level, string data, Color? textColor = null, Color? backgroundColor = null, Exception? ex = null) { }
        public void LogWithColor(string data, Color? textColor = null, Color? backgroundColor = null, Exception? ex = null) { }
        public bool IsLogEnabled(LogLevel level) => true;
    }
}
