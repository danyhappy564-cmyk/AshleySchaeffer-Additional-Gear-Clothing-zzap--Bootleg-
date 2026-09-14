using SPTarkov.Server.Core.Models.Spt.Mod;
using SemVerRange = SemanticVersioning.Range;
using SemVerVersion = SemanticVersioning.Version;

namespace AshleySchaeffer;

/// <summary>
/// Mod manifest the server reads before anything else is loaded.
/// </summary>
/// <remarks>
/// 4.1 replaced the <c>AbstractModMetadata</c> record with the <see cref="IModMetadata"/> interface,
/// so this is a plain class now. Two members also changed shape: <c>IsBundleMod</c> is gone (4.1
/// detects a bundle mod from the presence of bundles.json) and <c>HasPrepatcher</c> is new. Every
/// other value is carried over from the 4.0 build except <see cref="SptVersion"/>, which has to
/// move to 4.1, and <see cref="ModGuid"/>, which 4.1 validates and the 4.0 value fails.
/// </remarks>
public class ModMetadata : IModMetadata
{
    /// <summary>
    /// The 4.0 build used "com.AshleySchaefferBMW.Ashley Schaeffer Additional Gear and Clothing",
    /// which 4.1 rejects: ModValidator matches every guid against
    /// <c>^[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)*$</c> - letters, digits and hyphens, dot separated, and
    /// the 4.0 value carries five spaces. A failing guid is not a per-mod problem either: the
    /// validator sets errorsFound and returns an empty list, so one bad guid disables EVERY mod
    /// on the server.
    /// </summary>
    public string ModGuid { get; init; } =
        "com.AshleySchaefferBMW.AshleySchaefferAdditionalGearAndClothing";

    public string Name { get; init; } = "Ashley Schaeffer Additional Gear and Clothing";

    public string Author { get; init; } = "AshleySchaefferBMW";

    public List<string>? Contributors { get; init; }

    public SemVerVersion Version { get; init; } = new("4.1.3");

    public SemVerRange SptVersion { get; init; } = new("~4.1.0");

    public bool HasPrepatcher { get; init; }

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, SemVerRange>? ModDependencies { get; init; }

    public string? Url { get; init; }

    public string License { get; init; } = "MIT";
}
