using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace Venom.Client
{
    // Stage-specific non-verbal breathing behavior.
    // Both symptom stages use Tarkov's native OnBreath trigger so the
    // character breathes instead of playing spoken/hurt voice lines.
    [HarmonyPatch]
    internal static class VenomNauseousBreathFix
    {
        private static readonly FieldInfo PlayerField =
            AccessTools.Field(typeof(VenomInfectionEffect), "_player");

        private static readonly FieldInfo HealthControllerField =
            AccessTools.Field(typeof(VenomInfectionEffect), "_healthController");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(VenomInfectionEffect),
                "PlayNearDeathBreathing");
        }

        [HarmonyPrefix]
        private static bool Prefix(VenomInfectionEffect __instance)
        {
            Player player = PlayerField == null
                ? null
                : PlayerField.GetValue(__instance) as Player;

            ActiveHealthController healthController = HealthControllerField == null
                ? null
                : HealthControllerField.GetValue(__instance) as ActiveHealthController;

            if (player == null ||
                healthController == null ||
                !healthController.IsAlive)
            {
                return false;
            }

            PlayBreath(player);
            return false;
        }

        internal static void PlayBreath(Player player)
        {
            if (player == null) return;

            try
            {
                player.Say(
                    EPhraseTrigger.OnBreath,
                    true,
                    0f,
                    (ETagStatus)0,
                    100,
                    false);
            }
            catch (System.Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "[Venom] OnBreath trigger failed: " +
                        exception.Message);
                }
            }
        }
    }

    // TickDying does not schedule voice/breath lines by itself, so play the
    // same native breathing trigger on Venom's existing randomized cadence.
    [HarmonyPatch]
    internal static class VenomDyingBreathFix
    {
        private static readonly FieldInfo PlayerField =
            AccessTools.Field(typeof(VenomInfectionEffect), "_player");

        private static readonly FieldInfo HealthControllerField =
            AccessTools.Field(typeof(VenomInfectionEffect), "_healthController");

        private static readonly FieldInfo NextBreathingAtField =
            AccessTools.Field(typeof(VenomInfectionEffect), "_nextBreathingAt");

        private static readonly FieldInfo DyingStartedAtField =
            AccessTools.Field(typeof(VenomInfectionEffect), "_dyingStartedAt");

        private static readonly Dictionary<int, float> LastDyingStart =
            new Dictionary<int, float>();

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(VenomInfectionEffect),
                "TickDying");
        }

        [HarmonyPrefix]
        private static void Prefix(VenomInfectionEffect __instance)
        {
            if (__instance == null ||
                PlayerField == null ||
                HealthControllerField == null ||
                NextBreathingAtField == null ||
                DyingStartedAtField == null)
            {
                return;
            }

            Player player = PlayerField.GetValue(__instance) as Player;
            ActiveHealthController healthController =
                HealthControllerField.GetValue(__instance) as ActiveHealthController;

            if (player == null ||
                healthController == null ||
                !healthController.IsAlive)
            {
                return;
            }

            float dyingStartedAt = (float)DyingStartedAtField.GetValue(__instance);
            float nextBreathingAt = (float)NextBreathingAtField.GetValue(__instance);
            int instanceId = __instance.GetInstanceID();

            float previousStart;
            bool newDyingStage =
                !LastDyingStart.TryGetValue(instanceId, out previousStart) ||
                Mathf.Abs(previousStart - dyingStartedAt) > 0.01f;

            if (newDyingStage)
            {
                LastDyingStart[instanceId] = dyingStartedAt;
                VenomNauseousBreathFix.PlayBreath(player);
                ScheduleNext(__instance);
                return;
            }

            if (Time.time >= nextBreathingAt)
            {
                VenomNauseousBreathFix.PlayBreath(player);
                ScheduleNext(__instance);
            }
        }

        private static void ScheduleNext(VenomInfectionEffect instance)
        {
            NextBreathingAtField.SetValue(
                instance,
                Time.time + UnityEngine.Random.Range(
                    VenomConstants.BreathingMinInterval,
                    VenomConstants.BreathingMaxInterval));
        }
    }
}
