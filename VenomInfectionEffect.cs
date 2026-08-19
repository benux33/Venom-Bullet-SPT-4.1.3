using System;
using System.Collections;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace Venom.Client
{
    [HarmonyPatch(typeof(ActiveHealthController), "ApplyDamage", new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) })]
    internal static class VenomImpactPatch
    {
        private static void Postfix(ActiveHealthController __instance, EBodyPart bodyPart, float damage, DamageInfo damageInfo)
        {
            bool armourStoppedProjectile = damageInfo.Blunt ||
                                           (damageInfo.BlockedBy.HasValue && !damageInfo.Penetrated);
            if (__instance == null ||
                damageInfo.SourceId != VenomConstants.TemplateId ||
                damageInfo.DamageType != EDamageType.Bullet ||
                damage <= 0f ||
                !__instance.IsAlive)
            {
                return;
            }

            if (bodyPart == EBodyPart.Head || armourStoppedProjectile) return;
            VenomInfectionEffect.Begin(__instance, bodyPart);
        }
    }

    // Prevent the infected character from immediately toggling back to stand
    // during Venom's final crawl-only phase. Venom's own call to ToggleProne()
    // is still allowed while the character is not yet prone.
    [HarmonyPatch(typeof(Player), nameof(Player.ToggleProne))]
    internal static class VenomForcedProneTogglePatch
    {
        private static bool Prefix(Player __instance)
        {
            if (__instance == null) return true;
            VenomInfectionEffect infection = __instance.GetComponent<VenomInfectionEffect>();
            if (infection == null || !infection.ShouldKeepProne) return true;

            try
            {
                if (__instance.MovementContext != null && __instance.MovementContext.IsInPronePose)
                {
                    return false;
                }
            }
            catch
            {
                // If Tarkov is changing movement state during a transition,
                // allow its own input path rather than trapping a bad state.
            }

            return true;
        }
    }

    internal sealed class VenomInfectionEffect : MonoBehaviour
    {
        private Player _player;
        private ActiveHealthController _healthController;
        private EBodyPart _woundBodyPart;
        private VenomStage _stage;
        private float _stageEndsAt;
        private float _nextBreathingAt;
        private bool _forcedProne;
        private bool _curingOrDead;
        private bool _venomDisabledSprint;
        private bool _staminaSampleValid;
        private float _lastLegStamina;
        private float _dyingStartedAt;
        private float _nextProneRetryAt;
        private ActiveHealthController.TunnelVision _nativeTunnelVision;

        internal bool IsInfected
        {
            get { return !_curingOrDead && _stage != VenomStage.None; }
        }

        internal bool ShouldKeepProne
        {
            get { return IsInfected && _stage == VenomStage.Dying && _forcedProne; }
        }

        internal static void Begin(ActiveHealthController healthController, EBodyPart woundBodyPart)
        {
            Player player = healthController == null ? null : healthController.Player;
            if (player == null) return;

            VenomInfectionEffect existing = player.GetComponent<VenomInfectionEffect>();
            if (existing != null && existing.IsInfected) return;

            VenomInfectionEffect infection = existing != null
                ? existing
                : player.gameObject.AddComponent<VenomInfectionEffect>();

            infection.RemoveNativeTunnelVision();
            infection.enabled = true;
            infection._player = player;
            infection._healthController = healthController;
            infection._woundBodyPart = woundBodyPart;
            infection._curingOrDead = false;
            infection._forcedProne = false;
            infection._venomDisabledSprint = false;
            infection._staminaSampleValid = false;
            infection._nextBreathingAt = 0f;
            infection._dyingStartedAt = 0f;
            infection._nextProneRetryAt = 0f;

            infection.EnterStage(
                VenomStage.InfectedWound,
                UnityEngine.Random.Range(
                    VenomConstants.InfectedWoundMinSeconds,
                    VenomConstants.InfectedWoundMaxSeconds));

            VenomStatusRegistry.Register(
                healthController,
                woundBodyPart,
                VenomStage.InfectedWound);

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "[Venom] Infection started on " +
                    (player.Profile == null ? "unknown" : player.Profile.Nickname) +
                    " at " + woundBodyPart +
                    ". Hidden stage-1 roll: " +
                    infection.SecondsRemaining.ToString("0.0") + "s.");
            }
        }

        internal void Cure()
        {
            if (!IsInfected) return;
            _curingOrDead = true;
            _stage = VenomStage.None;
            RemoveNativeTunnelVision();
            RestoreMovement();
            ClearPresentation();
            enabled = false;
        }

        private float SecondsRemaining
        {
            get { return Mathf.Max(0f, _stageEndsAt - Time.time); }
        }

        private void EnterStage(VenomStage stage, float duration)
        {
            _stage = stage;
            _stageEndsAt = Time.time + duration;
            _forcedProne = false;
            _staminaSampleValid = false;
            _nextProneRetryAt = 0f;
            _dyingStartedAt = stage == VenomStage.Dying ? Time.time : 0f;

            if (_healthController != null)
            {
                VenomStatusRegistry.SetStage(_healthController, stage);
            }

            if (stage == VenomStage.Dying)
            {
                StartNativeTunnelVision(duration);
            }
            else
            {
                RemoveNativeTunnelVision();
            }
        }

        private void Update()
        {
            if (_curingOrDead) return;

            if (_player == null || _healthController == null || !_healthController.IsAlive)
            {
                _curingOrDead = true;
                RemoveNativeTunnelVision();
                ClearPresentation();
                Destroy(this);
                return;
            }

            if (_stage == VenomStage.InfectedWound) TickInfectedWound();
            else if (_stage == VenomStage.Nauseous) TickNauseous();
            else if (_stage == VenomStage.Dying) TickDying();
        }

        private void TickInfectedWound()
        {
            if (Time.time < _stageEndsAt) return;

            float duration = UnityEngine.Random.Range(
                VenomConstants.NauseousMinSeconds,
                VenomConstants.NauseousMaxSeconds);
            EnterStage(VenomStage.Nauseous, duration);

            _nextBreathingAt = Time.time;
            PlayNearDeathBreathing();
            ScheduleNextBreathing();
            VenomScreenEffects.SetTremor(_player, 1f);
        }

        private void TickNauseous()
        {
            VenomScreenEffects.SetTremor(_player, 1f);

            if (Time.time >= _nextBreathingAt)
            {
                PlayNearDeathBreathing();
                ScheduleNextBreathing();
            }

            if (Time.time < _stageEndsAt) return;

            VenomScreenEffects.SetTremor(_player, 0f);
            float duration = UnityEngine.Random.Range(
                VenomConstants.DyingMinSeconds,
                VenomConstants.DyingMaxSeconds);
            EnterStage(VenomStage.Dying, duration);
        }

        private void TickDying()
        {
            float progress = Mathf.Clamp01(
                (Time.time - _dyingStartedAt) /
                Mathf.Max(0.001f, _stageEndsAt - _dyingStartedAt));

            ApplyDyingMovementPenalties();
            UpdateNativeTunnelVision(progress);

            if (SecondsRemaining <= VenomConstants.ForcedProneSeconds)
            {
                _forcedProne = true;
                EnforceProne();
            }

            if (Time.time >= _stageEndsAt)
            {
                KillByVenom();
            }
        }

        private void ApplyDyingMovementPenalties()
        {
            try
            {
                if (_player.Physical != null)
                {
                    _player.Physical.HandsStamina.Current = 0f;
                }

                _player.MovementContext.EnableSprint(false);
                _venomDisabledSprint = true;
                SlowLegStaminaRecovery();

                if (_forcedProne)
                {
                    EnforceProne();
                }
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "[Venom] Dying movement penalty failed: " + exception);
                }
            }
        }

        private void SlowLegStaminaRecovery()
        {
            object physical = _player.Physical;
            object stamina = ReflectionHelper.GetMemberValue(physical, "Stamina");
            if (stamina == null) return;

            float current;
            if (!ReflectionHelper.TryReadFloat(stamina, "Current", out current)) return;

            if (!_staminaSampleValid)
            {
                _lastLegStamina = current;
                _staminaSampleValid = true;
                return;
            }

            if (current > _lastLegStamina)
            {
                float maximum = _lastLegStamina +
                                VenomConstants.MaxLegStaminaRecoveryPerSecond *
                                Time.deltaTime;
                if (current > maximum &&
                    ReflectionHelper.TryWriteFloat(stamina, "Current", maximum))
                {
                    current = maximum;
                }
            }

            _lastLegStamina = current;
        }

        private void EnforceProne()
        {
            if (_player == null || _player.MovementContext == null) return;

            try
            {
                _player.MovementContext.EnableSprint(false);
                _venomDisabledSprint = true;

                // Use Tarkov's real prone input path. ToggleProne() is the same
                // method EFT calls for ECommand.ToggleProne, so the movement
                // state, pose and crawl animator stay synchronized.
                string stateName = _player.MovementContext.CurrentState == null
                    ? string.Empty
                    : _player.MovementContext.CurrentState.GetType().Name;

                bool proneOrTransition =
                    _player.MovementContext.IsInPronePose ||
                    stateName.IndexOf("Prone", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!proneOrTransition &&
                    Time.time >= _nextProneRetryAt &&
                    _player.MovementContext.CanProne)
                {
                    _nextProneRetryAt = Time.time + 0.75f;
                    _player.ToggleProne();
                }
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "[Venom] Forced-prone enforcement failed: " + exception);
                }
            }
        }

        private void RestoreMovement()
        {
            if (_player == null) return;

            if (_venomDisabledSprint)
            {
                try
                {
                    _player.MovementContext.EnableSprint(true);
                }
                catch (Exception exception)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning(
                            "[Venom] Sprint unlock after cure failed: " +
                            exception.Message);
                    }
                }
            }

            _venomDisabledSprint = false;
            _forcedProne = false;
            _staminaSampleValid = false;
            _nextProneRetryAt = 0f;
            VenomScreenEffects.SetTremor(_player, 0f);
        }

        private void StartNativeTunnelVision(float duration)
        {
            RemoveNativeTunnelVision();

            if (_player == null ||
                !_player.IsYourPlayer ||
                _healthController == null ||
                !_healthController.IsAlive)
            {
                return;
            }

            try
            {
                _nativeTunnelVision =
                    _healthController.AddEffect<ActiveHealthController.TunnelVision>(
                        EBodyPart.Head,
                        0f,
                        duration + 1f,
                        0f,
                        0.20f,
                        null);
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "[Venom] Tarkov native tunnel vision could not start: " +
                        exception);
                }
            }
        }

        private void UpdateNativeTunnelVision(float progress)
        {
            if (_nativeTunnelVision == null) return;

            try
            {
                // Keep EFT in control of the actual post-processing. Venom
                // only drives the native effect strength as the infection
                // worsens; there is no custom grayscale or fake vignette.
                _nativeTunnelVision.SetStrength(
                    Mathf.Lerp(0.20f, 1.00f, Mathf.SmoothStep(0f, 1f, progress)));
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "[Venom] Native tunnel vision strength update failed: " +
                        exception.Message);
                }
                RemoveNativeTunnelVision();
            }
        }

        private void RemoveNativeTunnelVision()
        {
            if (_nativeTunnelVision == null) return;

            try
            {
                _nativeTunnelVision.ForceRemove();
            }
            catch
            {
                // It may already have expired or been removed by EFT.
            }

            _nativeTunnelVision = null;
        }

        private void PlayNearDeathBreathing()
        {
            if (_player == null ||
                _healthController == null ||
                !_healthController.IsAlive)
            {
                return;
            }

            try
            {
                EPhraseTrigger phrase = VoicePhraseHelper.HasPhrase(
                    _player,
                    EPhraseTrigger.HurtNearDeath)
                    ? EPhraseTrigger.HurtNearDeath
                    : EPhraseTrigger.HurtHeavy;

                _player.Say(
                    phrase,
                    true,
                    0f,
                    (ETagStatus)0,
                    100,
                    false);
            }
            catch (Exception exception)
            {
                // If a particular voice has an unusual phrase bank, make one
                // final attempt with the generic hurt-breath trigger.
                try
                {
                    _player.Say(
                        EPhraseTrigger.HurtHeavy,
                        true,
                        0f,
                        (ETagStatus)0,
                        100,
                        false);
                }
                catch
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning(
                            "[Venom] Near-death breathing failed: " +
                            exception.Message);
                    }
                }
            }
        }

        private void ScheduleNextBreathing()
        {
            _nextBreathingAt = Time.time + UnityEngine.Random.Range(
                VenomConstants.BreathingMinInterval,
                VenomConstants.BreathingMaxInterval);
        }

        private void KillByVenom()
        {
            if (_curingOrDead ||
                _healthController == null ||
                !_healthController.IsAlive)
            {
                return;
            }

            _curingOrDead = true;
            RemoveNativeTunnelVision();

            try
            {
                DamageInfo lethal = new DamageInfo
                {
                    DamageType = EDamageType.Bullet,
                    Damage = 9999f,
                    SourceId = VenomConstants.TemplateId,
                    DelayedDamage = true,
                    Direction = Vector3.down,
                };

                _healthController.ChangeHealth(EBodyPart.Chest, -9999f, lethal);
                ValueStruct health = _healthController.GetBodyPartHealth(
                    EBodyPart.Chest,
                    false);

                if (health.Current <= 0f &&
                    !_healthController.BodyState[EBodyPart.Chest].IsDestroyed)
                {
                    _healthController.DestroyBodyPart(
                        EBodyPart.Chest,
                        EDamageType.Bullet);
                    _healthController.TryToKillAfterDestroyPart(
                        EBodyPart.Chest,
                        EDamageType.Bullet);
                }
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogError(
                        "[Venom] Final collapse/death failed: " + exception);
                }
            }

            ClearPresentation();
            Destroy(this);
        }

        private void ClearPresentation()
        {
            RemoveNativeTunnelVision();
            VenomStatusRegistry.Remove(_healthController);
            if (_player != null && _player.IsYourPlayer)
            {
                VenomScreenEffects.ResetAll();
            }
        }

        private void OnDestroy()
        {
            RemoveNativeTunnelVision();
            VenomStatusRegistry.Remove(_healthController);
            if (_player != null && _player.IsYourPlayer)
            {
                VenomScreenEffects.ResetAll();
            }
        }
    }

    internal static class VoicePhraseHelper
    {
        private static readonly string[] AvailabilityMethodNames =
        {
            "HasPhrase",
            "IsPhraseAvailable",
        };

        internal static bool HasPhrase(Player player, EPhraseTrigger phrase)
        {
            if (player == null) return false;

            object speaker = null;
            try { speaker = player.Speaker; }
            catch { }
            if (speaker == null) return true;

            bool available;
            if (TryCheckObject(speaker, phrase, out available)) return available;

            // EFT voice implementations move their phrase bank between helper
            // objects across versions. Check one level of likely phrase/voice
            // members without hard-coding an obfuscated concrete class.
            Type type = speaker.GetType();
            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!LooksLikeVoiceMember(field.Name)) continue;
                object value = null;
                try { value = field.GetValue(speaker); }
                catch { }
                if (value != null && TryCheckObject(value, phrase, out available))
                {
                    return available;
                }
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanRead ||
                    property.GetIndexParameters().Length != 0 ||
                    !LooksLikeVoiceMember(property.Name))
                {
                    continue;
                }

                object value = null;
                try { value = property.GetValue(speaker, null); }
                catch { }
                if (value != null && TryCheckObject(value, phrase, out available))
                {
                    return available;
                }
            }

            // If this EFT voice type exposes no availability API, prefer the
            // requested near-death trigger. Player.Say is still wrapped by the
            // HurtHeavy fallback in PlayNearDeathBreathing().
            return true;
        }

        private static bool TryCheckObject(
            object instance,
            EPhraseTrigger phrase,
            out bool available)
        {
            available = false;
            if (instance == null) return false;

            Type type = instance.GetType();
            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                bool nameMatch = false;
                for (int index = 0; index < AvailabilityMethodNames.Length; index++)
                {
                    if (string.Equals(
                        method.Name,
                        AvailabilityMethodNames[index],
                        StringComparison.OrdinalIgnoreCase))
                    {
                        nameMatch = true;
                        break;
                    }
                }

                if (!nameMatch || method.ReturnType != typeof(bool)) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1) continue;

                object argument;
                Type parameterType = parameters[0].ParameterType;
                if (parameterType == typeof(EPhraseTrigger))
                {
                    argument = phrase;
                }
                else if (parameterType.IsEnum)
                {
                    try
                    {
                        argument = Enum.ToObject(
                            parameterType,
                            Convert.ToInt32(phrase));
                    }
                    catch
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                try
                {
                    object result = method.Invoke(instance, new[] { argument });
                    if (result is bool)
                    {
                        available = (bool)result;
                        return true;
                    }
                }
                catch
                {
                    // Try another compatible availability method/member.
                }
            }

            // Also handle a directly exposed phrase collection.
            if (instance is IEnumerable enumerable)
            {
                try
                {
                    foreach (object entry in enumerable)
                    {
                        if (entry == null) continue;
                        if (entry is EPhraseTrigger &&
                            (EPhraseTrigger)entry == phrase)
                        {
                            available = true;
                            return true;
                        }
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool LooksLikeVoiceMember(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("phrase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("voice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("sound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal static class ReflectionHelper
    {
        internal static object GetMemberValue(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (field != null)
            {
                try { return field.GetValue(instance); }
                catch { return null; }
            }

            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (property != null && property.CanRead)
            {
                try { return property.GetValue(instance, null); }
                catch { return null; }
            }

            return null;
        }

        internal static bool TryReadFloat(
            object instance,
            string name,
            out float value)
        {
            value = 0f;
            object raw = GetMemberValue(instance, name);
            if (raw == null) return false;
            try
            {
                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryWriteFloat(
            object instance,
            string name,
            float value)
        {
            if (instance == null) return false;
            Type type = instance.GetType();
            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            FieldInfo field = type.GetField(name, flags);
            if (field != null && field.FieldType == typeof(float))
            {
                try
                {
                    field.SetValue(instance, value);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null &&
                property.CanWrite &&
                property.PropertyType == typeof(float))
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
