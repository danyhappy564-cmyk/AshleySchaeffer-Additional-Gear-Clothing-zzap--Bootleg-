using SPTarkov.Server.Core.Models.Common;

namespace AshleySchaeffer;

/// <summary>One entry under <c>Gear</c> in <c>db/items/itemData.json</c>.</summary>
public class GearItem
{
    /// <summary>Vanilla item this one is cloned from.</summary>
    public MongoId BaseItemID { get; set; }

    public string? BundlePath { get; set; }

    public MongoId id { get; set; }

    public string? name { get; set; }

    public bool enabled { get; set; }

    /// <summary>"Bear" / "Usec", or null for both. Only consulted when PMCs are faction-locked.</summary>
    public string? side { get; set; }

    /// <summary>Seasons this piece may show up in; null means all year.</summary>
    public List<int>? season { get; set; }

    /// <summary>Copy the base item into every slot filter and conflict list that accepts it.</summary>
    public bool? needsFilterUpdate { get; set; }
}
