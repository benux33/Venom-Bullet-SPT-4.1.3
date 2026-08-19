using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Venom.Client
{
    internal enum VenomStage { None = 0, InfectedWound = 1, Nauseous = 2, Dying = 3 }

    internal sealed class VenomPresentationState
    {
        internal EBodyPart WoundBodyPart;
        internal VenomStage Stage;
    }

    internal static class VenomStatusRegistry
    {
        private static readonly Dictionary<ActiveHealthController, VenomPresentationState> States = new Dictionary<ActiveHealthController, VenomPresentationState>();

        internal static void Register(ActiveHealthController controller, EBodyPart woundBodyPart, VenomStage stage)
        {
            if (controller == null) return;
            VenomPresentationState state;
            if (!States.TryGetValue(controller, out state)) { state = new VenomPresentationState(); States.Add(controller, state); }
            state.WoundBodyPart = woundBodyPart;
            state.Stage = stage;
        }

        internal static void SetStage(ActiveHealthController controller, VenomStage stage)
        {
            VenomPresentationState state;
            if (controller != null && States.TryGetValue(controller, out state)) state.Stage = stage;
        }

        internal static bool IsWoundActive(IHealthController healthController, EBodyPart bodyPart)
        {
            VenomPresentationState state;
            return TryGetLiveState(healthController, out state) && state.Stage != VenomStage.None && state.WoundBodyPart == bodyPart;
        }

        internal static bool IsNauseousActive(IHealthController healthController, EBodyPart bodyPart)
        {
            VenomPresentationState state;
            return bodyPart == EBodyPart.Head && TryGetLiveState(healthController, out state) && state.Stage == VenomStage.Nauseous;
        }

        internal static bool IsDyingActive(IHealthController healthController, EBodyPart bodyPart)
        {
            VenomPresentationState state;
            return bodyPart == EBodyPart.Head && TryGetLiveState(healthController, out state) && state.Stage == VenomStage.Dying;
        }

        internal static void Remove(ActiveHealthController controller) { if (controller != null) States.Remove(controller); }

        private static bool TryGetLiveState(IHealthController healthController, out VenomPresentationState state)
        {
            ActiveHealthController active = healthController as ActiveHealthController;
            if (active == null || !States.TryGetValue(active, out state)) { state = null; return false; }
            if (!active.IsAlive) { States.Remove(active); state = null; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(EffectsPanel), "Show", new[] { typeof(IHealthController), typeof(EBodyPart), typeof(SimpleTooltip) })]
    internal static class VenomEffectsPanelPatch
    {
        private static void Postfix(EffectsPanel __instance, IHealthController healthController, EBodyPart bodyPart, SimpleTooltip tooltip)
        {
            VenomEffectIconView view = __instance.GetComponent<VenomEffectIconView>();
            if (view == null) view = __instance.gameObject.AddComponent<VenomEffectIconView>();
            view.Bind(healthController, bodyPart, __instance._effectIconTemplate, tooltip);
        }
    }

    internal sealed class VenomEffectIconView : MonoBehaviour
    {
        private IHealthController _healthController;
        private EBodyPart _bodyPart;
        private SimpleTooltip _tooltip;
        private GameObject _woundIconObject;
        private GameObject _nauseousIconObject;
        private GameObject _dyingIconObject;
        private int _hoveredIcon;

        internal void Bind(IHealthController healthController, EBodyPart bodyPart, EffectIcon template, SimpleTooltip tooltip)
        {
            if ((_healthController != null && _healthController != healthController) || _bodyPart != bodyPart) ClearHover();
            _healthController = healthController;
            _bodyPart = bodyPart;
            _tooltip = tooltip;
            if (_woundIconObject == null)
            {
                _woundIconObject = CreateIcon(template, "Venom Infected Wound effect icon", VenomAssets.InfectedWoundStatusSprite, 1);
                _nauseousIconObject = CreateIcon(template, "Venom Nauseous effect icon", VenomAssets.NauseousStatusSprite, 2);
                _dyingIconObject = CreateIcon(template, "Venom Dying effect icon", VenomAssets.DyingStatusSprite, 3);
                ConfigureHover(_woundIconObject, 1);
                ConfigureHover(_nauseousIconObject, 2);
                ConfigureHover(_dyingIconObject, 3);
            }
            UpdateVisibility();
        }

        private void Update() { UpdateVisibility(); }

        private void UpdateVisibility()
        {
            if (_woundIconObject == null) return;
            bool wound = VenomStatusRegistry.IsWoundActive(_healthController, _bodyPart);
            bool nauseous = VenomStatusRegistry.IsNauseousActive(_healthController, _bodyPart);
            bool dying = VenomStatusRegistry.IsDyingActive(_healthController, _bodyPart);
            if ((_hoveredIcon == 1 && !wound) || (_hoveredIcon == 2 && !nauseous) || (_hoveredIcon == 3 && !dying)) ClearHover();
            _woundIconObject.SetActive(wound);
            _nauseousIconObject.SetActive(nauseous);
            _dyingIconObject.SetActive(dying);
        }

        private void ConfigureHover(GameObject iconObject, int iconKind)
        {
            HoverTrigger hover = iconObject.AddComponent<HoverTrigger>();
            hover.Init(ignored => BeginHover(iconKind), ignored => EndHover(iconKind));
        }

        private void BeginHover(int iconKind)
        {
            if (!IsIconActive(iconKind)) return;
            _hoveredIcon = iconKind;
            if (_tooltip != null) _tooltip.Show(GetTooltipText(iconKind), null, 0f, null);
        }

        private void EndHover(int iconKind) { if (_hoveredIcon == iconKind) ClearHover(); }

        private bool IsIconActive(int iconKind)
        {
            if (iconKind == 1) return VenomStatusRegistry.IsWoundActive(_healthController, _bodyPart);
            if (iconKind == 2) return VenomStatusRegistry.IsNauseousActive(_healthController, _bodyPart);
            return VenomStatusRegistry.IsDyingActive(_healthController, _bodyPart);
        }

        private static string GetTooltipText(int iconKind)
        {
            if (iconKind == 1) return "Infected Wound [?]";
            if (iconKind == 2) return "You feel nauseous [?]";
            return "You feel like you are dying [?]";
        }

        private void ClearHover()
        {
            if (_hoveredIcon == 0) return;
            _hoveredIcon = 0;
            if (_tooltip != null) _tooltip.Close();
        }

        private GameObject CreateIcon(EffectIcon template, string name, Sprite sprite, int siblingOffset)
        {
            Transform parent = template != null && template.transform.parent != null ? template.transform.parent : transform;
            GameObject iconObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            iconObject.layer = parent.gameObject.layer;
            iconObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)iconObject.transform;
            rect.sizeDelta = new Vector2(26f, 26f);
            LayoutElement layout = iconObject.GetComponent<LayoutElement>();
            layout.minWidth = 24f; layout.minHeight = 24f; layout.preferredWidth = 26f; layout.preferredHeight = 26f;
            Image image = iconObject.GetComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = true;
            if (template != null) iconObject.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + siblingOffset);
            iconObject.SetActive(false);
            return iconObject;
        }

        private void OnDestroy()
        {
            ClearHover();
            if (_woundIconObject != null) Destroy(_woundIconObject);
            if (_nauseousIconObject != null) Destroy(_nauseousIconObject);
            if (_dyingIconObject != null) Destroy(_dyingIconObject);
        }
    }
}
