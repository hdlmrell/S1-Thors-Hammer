using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Weather;
#else
using ScheduleOne.Weather;
#endif

namespace ThorHammer;

/// <summary>
/// Harmony patch to allow EnvironmentManager.SetWeatherSequence to run when not server.
/// When the server check would block, we run the logic via reflection (for single-player / host).
/// </summary>
[HarmonyPatch(typeof(EnvironmentManager), "SetWeatherSequence")]
internal static class WeatherPatch
{
    private static FieldInfo _weatherSequences;
    private static FieldInfo _activeVolumes;
    private static FieldInfo _currentSequence;
    private static FieldInfo _volumeCount;
    private static MethodInfo _createVolumes;

    [HarmonyPrefix]
    static bool Prefix(EnvironmentManager __instance, string sequenceId)
    {
        var finderType = Type.GetType("FishNet.Runtime.InstanceFinder, FishNet.Runtime")
            ?? Type.GetType("InstanceFinder, FishNet.Runtime");
        if (finderType == null) return true;

        var isServerProp = finderType.GetProperty("IsServer");
        if (isServerProp == null) return true;

        var isServer = (bool)isServerProp.GetValue(null);
        if (isServer)
            return true; // Run original

        // Not server — run logic ourselves via reflection
        try
        {
            _weatherSequences ??= typeof(EnvironmentManager).GetField("_weatherSequences",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _activeVolumes ??= typeof(EnvironmentManager).GetField("_activeWeatherVolumes",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _currentSequence ??= typeof(EnvironmentManager).GetField("_currentWeatherSequence",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _volumeCount ??= typeof(EnvironmentManager).GetField("_weatherVolumeCount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _createVolumes ??= typeof(EnvironmentManager).GetMethod("CreateWeatherVolumes",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (_weatherSequences?.GetValue(__instance) is not System.Collections.IList seqs)
                return false;

            WeatherSequence seq = null;
            foreach (var s in seqs)
            {
                if (s is WeatherSequence ws && ws.Id.Equals(sequenceId))
                {
                    seq = ws;
                    break;
                }
            }

            if (seq == null)
            {
                UnityEngine.Debug.LogError("No weather sequence found with id " + sequenceId);
                return false;
            }

            var volumeCount = (int)(_volumeCount?.GetValue(__instance) ?? 3);
            var volumes = _activeVolumes?.GetValue(__instance);
            if (volumes is System.Collections.IList volList && volList.Count > 0)
            {
                var baseType = __instance.GetType().BaseType;
                var despawnParams = new[] { typeof(UnityEngine.GameObject), Type.GetType("FishNet.Connection.NetworkConnection, FishNet.Runtime") ?? typeof(object) };
                var despawn = baseType?.GetMethod("Despawn", despawnParams);
                if (despawn != null)
                {
                    for (int i = 0; i < volumeCount && volList.Count > 0; i++)
                    {
                        var vol = volList[0];
                        if (vol != null)
                        {
                            var go = (vol as UnityEngine.Component)?.gameObject;
                            if (go != null)
                                despawn.Invoke(__instance, new object[] { go, null });
                        }
                    }
                }
            }

            _currentSequence?.SetValue(__instance, seq);
            _createVolumes?.Invoke(__instance, null);
        }
        catch (System.Exception ex)
        {
            MelonLoader.MelonLogger.Warning($"[ThorHammer] Weather change failed: {ex.Message}");
        }

        return false; // Skip original
    }
}
