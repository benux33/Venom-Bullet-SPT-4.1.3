using EFT;
using EFT.HealthSystem;
using HarmonyLib;

namespace Venom.Client
{
    [HarmonyPatch(typeof(ActiveHealthController.MedEffect), nameof(ActiveHealthController.MedEffect.Residue))]
    internal static class CureItemPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ActiveHealthController.MedEffect __instance, bool ____interrupted)
        {
            try
            {
                if (__instance == null || ____interrupted) return;
                if (__instance.MedItem?.TemplateId != VenomConstants.AugmentinTemplateId) return;
                ActiveHealthController controller = __instance.HealthController;
                Player player = controller == null ? null : controller.Player;
                if (player == null) return;
                VenomInfectionEffect infection = player.GetComponent<VenomInfectionEffect>();
                if (infection != null && infection.IsInfected) infection.Cure();
            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogWarning("[Venom] Augmentin completion hook failed: " + exception);
            }
        }
    }
}
