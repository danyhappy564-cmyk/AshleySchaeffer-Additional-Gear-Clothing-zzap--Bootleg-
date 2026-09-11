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
/// other value is carried over verbatim from the 4.0 build except <see cref="SptVersion"/>, which
/// has to move to 4.1 for the server to accept the mod at all.
/// </remarks>
public class ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } =
        "com.AshleySchaefferBMW.Ashley Schaeffer Additional Gear and Clothing";

    public string Name { get; init; } = "Ashley Schaeffer Additional Gear and Clothing";

    public string Author { get; init; } = "AshleySchaefferBMW";

    public List<string>? Contributors { get; init; }

    public SemVerVersion Version { get; init; } = new("4.1.0");

    public SemVerRange SptVersion { get; init; } = new("~4.1.0");

    public bool HasPrepatcher { get; init; }

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, SemVerRange>? ModDependencies { get; init; }

    public string? Url { get; init; }

    public string License { get; init; } = "MIT";
}
