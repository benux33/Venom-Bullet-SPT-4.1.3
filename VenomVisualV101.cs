using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Venom.Client
{
    // Final visual pass. The existing VenomVisuals class still does
    // the Dragon's-Breath-style model interception/private-material setup.
    // This patch runs after that pass and applies the requested brown
    // cartridge-case finish plus a black projectile-tip material.
    internal static class VenomVisualV101
    {
        internal static void Apply(Item item, GameObject itemObject)
        {
            if (item == null || itemObject == null ||
                item.TemplateId.ToString() != VenomConstants.TemplateId)
            {
                return;
            }

            Renderer[] renderers = itemObject.GetComponentsInChildren<Renderer>(true);
            int bodyRenderers = 0;
            int tipRenderers = 0;

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || renderer is ParticleSystemRenderer) continue;

                if (IsVenomTipRenderer(renderer))
                {
                    ApplyBlackTip(renderer);
                    tipRenderers++;
                    continue;
                }

                Material[] originals = renderer.sharedMaterials;
                if (originals == null || originals.Length == 0) continue;

                Material[] brown = new Material[originals.Length];
                for (int materialIndex = 0; materialIndex < originals.Length; materialIndex++)
                {
                    brown[materialIndex] = CreateBrownMaterial(originals[materialIndex]);
                }

                renderer.sharedMaterials = brown;
                bodyRenderers++;
            }

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "[Venom v1.0.2] Applied brown cartridge finish to " +
                    bodyRenderers + " renderer(s) and blackened " +
                    tipRenderers + " projectile-tip renderer(s).");
            }
        }

        private static bool IsVenomTipRenderer(Renderer renderer)
        {
            Transform current = renderer.transform;
            while (current != null)
            {
                string name = current.name ?? string.Empty;
                if (name.IndexOf("Venom", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("tip", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static Material CreateBrownMaterial(Material source)
        {
            if (source == null) return null;

            Material material = new Material(source);
            material.name = source.name + " (Venom v1.0.2 brown)";

            Texture2D brownTexture = VenomBrownTextureV102.Paint(source.mainTexture);
            if (brownTexture != null)
            {
                material.mainTexture = brownTexture;
                SetTexture(material, "_BaseMap", brownTexture);
                SetTexture(material, "_BaseColorMap", brownTexture);
                SetTexture(material, "_Albedo", brownTexture);
                SetColour(material, Color.white);
            }
            else
            {
                SetColour(material, new Color(0.34f, 0.18f, 0.08f, 1f));
            }

            SetFloat(material, "_Metallic", 0.30f);
            SetFloat(material, "_Glossiness", 0.34f);
            SetFloat(material, "_Smoothness", 0.34f);
            return material;
        }

        private static void ApplyBlackTip(Renderer renderer)
        {
            Material[] originals = renderer.sharedMaterials;
            int count = originals == null || originals.Length == 0 ? 1 : originals.Length;
            Material[] black = new Material[count];
            for (int i = 0; i < black.Length; i++)
            {
                black[i] = CreateBlackTipMaterial();
            }
            renderer.sharedMaterials = black;
        }

        private static Material CreateBlackTipMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            material.name = "Venom v1.0.2 black projectile tip";
            Color black = new Color(0.018f, 0.018f, 0.018f, 1f);
            material.color = black;
            SetColour(material, black);
            SetFloat(material, "_Metallic", 0.12f);
            SetFloat(material, "_Glossiness", 0.42f);
            SetFloat(material, "_Smoothness", 0.42f);
            return material;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property)) material.SetTexture(property, texture);
        }

        private static void SetColour(Material material, Color colour)
        {
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }

    internal static class VenomBrownTextureV102
    {
        private static readonly Dictionary<int, Texture2D> Cache =
            new Dictionary<int, Texture2D>();

        internal static Texture2D Paint(Texture source)
        {
            if (source == null) return null;

            int sourceId = source.GetInstanceID();
            Texture2D cached;
            if (Cache.TryGetValue(sourceId, out cached) && cached != null)
            {
                return cached;
            }

            Texture2D texture = ReadTexture(source);
            if (texture == null) return null;

            Color[] pixels = texture.GetPixels();
            for (int index = 0; index < pixels.Length; index++)
            {
                Color original = pixels[index];
                if (original.a <= 0.001f) continue;

                float luminance =
                    original.r * 0.2126f +
                    original.g * 0.7152f +
                    original.b * 0.0722f;
                float value = Mathf.Max(
                    original.r,
                    Mathf.Max(original.g, original.b));
                float shading = Mathf.Clamp01(luminance * 0.62f + value * 0.38f);
                float shade = Mathf.Lerp(
                    0.42f,
                    1.00f,
                    Mathf.SmoothStep(0f, 1f, shading));
                if (shading < 0.08f) shade *= 0.52f;

                // Warm, earthy cartridge brown while preserving the original
                // model's highlights and shadows.
                pixels[index] = new Color(
                    0.38f * shade,
                    0.20f * shade,
                    0.085f * shade,
                    original.a);
            }

            texture.name = source.name + " - Venom v1.0.2 brown cartridge";
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            Cache[sourceId] = texture;
            return texture;
        }

        private static Texture2D ReadTexture(Texture source)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                Texture2D copy = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    true);
                copy.ReadPixels(
                    new Rect(0, 0, source.width, source.height),
                    0,
                    0,
                    false);
                copy.Apply(false, false);
                copy.wrapMode = source.wrapMode;
                copy.filterMode = source.filterMode;
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    [HarmonyPatch]
    internal static class VenomV101SynchronousVisualPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ObjectsFactory)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.ReturnType == typeof(GameObject) &&
                    parameters.Length > 0 &&
                    parameters[0].ParameterType == typeof(Item))
                {
                    yield return method;
                }
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Item __0, GameObject __result)
        {
            VenomVisualV101.Apply(__0, __result);
        }
    }

    [HarmonyPatch]
    internal static class VenomV101AsynchronousVisualPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ObjectsFactory)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.ReturnType == typeof(Task<GameObject>) &&
                    parameters.Length > 0 &&
                    parameters[0].ParameterType == typeof(Item))
                {
                    yield return method;
                }
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Item __0, ref Task<GameObject> __result)
        {
            if (__0 == null ||
                __0.TemplateId.ToString() != VenomConstants.TemplateId ||
                __result == null)
            {
                return;
            }

            __result = ApplyWhenReady(__0, __result);
        }

        private static async Task<GameObject> ApplyWhenReady(
            Item item,
            Task<GameObject> itemTask)
        {
            GameObject itemObject = await itemTask;
            VenomVisualV101.Apply(item, itemObject);
            return itemObject;
        }
    }
}
