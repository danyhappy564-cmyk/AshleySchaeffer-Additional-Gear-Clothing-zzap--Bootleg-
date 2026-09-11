namespace AshleySchaeffer;

/// <summary>
/// Shape of <c>config.json</c>.
/// </summary>
/// <remarks>
/// Deliberately kept to the seven switches the 4.0 build actually read. The shipped
/// <c>config.json</c> also carries <c>pmcsGearWeightMultiplier</c> and <c>sellClothingOnRagman</c>,
/// and <c>itemData.json</c> carries a per-gear <c>weight</c>, but no released build of this mod
/// ever read any of the three - they deserialise into nothing and are ignored, exactly as before.
/// </remarks>
public class ModConfig
{
    public bool sellGearOnFlea { get; set; }

    public bool allGearAvailableOnFlea { get; set; }

    public bool traderEnabled { get; set; }

    public bool botsUseGear { get; set; }

    public bool pmcsUseFactionedGearOnly { get; set; }

    public bool botsUseClothing { get; set; }

    public bool unlockOthersFactionsClothing { get; set; }
}
