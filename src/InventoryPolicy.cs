namespace LethalFakeClient;

internal static class InventoryPolicy
{
    internal static bool FitsUtilitySlot(bool isScrap, bool twoHanded, bool disallowUtilitySlot)
    {
        return !isScrap && !twoHanded && !disallowUtilitySlot;
    }

    internal static bool HasRoom(int freeSlots, bool utilityEmpty, bool isScrap, bool twoHanded, bool disallowUtilitySlot)
    {
        return freeSlots > 0 || utilityEmpty && FitsUtilitySlot(isScrap, twoHanded, disallowUtilitySlot);
    }
}
