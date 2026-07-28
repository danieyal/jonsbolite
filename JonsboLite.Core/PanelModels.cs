namespace JonsboLite;

/// <summary>
/// Which USB devices are this panel. Protocol data, so it lives with the protocol rather
/// than being restated by each platform's discovery code.
///
/// From main/constant/index.js in the vendor app, where the three entries are commented
/// 公版 (reference), 奥斯艾 (Ocypus) and 锐拓 (Ruituo). Software treats them identically;
/// only the two display-mode bytes in the frame mean different things to their firmware.
/// </summary>
public static class PanelModels
{
    public const ushort VendorId = 0x5131;

    public static readonly ushort[] ProductIds = { 0x2007, 0x2009, 0x200A };

    public static bool IsPanel(ushort vendorId, ushort productId) =>
        vendorId == VendorId && Array.IndexOf(ProductIds, productId) >= 0;

    public static string Name(ushort productId) => productId switch
    {
        0x2007 => "reference (公版)",
        0x2009 => "Ocypus (奥斯艾)",
        0x200A => "Ruituo (锐拓)",
        _ => $"unknown 0x{productId:X4}",
    };
}
