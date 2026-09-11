namespace AshleySchaeffer;

/// <summary>Shape of <c>db/items/itemData.json</c>.</summary>
public class CustomItemData
{
    public Dictionary<string, GearItem>? Gear { get; set; }

    public Dictionary<string, ClothingItemTop>? Tops { get; set; }

    public Dictionary<string, ClothingItem>? Bottoms { get; set; }
}
