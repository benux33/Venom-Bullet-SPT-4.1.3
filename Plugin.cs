using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Venom.Client
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.bensburnedwaffles.venom.client";
        public const string PluginName = "5.56x45 Venom Client";
        public const string PluginVersion = "1.0.6";
        internal static ManualLogSource Log { get; private set; }
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            try
            {
                VenomAssets.LoadAll();
                VenomScreenEffects.EnsureHost();
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo("5.56x45 Venom v1.0.6 loaded: native Tarkov tunnel vision, OnBreath symptom audio, real prone crawl, native effect icons, brown/black cartridge visuals, and Venom-specific item icon cache are active.");
            }
            catch (Exception exception)
            {
                Logger.LogError("5.56x45 Venom failed to initialize: " + exception);
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
            VenomScreenEffects.ResetAll();
        }
    }

    internal static class VenomAssets
    {
        internal static Sprite InfectedWoundStatusSprite { get; private set; }
        internal static Sprite NauseousStatusSprite { get; private set; }
        internal static Sprite DyingStatusSprite { get; private set; }

        internal static void LoadAll()
        {
            InfectedWoundStatusSprite = LoadIcon("InfectedWound.png", "Venom infected wound icon");
            NauseousStatusSprite = LoadIcon("Nauseous.png", "Venom nauseous icon");
            DyingStatusSprite = LoadIcon("Dying.png", "Venom dying icon");
        }

        private static Sprite LoadIcon(string fileName, string friendlyName)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), fileName);
            if (!File.Exists(path)) throw new FileNotFoundException("The " + friendlyName + " is missing.", path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = friendlyName;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) throw new InvalidOperationException("The " + friendlyName + " could not be decoded.");
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = friendlyName + " status sprite";
            return sprite;
        }
    }
}
