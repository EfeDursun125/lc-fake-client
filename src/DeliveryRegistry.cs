using System.Collections.Generic;
using HarmonyLib;
using Unity.Netcode;

namespace LethalFakeClient;

internal static class DeliveryRegistry
{
    private static readonly HashSet<ulong> existing = new HashSet<ulong>();
    internal static readonly HashSet<GrabbableObject> Items = new HashSet<GrabbableObject>();

    internal static void Begin()
    {
        existing.Clear();
        var network = NetworkManager.Singleton;
        if (network == null || network.SpawnManager == null)
            return;
        foreach (var entry in network.SpawnManager.SpawnedObjects)
            existing.Add(entry.Key);
    }

    internal static void End()
    {
        var network = NetworkManager.Singleton;
        if (network == null || network.SpawnManager == null)
            return;
        foreach (var entry in network.SpawnManager.SpawnedObjects)
            if (!existing.Contains(entry.Key) && entry.Value != null && entry.Value.TryGetComponent<GrabbableObject>(out var item))
                Items.Add(item);
        existing.Clear();
        Plugin.DeliveryChanged();
    }

    internal static void Clear()
    {
        existing.Clear();
        Items.Clear();
    }
}

[HarmonyPatch(typeof(ItemDropship), "OpenShipDoorsOnServer")]
internal static class DeliveryCapture
{
    private static void Prefix(ItemDropship __instance, out bool __state)
    {
        __state = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost &&
            __instance.shipLanded && !__instance.shipDoorsOpened;
        if (__state)
            DeliveryRegistry.Begin();
    }

    private static void Postfix(bool __state)
    {
        if (__state)
            DeliveryRegistry.End();
    }
}
