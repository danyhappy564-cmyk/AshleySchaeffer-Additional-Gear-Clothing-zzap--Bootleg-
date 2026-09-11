using SPTarkov.Server.Core.Models.Common;

namespace AshleySchaeffer;

/// <summary>
/// One entry under <c>Tops</c> in <c>db/items/itemData.json</c>. A top also owns the hands mesh,
/// because EFT models sleeves as part of the hands rather than the body.
/// </summary>
public class ClothingItemTop : ClothingItem
{
    public MongoId HandsId { get; set; }

    /// <summary>Vanilla hands this one is cloned from, and the fallback when there is no custom mesh.</summary>
    public MongoId HandsBaseID { get; set; }

    public string? HandsBundlePath { get; set; }
}
