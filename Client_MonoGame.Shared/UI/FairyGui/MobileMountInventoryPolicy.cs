using System;

namespace MonoShare;

/// <summary>
/// Inventory destination policy for unloading a mount accessory.  Inventory
/// slots below BeltIdx are the hotbar and are intentionally excluded.
/// </summary>
internal static class MobileMountInventoryPolicy
{
    internal static int FindFirstEmptyPackageSlot(UserItem[] inventory, int beltIdx)
    {
        if (inventory == null)
            return -1;

        int firstPackageSlot = Math.Clamp(beltIdx, 0, inventory.Length);
        for (int i = firstPackageSlot; i < inventory.Length; i++)
        {
            if (inventory[i] == null)
                return i;
        }

        return -1;
    }
}
