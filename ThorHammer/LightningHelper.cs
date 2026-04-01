#if IL2CPP
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Weather;
#else
using ScheduleOne.Effects;
using ScheduleOne.NPCs;
using ScheduleOne.Weather;
#endif

using MelonLoader;
using System.Collections;
using UnityEngine;

namespace ThorHammer;

/// <summary>
/// Lightning effect helpers using the game's real lightning VFX.
/// Clones the ThunderController VFX so it works without setting weather to heavy rain.
/// </summary>
internal static class LightningHelper
{
    private static VFXEffectHandler _lightningVFX;
    private static bool _searched;

    /// <summary>Clears the electrify effect from an NPC after a delay.</summary>
    internal static IEnumerator ClearElectrifyCoroutine(NPC npc)
    {
        yield return new WaitForSeconds(Core.ElectrifyDuration);
        if (npc != null && npc.Avatar != null)
            Electrifying.ClearFromAvatar(npc.Avatar);
    }

    /// <summary>Shoots a lightning bolt from the hammer to the target (not from sky).</summary>
    internal static void ShootBoltFromTo(Vector3 from, Vector3 to)
    {
        var go = new GameObject("ThorLightningBolt");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 0;
        lr.useWorldSpace = true;

        var shader = Shader.Find("Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.color = new Color(0.85f, 0.95f, 1f, 1f);
        if (mat.HasProperty("_EmissionColor"))
            mat.SetColor("_EmissionColor", new Color(0.6f, 0.85f, 1f, 1f));
        lr.material = mat;
        lr.startWidth = 0.1f;
        lr.endWidth = 0.025f;
        lr.startColor = new Color(0.95f, 0.98f, 1f, 1f);
        lr.endColor = new Color(0.5f, 0.75f, 1f, 0.8f);

        int segments = 36;
        var points = new Vector3[segments + 1];
        var dir = to - from;
        float len = dir.magnitude;
        dir.Normalize();
        var right = Vector3.Cross(dir, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(dir, Vector3.forward).normalized;
        var up = Vector3.Cross(right, dir).normalized;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 basePt = from + dir * (t * len);
            if (i > 0 && i < segments)
            {
                float jitter = (UnityEngine.Random.value - 0.5f) * 0.06f * len;
                float jitter2 = (UnityEngine.Random.value - 0.5f) * 0.06f * len;
                basePt += right * jitter + up * jitter2;
            }
            points[i] = basePt;
        }
        points[0] = from;
        points[segments] = to;

        lr.positionCount = segments + 1;
        lr.SetPositions(points);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.85f, 0.95f, 1f);
        light.intensity = 2f;
        light.range = len * 0.5f;
        light.renderMode = LightRenderMode.ForceVertex;

        MelonCoroutines.Start(DestroyAfter(go, 0.18f));
    }

    private static IEnumerator DestroyAfter(UnityEngine.GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null)
            UnityEngine.Object.Destroy(go);
    }

    /// <summary>Strikes the game's real lightning VFX at the given position. Works without heavy rain.</summary>
    internal static void StrikeLightning(Vector3 position)
    {
        EnsureVFX();
        if (_lightningVFX == null) return;
        _lightningVFX.SetPosition(position);
        _lightningVFX.Activate();
        _lightningVFX.DelayDeactivate(2f);
    }

    /// <summary>
    /// Lazily finds and clones the Lightning VFXEffectHandler from the game's
    /// ThunderController. The clone lives in a standalone active hierarchy
    /// (DontDestroyOnLoad) so it renders even when there is no active storm.
    /// </summary>
    private static void EnsureVFX()
    {
        if (_searched) return;
        _searched = true;

        var all = Resources.FindObjectsOfTypeAll<ThunderController>();
        ThunderController tc = all.Length > 0 ? all[0] : null;

        if (tc == null)
        {
            Melon<Core>.Logger.Warning("No ThunderController found — lightning VFX unavailable");
            return;
        }

        VFXEffectHandler original = null;
#if IL2CPP
        original = tc._lightningEffect;
        if (original == null && tc.visualEffects != null)
        {
            foreach (var vfx in tc.visualEffects)
            {
                if (vfx != null && vfx.Id == "Lightning")
                {
                    original = vfx;
                    break;
                }
            }
        }
#else
        var field = typeof(ThunderController).GetField("_lightningEffect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        original = field?.GetValue(tc) as VFXEffectHandler;
        if (original == null)
        {
            var listField = typeof(WeatherEffectController).GetField("visualEffects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (listField?.GetValue(tc) is System.Collections.IList list)
            {
                foreach (var item in list)
                {
                    if (item is VFXEffectHandler vfx && vfx.Id == "Lightning")
                    {
                        original = vfx;
                        break;
                    }
                }
            }
        }
#endif

        if (original == null)
        {
            Melon<Core>.Logger.Warning("No lightning VFX found on ThunderController");
            return;
        }

        var clone = UnityEngine.Object.Instantiate(original.gameObject);
        clone.name = "ThorLightningVFX";
        clone.SetActive(true);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        _lightningVFX = clone.GetComponent<VFXEffectHandler>();
        if (_lightningVFX != null)
        {
            _lightningVFX.Deactivate();
            Melon<Core>.Logger.Msg("Cloned game lightning VFX for ThorHammer (works without heavy rain).");
        }
        else
        {
            Melon<Core>.Logger.Warning("Cloned lightning VFX has no VFXEffectHandler component");
            UnityEngine.Object.Destroy(clone);
        }
    }
}
