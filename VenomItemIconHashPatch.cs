using EFT.InventoryLogic;
using HarmonyLib;

namespace Venom.Client
{
    [HarmonyPatch(typeof(IconsHash), nameof(IconsHash.GetItemHash))]
    internal static class VenomItemIconHashPatch
    {
        // Force EFT to treat Venom's generated icon as a distinct visual revision
        // instead of reusing the vanilla 5.56x45 HP thumbnail cache entry.
        private const int VisualRevision = 0x564E0515;

        private static void Postfix(Item item, ref int __result)
        {
            if (item != null && item.TemplateId.ToString() == VenomConstants.TemplateId)
            {
                __result ^= VisualRevision;
            }
        }
    }
}
