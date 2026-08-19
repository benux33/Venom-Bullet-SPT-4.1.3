using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Venom.Client
{
    internal sealed class VenomVisualMarker : MonoBehaviour
    {
        private Renderer[] _renderers;
        private Material[][] _originalMaterials;
        private Material[][] _silverMaterials;
        private GameObject[] _overlays;
        private bool _initialized;
        private bool _showVenom;
        private float _nextInitializationAttempt;

        internal void Initialize(Renderer[] renderers)
        {
            List<Renderer> usableRenderers = new List<Renderer>();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer != null &&
                    !(renderer is ParticleSystemRenderer) &&
                    (renderer.transform == transform || renderer.transform.IsChildOf(transform)))
                {
                    usableRenderers.Add(renderer);
                }
            }

            if (usableRenderers.Count == 0)
            {
                return;
            }

            _renderers = usableRenderers.ToArray();
            _originalMaterials = new Material[_renderers.Length][];
            _silverMaterials = new Material[_renderers.Length][];

            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Material[] originals = _renderers[rendererIndex].sharedMaterials;
                Material[] silver = new Material[originals.Length];
                _originalMaterials[rendererIndex] = originals;

                for (int materialIndex = 0; materialIndex < originals.Length; materialIndex++)
                {
                    silver[materialIndex] = CreateSilverMaterial(originals[materialIndex]);
                }

                _silverMaterials[rendererIndex] = silver;
            }

            List<GameObject> overlays = new List<GameObject>();
            Renderer cartridgeRenderer = SelectHighestDetailRenderer(_renderers);
            if (cartridgeRenderer != null)
            {
                GameObject greenTip = VenomTipGeometry.Create(cartridgeRenderer);
                if (greenTip != null)
                {
                    overlays.Add(greenTip);
                }
            }

            _overlays = overlays.ToArray();
            _initialized = true;
            ApplyCurrentVisual();

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "[Venom] Recoloured " + _renderers.Length +
                    " renderer(s) using private silver materials and created " +
                    _overlays.Length + " bright-green tip overlay(s); vanilla 5.56 HP materials were left untouched.");
            }
        }

        private static Material CreateSilverMaterial(Material source)
        {
            if (source == null)
            {
                return null;
            }

            Material material = new Material(source);
            material.name = source.name + " (Venom silver cartridge)";
            Texture2D silverTexture = VenomSilverTexture.Paint(source.mainTexture);
            if (silverTexture != null)
            {
                material.mainTexture = silverTexture;
                SetTexture(material, "_BaseMap", silverTexture);
                SetTexture(material, "_BaseColorMap", silverTexture);
                SetTexture(material, "_Albedo", silverTexture);
            }

            SetColour(material, Color.white);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.55f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.48f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.48f);
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

        private Renderer SelectHighestDetailRenderer(Renderer[] renderers)
        {
            Renderer selected = null;
            int selectedScore = -1;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || renderer is ParticleSystemRenderer ||
                    (renderer.transform != transform && !renderer.transform.IsChildOf(transform)))
                {
                    continue;
                }

                Mesh mesh = GetMesh(renderer);
                if (mesh == null || mesh.vertexCount == 0) continue;
                string rendererName = renderer.name.ToLowerInvariant();
                int lodBonus = rendererName.Contains("lod0") || rendererName.Contains("lod_0") ? 10000000 : 0;
                int score = lodBonus + mesh.vertexCount;
                if (score > selectedScore)
                {
                    selected = renderer;
                    selectedScore = score;
                }
            }
            return selected;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null) return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        internal void ShowVenom()
        {
            _showVenom = true;
            ApplyCurrentVisual();
        }

        internal void ShowOriginalHp()
        {
            _showVenom = false;
            ApplyCurrentVisual();
        }

        private void ApplyCurrentVisual()
        {
            SetMaterials(_showVenom ? _silverMaterials : _originalMaterials);
            SetOverlays(_showVenom);
        }

        private void SetMaterials(Material[][] materialSets)
        {
            if (_renderers == null || materialSets == null) return;
            for (int index = 0; index < _renderers.Length; index++)
            {
                if (_renderers[index] != null) _renderers[index].sharedMaterials = materialSets[index];
            }
        }

        private void SetOverlays(bool visible)
        {
            if (_overlays == null) return;
            for (int i = 0; i < _overlays.Length; i++)
            {
                if (_overlays[i] != null) _overlays[i].SetActive(visible);
            }
        }

        private void Update()
        {
            if (!_initialized && Time.unscaledTime >= _nextInitializationAttempt)
            {
                _nextInitializationAttempt = Time.unscaledTime + 0.15f;
                Initialize(GetComponentsInChildren<Renderer>(true));
            }
        }
    }

    internal static class VenomSilverTexture
    {
        private static readonly Dictionary<int, Texture2D> Cache = new Dictionary<int, Texture2D>();

        internal static Texture2D Paint(Texture source)
        {
            if (source == null) return null;
            int sourceId = source.GetInstanceID();
            Texture2D cached;
            if (Cache.TryGetValue(sourceId, out cached) && cached != null) return cached;

            Texture2D texture = ReadTexture(source);
            if (texture == null) return null;

            Color[] pixels = texture.GetPixels();
            for (int index = 0; index < pixels.Length; index++)
            {
                Color original = pixels[index];
                if (original.a <= 0.001f) continue;

                float value = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
                float luminance = original.r * 0.2126f + original.g * 0.7152f + original.b * 0.0722f;
                float shading = Mathf.Clamp01(value * 0.70f + luminance * 0.30f);
                float silver = Mathf.Lerp(0.30f, 0.88f, Mathf.SmoothStep(0f, 1f, shading));
                if (shading < 0.09f) silver *= 0.42f;

                pixels[index] = new Color(
                    silver * 0.96f,
                    silver * 0.985f,
                    silver * 1.04f,
                    original.a);
            }

            texture.name = source.name + " - Venom silver cartridge";
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
                Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
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

    internal sealed class VenomOverlayVisibility : MonoBehaviour
    {
        private Renderer _source;
        private Renderer[] _overlayRenderers;

        internal void Bind(Renderer source, Renderer[] overlayRenderers)
        {
            _source = source;
            _overlayRenderers = overlayRenderers;
            Sync();
        }

        private void LateUpdate() { Sync(); }

        private void Sync()
        {
            if (_source == null || _overlayRenderers == null) return;
            bool visible = _source.enabled && _source.gameObject.activeInHierarchy;
            for (int i = 0; i < _overlayRenderers.Length; i++)
            {
                if (_overlayRenderers[i] != null) _overlayRenderers[i].enabled = visible;
            }
        }
    }

    internal static class VenomTipGeometry
    {
        private const int Segments = 36;
        private const int Rings = 6;
        private const float TipStartFromBase = 0.80f;
        private const float TipEndFromBase = 0.997f;
        private const float OverlayExpansion = 1.035f;

        internal static GameObject Create(Renderer sourceRenderer)
        {
            if (sourceRenderer == null) return null;
            Mesh sourceMesh = GetMesh(sourceRenderer);
            if (sourceMesh == null || sourceMesh.vertexCount == 0) return null;

            Bounds bounds = sourceMesh.bounds;
            int axis = LongestAxis(bounds.size);
            int radialA = (axis + 1) % 3;
            int radialB = (axis + 2) % 3;
            bool baseAtMinimum = DetectBaseAtMinimum(sourceMesh.vertices, bounds, axis, radialA, radialB);

            float radiusA;
            float radiusB;
            MeasureBulletRadius(
                sourceMesh.vertices,
                bounds,
                axis,
                radialA,
                radialB,
                baseAtMinimum,
                out radiusA,
                out radiusB);

            if (radiusA <= 0f || radiusB <= 0f) return null;

            GameObject root = new GameObject("Venom bright-green tip overlay");
            root.layer = sourceRenderer.gameObject.layer;
            root.transform.SetParent(sourceRenderer.transform, false);

            GameObject tip = new GameObject("Venom bright-green tip");
            tip.layer = root.layer;
            tip.transform.SetParent(root.transform, false);
            MeshFilter filter = tip.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateTipMesh(bounds, axis, radialA, radialB, baseAtMinimum, radiusA, radiusB);
            MeshRenderer renderer = tip.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateGreenMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            VenomOverlayVisibility visibility = root.AddComponent<VenomOverlayVisibility>();
            visibility.Bind(sourceRenderer, new Renderer[] { renderer });
            return root;
        }

        private static Mesh CreateTipMesh(
            Bounds bounds,
            int axis,
            int radialA,
            int radialB,
            bool baseAtMinimum,
            float radiusA,
            float radiusB)
        {
            int verticesPerRing = Segments + 1;
            Vector3[] vertices = new Vector3[Rings * verticesPerRing + 1];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            float minimum = Axis(bounds.min, axis);
            float length = Mathf.Max(0.0001f, Axis(bounds.max, axis) - minimum);

            for (int ring = 0; ring < Rings; ring++)
            {
                float t = (float)ring / (Rings - 1);
                float fromBase = Mathf.Lerp(TipStartFromBase, TipEndFromBase, t);
                float axial = baseAtMinimum ? minimum + length * fromBase : minimum + length * (1f - fromBase);
                float taper = Mathf.Lerp(1f, 0.09f, Mathf.SmoothStep(0f, 1f, t));

                for (int segment = 0; segment <= Segments; segment++)
                {
                    float turn = (float)segment / Segments;
                    float angle = turn * Mathf.PI * 2f;
                    Vector3 vertex = bounds.center;
                    SetAxis(ref vertex, axis, axial);
                    SetAxis(ref vertex, radialA, Axis(bounds.center, radialA) + Mathf.Cos(angle) * radiusA * taper * OverlayExpansion);
                    SetAxis(ref vertex, radialB, Axis(bounds.center, radialB) + Mathf.Sin(angle) * radiusB * taper * OverlayExpansion);
                    int index = ring * verticesPerRing + segment;
                    vertices[index] = vertex;

                    Vector3 normal = Vector3.zero;
                    SetAxis(ref normal, radialA, Mathf.Cos(angle));
                    SetAxis(ref normal, radialB, Mathf.Sin(angle));
                    normals[index] = normal.normalized;
                    uv[index] = new Vector2(turn, t);
                }
            }

            int tipIndex = Rings * verticesPerRing;
            Vector3 tipPoint = bounds.center;
            float tipAxial = baseAtMinimum ? Axis(bounds.max, axis) : Axis(bounds.min, axis);
            SetAxis(ref tipPoint, axis, tipAxial);
            vertices[tipIndex] = tipPoint;
            Vector3 tipNormal = AxisVector(axis) * (baseAtMinimum ? 1f : -1f);
            normals[tipIndex] = tipNormal;
            uv[tipIndex] = new Vector2(0.5f, 1f);

            List<int> triangles = new List<int>();
            for (int ring = 0; ring < Rings - 1; ring++)
            {
                for (int segment = 0; segment < Segments; segment++)
                {
                    int current = ring * verticesPerRing + segment;
                    int next = current + verticesPerRing;
                    triangles.Add(current);
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    triangles.Add(next + 1);
                    triangles.Add(next);
                }
            }

            int lastRing = (Rings - 1) * verticesPerRing;
            for (int segment = 0; segment < Segments; segment++)
            {
                int current = lastRing + segment;
                triangles.Add(current);
                triangles.Add(current + 1);
                triangles.Add(tipIndex);
            }

            int outsideCount = triangles.Count;
            for (int index = 0; index < outsideCount; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
            }

            Mesh mesh = new Mesh { name = "Venom bright-green projectile tip" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void MeasureBulletRadius(
            Vector3[] vertices,
            Bounds bounds,
            int axis,
            int radialA,
            int radialB,
            bool baseAtMinimum,
            out float radiusA,
            out float radiusB)
        {
            float minimum = Axis(bounds.min, axis);
            float length = Mathf.Max(0.0001f, Axis(bounds.max, axis) - minimum);
            float centerA = Axis(bounds.center, radialA);
            float centerB = Axis(bounds.center, radialB);
            radiusA = 0f;
            radiusB = 0f;

            for (int i = 0; i < vertices.Length; i++)
            {
                float axial = (Axis(vertices[i], axis) - minimum) / length;
                float fromBase = baseAtMinimum ? axial : 1f - axial;
                if (fromBase < 0.69f || fromBase > 0.84f) continue;
                radiusA = Mathf.Max(radiusA, Mathf.Abs(Axis(vertices[i], radialA) - centerA));
                radiusB = Mathf.Max(radiusB, Mathf.Abs(Axis(vertices[i], radialB) - centerB));
            }

            if (radiusA <= 0f) radiusA = Axis(bounds.size, radialA) * 0.30f;
            if (radiusB <= 0f) radiusB = Axis(bounds.size, radialB) * 0.30f;
        }

        private static bool DetectBaseAtMinimum(Vector3[] vertices, Bounds bounds, int axis, int radialA, int radialB)
        {
            float minimum = Axis(bounds.min, axis);
            float length = Mathf.Max(0.0001f, Axis(bounds.max, axis) - minimum);
            float centerA = Axis(bounds.center, radialA);
            float centerB = Axis(bounds.center, radialB);
            float minimumRadius = 0f;
            float maximumRadius = 0f;

            for (int i = 0; i < vertices.Length; i++)
            {
                float axial = (Axis(vertices[i], axis) - minimum) / length;
                if (axial > 0.20f && axial < 0.80f) continue;
                float deltaA = Axis(vertices[i], radialA) - centerA;
                float deltaB = Axis(vertices[i], radialB) - centerB;
                float radius = Mathf.Sqrt(deltaA * deltaA + deltaB * deltaB);
                if (axial <= 0.20f) minimumRadius = Mathf.Max(minimumRadius, radius);
                else maximumRadius = Mathf.Max(maximumRadius, radius);
            }

            return minimumRadius >= maximumRadius;
        }

        private static Material CreateGreenMaterial()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Material material = new Material(shader);
            material.name = "Venom bright-green tip material";
            Color green = new Color(0.20f, 1.00f, 0.08f, 1f);
            material.color = green;
            if (material.HasProperty("_Color")) material.SetColor("_Color", green);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", green);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.12f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.55f);
            return material;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null) return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static int LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return 0;
            return size.y >= size.z ? 1 : 2;
        }

        private static Vector3 AxisVector(int axis)
        {
            if (axis == 0) return Vector3.right;
            return axis == 1 ? Vector3.up : Vector3.forward;
        }

        private static float Axis(Vector3 value, int axis)
        {
            if (axis == 0) return value.x;
            return axis == 1 ? value.y : value.z;
        }

        private static void SetAxis(ref Vector3 value, int axis, float component)
        {
            if (axis == 0) value.x = component;
            else if (axis == 1) value.y = component;
            else value.z = component;
        }
    }

    internal static class VenomVisuals
    {
        internal static bool UsesHpModel(Item item)
        {
            if (item == null) return false;
            string templateId = item.TemplateId.ToString();
            return templateId == VenomConstants.TemplateId || templateId == VenomConstants.HpTemplateId;
        }

        internal static void Apply(Item item, GameObject itemObject)
        {
            if (item == null || itemObject == null || !UsesHpModel(item)) return;
            VenomVisualMarker marker = itemObject.GetComponent<VenomVisualMarker>();
            if (marker == null)
            {
                marker = itemObject.AddComponent<VenomVisualMarker>();
                marker.Initialize(itemObject.GetComponentsInChildren<Renderer>(true));
            }

            if (item.TemplateId.ToString() == VenomConstants.TemplateId) marker.ShowVenom();
            else marker.ShowOriginalHp();
        }
    }

    [HarmonyPatch]
    internal static class VenomSynchronousItemVisualPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ObjectsFactory)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.ReturnType == typeof(GameObject) && parameters.Length > 0 && parameters[0].ParameterType == typeof(Item))
                {
                    yield return method;
                }
            }
        }

        private static void Postfix(Item __0, GameObject __result)
        {
            VenomVisuals.Apply(__0, __result);
        }
    }

    [HarmonyPatch]
    internal static class VenomAsynchronousItemVisualPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ObjectsFactory)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.ReturnType == typeof(Task<GameObject>) && parameters.Length > 0 && parameters[0].ParameterType == typeof(Item))
                {
                    yield return method;
                }
            }
        }

        private static void Postfix(Item __0, ref Task<GameObject> __result)
        {
            if (!VenomVisuals.UsesHpModel(__0) || __result == null) return;
            __result = ApplyWhenReady(__0, __result);
        }

        private static async Task<GameObject> ApplyWhenReady(Item item, Task<GameObject> itemTask)
        {
            GameObject itemObject = await itemTask;
            VenomVisuals.Apply(item, itemObject);
            return itemObject;
        }
    }
}
