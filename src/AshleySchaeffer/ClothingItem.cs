using SPTarkov.Server.Core.Models.Common;

namespace AshleySchaeffer;

/// <summary>One entry under <c>Bottoms</c> in <c>db/items/itemData.json</c>.</summary>
public class ClothingItem
{
    public string? name { get; set; }

    public string? displayName { get; set; }

    /// <summary>The customisation item (the mesh).</summary>
    public MongoId id { get; set; }

    /// <summary>The suite that wraps it, which is what the trader actually sells.</summary>
    public MongoId SuiteId { get; set; }

    public string? BundlePath { get; set; }

    public int LoyaltyLevel { get; set; }

    public int ProfileLevel { get; set; }

    public float Standing { get; set; }

    /// <summary>Price in roubles.</summary>
    public int Price { get; set; }

    public bool? enabled { get; set; }

    public List<int>? season { get; set; }

    /// <summary>"Bear" / "Usec"; empty or null means both.</summary>
    public List<string>? Side { get; set; }
}
