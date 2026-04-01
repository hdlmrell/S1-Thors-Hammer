using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
#else
using ScheduleOne.Combat;
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
#endif

namespace ThorHammer;

/// <summary>
/// Adds camera shake on punch release so the camera isn't static during the swing.
/// The base game only shakes on hit; this patch shakes on every release (hit or miss).
/// </summary>
[HarmonyPatch(typeof(PunchController), "Punch")]
internal static class PunchCameraPatch
{
    [HarmonyPostfix]
    static void Postfix(float power)
    {
        // __0 is the power (num) passed to Punch - 0 to 1
        if (PlayerSingleton<PlayerCamera>.Instance == null) return;
        float intensity = UnityEngine.Mathf.Lerp(0.06f, 0.14f, power);
        float duration = UnityEngine.Mathf.Lerp(0.12f, 0.18f, power);
        PlayerSingleton<PlayerCamera>.Instance.StartCameraShake(intensity, duration, true);
    }
}
