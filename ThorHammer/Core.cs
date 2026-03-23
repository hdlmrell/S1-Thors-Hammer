#if IL2CPP
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.Persistence;
#endif

using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;
using S1API.Items;
using S1API.Shops;
using S1MAPI.Gltf;
using S1MAPI.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(ThorHammer.Core), "Mjolnir", "1.0.3", "hdlmrell", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ThorHammer;

public class Core : MelonMod
{
    private bool _itemsRegistered;
    private bool _loadHooked;
    private static ItemDefinition _hammerDef;
    private static Sprite _cachedIcon;

    private static readonly string[] HardwareShopNames =
        { "Handy Hank's Hardware", "Dan's Hardware" };

    // Config entries
    private static MelonPreferences_Entry<float> _priceEntry;
    private static MelonPreferences_Entry<bool> _sellAtHardwareEntry;
    private static bool _shopsPopulated;

    // ── Controls ──

    /// <summary>The configured key for summoning lightning from the hammer.</summary>
    public static KeyCode LightningKey { get; private set; } = KeyCode.X;

    // ── Combat ──

    /// <summary>Damage dealt by a melee swing.</summary>
    public static float MeleeDamage { get; private set; } = 25f;

    /// <summary>Impact force of a melee swing.</summary>
    public static float MeleeForce { get; private set; } = 350f;

    /// <summary>Damage dealt when the thrown hammer hits.</summary>
    public static float ThrowDamage { get; private set; } = 100f;

    /// <summary>Impact force of the thrown hammer.</summary>
    public static float ThrowForce { get; private set; } = 600f;

    /// <summary>Damage dealt by a lightning zap.</summary>
    public static float LightningDamage { get; private set; } = 80f;

    /// <summary>Impact force of a lightning zap.</summary>
    public static float LightningForce { get; private set; } = 400f;

    /// <summary>Radius around lightning strikes that causes nearby NPCs to panic (0 to disable).</summary>
    public static float LightningPanicRadius { get; private set; } = 25f;

    // ── Mechanics ──

    /// <summary>How fast the player flies while holding Space during a charged wind-up.</summary>
    public static float FlightSpeed { get; private set; } = 18f;

    /// <summary>How fast the thrown hammer travels.</summary>
    public static float ThrowSpeed { get; private set; } = 40f;

    /// <summary>Maximum distance the hammer can travel before returning.</summary>
    public static float MaxThrowRange { get; private set; } = 30f;

    /// <summary>Seconds to fully charge the hammer spin (minimum 0.1).</summary>
    public static float WindUpDuration { get; private set; } = 1.2f;

    // ── Stamina ──

    /// <summary>Whether hammer actions consume stamina.</summary>
    public static bool StaminaEnabled { get; private set; } = true;

    /// <summary>Stamina consumed per melee swing.</summary>
    public static float SwingStaminaCost { get; private set; } = 15f;

    /// <summary>Stamina consumed per lightning zap.</summary>
    public static float LightningStaminaCost { get; private set; } = 20f;

    /// <summary>Stamina consumed per second while winding up.</summary>
    public static float WindUpStaminaRate { get; private set; } = 20f;

    /// <summary>Stamina consumed per second while flying.</summary>
    public static float FlightStaminaRate { get; private set; } = 15f;

    private static string IconPath =>
        Path.Combine(MelonEnvironment.UserDataDirectory, "S1API", "Icons", "ThorHammer.png");

    /// <inheritdoc />
    public override void OnInitializeMelon()
    {
#if IL2CPP
        ClassInjector.RegisterTypeInIl2Cpp<HammerEquippable>();
#endif

        // ── Controls ──
        var controls = MelonPreferences.CreateCategory("Mjolnir - Controls", "Mjolnir Controls");
        var savedKey = BindEntry(controls, "LightningKey", "X", "Lightning Key",
            "Key to summon lightning from the hammer (e.g. X, F, G, T)",
            (_, v) =>
            {
                if (Enum.TryParse<KeyCode>(v, true, out var k)) LightningKey = k;
                else LoggerInstance.Warning($"Invalid lightning key '{v}', keeping {LightningKey}");
            });
        if (Enum.TryParse<KeyCode>(savedKey, true, out var initial)) LightningKey = initial;
        else LoggerInstance.Warning($"Invalid lightning key '{savedKey}', defaulting to X");

        // ── Shop ──
        var shop = MelonPreferences.CreateCategory("Mjolnir - Shop", "Mjolnir Shop");
        _priceEntry = shop.CreateEntry("Price", 10000f, "Shop Price",
            "Price of Mjolnir at the Arms Dealer (in dollars)");
        _priceEntry.OnEntryValueChanged.Subscribe((_, v) => OnPriceChanged(v));
        _sellAtHardwareEntry = shop.CreateEntry("SellAtHardwareStores", false,
            "Sell at Hardware Stores",
            "Whether Mjolnir is also sold at hardware stores (Handy Hank's, Dan's)");
        _sellAtHardwareEntry.OnEntryValueChanged.Subscribe((_, v) => OnHardwareShopToggled(v));

        // ── Combat ──
        var combat = MelonPreferences.CreateCategory("Mjolnir - Combat", "Mjolnir Combat");
        MeleeDamage = BindEntry(combat, "MeleeDamage", 25f, "Melee Damage",
            "Damage dealt by a melee swing", (_, v) => MeleeDamage = v);
        MeleeForce = BindEntry(combat, "MeleeForce", 350f, "Melee Force",
            "Impact force of a melee swing", (_, v) => MeleeForce = v);
        ThrowDamage = BindEntry(combat, "ThrowDamage", 100f, "Throw Damage",
            "Damage dealt when the thrown hammer hits", (_, v) => ThrowDamage = v);
        ThrowForce = BindEntry(combat, "ThrowForce", 600f, "Throw Force",
            "Impact force of the thrown hammer", (_, v) => ThrowForce = v);
        LightningDamage = BindEntry(combat, "LightningDamage", 80f, "Lightning Damage",
            "Damage dealt by a lightning zap", (_, v) => LightningDamage = v);
        LightningForce = BindEntry(combat, "LightningForce", 400f, "Lightning Force",
            "Impact force of a lightning zap", (_, v) => LightningForce = v);
        LightningPanicRadius = BindEntry(combat, "LightningPanicRadius", 25f, "Lightning Panic Radius",
            "Radius around lightning strikes that causes nearby NPCs to panic (0 to disable)",
            (_, v) => LightningPanicRadius = v);

        // ── Mechanics ──
        var mechanics = MelonPreferences.CreateCategory("Mjolnir - Mechanics", "Mjolnir Mechanics");
        FlightSpeed = BindEntry(mechanics, "FlightSpeed", 18f, "Flight Speed",
            "How fast you fly while holding Space during a charged wind-up", (_, v) => FlightSpeed = v);
        ThrowSpeed = BindEntry(mechanics, "ThrowSpeed", 40f, "Throw Speed",
            "How fast the thrown hammer travels", (_, v) => ThrowSpeed = v);
        MaxThrowRange = BindEntry(mechanics, "MaxThrowRange", 30f, "Max Throw Range",
            "Maximum distance the hammer can travel before returning", (_, v) => MaxThrowRange = v);
        WindUpDuration = Math.Max(0.1f, BindEntry(mechanics, "WindUpDuration", 1.2f, "Wind-Up Duration",
            "Seconds to fully charge the hammer spin (minimum 0.1)",
            (_, v) => WindUpDuration = Math.Max(0.1f, v)));

        // ── Stamina ──
        var stamina = MelonPreferences.CreateCategory("Mjolnir - Stamina", "Mjolnir Stamina");
        StaminaEnabled = BindEntry(stamina, "StaminaEnabled", true, "Stamina Enabled",
            "Whether hammer actions consume stamina", (_, v) => StaminaEnabled = v);
        SwingStaminaCost = BindEntry(stamina, "SwingStaminaCost", 15f, "Swing Stamina Cost",
            "Stamina consumed per melee swing", (_, v) => SwingStaminaCost = v);
        LightningStaminaCost = BindEntry(stamina, "LightningStaminaCost", 20f, "Lightning Stamina Cost",
            "Stamina consumed per lightning zap", (_, v) => LightningStaminaCost = v);
        WindUpStaminaRate = BindEntry(stamina, "WindUpStaminaRate", 20f, "Wind-Up Stamina Rate",
            "Stamina consumed per second while winding up", (_, v) => WindUpStaminaRate = v);
        FlightStaminaRate = BindEntry(stamina, "FlightStaminaRate", 15f, "Flight Stamina Rate",
            "Stamina consumed per second while flying", (_, v) => FlightStaminaRate = v);

        LoggerInstance.Msg("Initialized.");
    }

    /// <inheritdoc />
    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (sceneName == "Main" && !_itemsRegistered)
        {
            _itemsRegistered = true;
            RegisterItems();
        }

        if (sceneName == "Main" && !_loadHooked)
        {
#if IL2CPP
            var lm = Singleton<LoadManager>.Instance;
            if (lm != null)
            {
                lm.onLoadComplete.AddListener((UnityEngine.Events.UnityAction)OnGameLoaded);
                _loadHooked = true;
            }
#else
            var lm = LoadManager.Instance;
            if (lm != null)
            {
                lm.onLoadComplete.AddListener(OnGameLoaded);
                _loadHooked = true;
            }
#endif
        }
    }

    /// <inheritdoc />
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (sceneName == "Main")
        {
            _loadHooked = false;
        }
    }

    private void RegisterItems()
    {
        var equippable = ItemCreator.CreateEquippableBuilder()
            .CreateEquippable<HammerEquippable>("ThorHammerEquippable")
            .WithInteraction(canInteract: true, canPickup: true)
            .Build();

        _hammerDef = ItemCreator.CreateBuilder()
            .WithBasicInfo(
                id: "thor_hammer",
                name: "Mjolnir",
                description: "Mjolnir. Whosoever holds this hammer, if they be worthy, shall possess the power of Thor.",
                category: ItemCategory.Tools)
            .WithStackLimit(1)
            .WithPricing(_priceEntry.Value, 0.5f)
            .WithLegalStatus(LegalStatus.Legal)
            .WithEquippable(equippable)
            .Build();

        RenderHammerIcon();

        LoggerInstance.Msg("Mjolnir registered.");
    }

    private void RenderHammerIcon()
    {
        if (_cachedIcon != null)
        {
            _hammerDef.Icon = _cachedIcon;
            return;
        }

        string path = IconPath;

        // Try loading from disk cache
        if (File.Exists(path))
        {
            try
            {
                byte[] pngData = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(pngData))
                {
                    _cachedIcon = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _cachedIcon.name = "ThorHammerIcon";
                    _hammerDef.Icon = _cachedIcon;
                    LoggerInstance.Msg("Loaded hammer icon from cache.");
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to load cached icon, re-rendering: {ex.Message}");
            }
        }

        // Render the icon from the GLB model
        GameObject stageRoot = null;
        Camera cam = null;
        RenderTexture rt = null;
        try
        {
            stageRoot = new GameObject("TH_IconStage");
            stageRoot.transform.position = new Vector3(0f, 5000f, 0f);

            // Load hammer model
            byte[] glbData = EmbeddedResourceLoader.LoadBytes(
                "ThorHammer.Resources.ThorHammer.glb",
                Assembly.GetExecutingAssembly());
            if (glbData == null)
            {
                LoggerInstance.Warning("Cannot render icon: GLB resource not found");
                return;
            }

            var hammer = GltfLoader.LoadGlb(glbData);
            if (hammer == null)
            {
                LoggerInstance.Warning("Cannot render icon: GLB load failed");
                return;
            }

            hammer.transform.SetParent(stageRoot.transform, false);
            hammer.transform.localPosition = Vector3.zero;
            hammer.transform.localRotation = Quaternion.Euler(0f, 135f, -30f);
            hammer.transform.localScale = Vector3.one * 0.5f;

            // Directional light
            var lightGo = new GameObject("TH_IconLight");
            lightGo.transform.SetParent(stageRoot.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Fill light
            var fillGo = new GameObject("TH_IconFill");
            fillGo.transform.SetParent(stageRoot.transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.8f, 0.85f, 0.95f);
            fill.intensity = 0.5f;
            fillGo.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

            // Camera — isometric product shot
            var camGo = new GameObject("TH_IconCamera");
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.8f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = ~0;
            cam.enabled = false;

            camGo.transform.position = stageRoot.transform.position + new Vector3(-1.5f, 1.5f, 1.5f);
            camGo.transform.LookAt(stageRoot.transform.position);

            // Render to texture
            rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            // Read pixels
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // Save to disk cache
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to save icon to disk: {ex.Message}");
            }

            // Create sprite
            _cachedIcon = Sprite.Create(tex,
                new Rect(0, 0, 256, 256),
                new Vector2(0.5f, 0.5f), 100f);
            _cachedIcon.name = "ThorHammerIcon";
            _hammerDef.Icon = _cachedIcon;

            LoggerInstance.Msg("Rendered hammer icon.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"RenderHammerIcon failed: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            if (stageRoot != null) UnityEngine.Object.Destroy(stageRoot);
        }
    }

    private void OnGameLoaded()
    {
        if (_hammerDef == null) return;

        int added = ShopManager.AddToShops(_hammerDef, "Arms Dealer");
        if (_sellAtHardwareEntry.Value)
            added += ShopManager.AddToShops(_hammerDef, HardwareShopNames);

        _shopsPopulated = true;
        LoggerInstance.Msg($"Mjolnir added to {added} shop(s).");
    }

    private void OnPriceChanged(float newPrice)
    {
        if (!_shopsPopulated || _hammerDef == null) return;

        if (_hammerDef is StorableItemDefinition storable)
            storable.BasePurchasePrice = newPrice;

        // Remove and re-add to all shops so the listing picks up the new price
        var shops = ShopManager.FindShopsByItem("thor_hammer");
        foreach (var s in shops)
        {
            s.RemoveItem("thor_hammer");
            s.AddItem(_hammerDef, newPrice);
        }
        LoggerInstance.Msg($"Mjolnir price updated to ${newPrice:N0}.");
    }

    private void OnHardwareShopToggled(bool enabled)
    {
        if (!_shopsPopulated || _hammerDef == null) return;

        if (enabled)
        {
            int added = ShopManager.AddToShops(_hammerDef, HardwareShopNames);
            LoggerInstance.Msg($"Mjolnir added to {added} hardware shop(s).");
        }
        else
        {
            foreach (var name in HardwareShopNames)
            {
                var s = ShopManager.GetShopByName(name);
                if (s != null && s.HasItem("thor_hammer"))
                    s.RemoveItem("thor_hammer");
            }
            LoggerInstance.Msg("Mjolnir removed from hardware shops.");
        }
    }

    /// <summary>Creates a config entry with a live-update callback. Returns the initial value.</summary>
    private static T BindEntry<T>(MelonPreferences_Category cat, string id, T defaultValue,
        string displayName, string description, LemonAction<T, T> onChanged)
    {
        var entry = cat.CreateEntry(id, defaultValue, displayName, description);
        entry.OnEntryValueChanged.Subscribe(onChanged);
        return entry.Value;
    }
}
