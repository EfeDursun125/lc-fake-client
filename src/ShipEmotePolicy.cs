namespace LethalFakeClient;

internal static class ShipEmotePolicy
{
    internal static bool CanPerform(bool aboard, bool moving, bool shipOperator, bool waiting, bool interacting)
    {
        return aboard && !moving && !shipOperator && waiting && !interacting;
    }

    internal static byte Pack(bool jumping, byte emote)
    {
        return (byte)((jumping ? 1 : 0) | (emote <= 2 ? emote << 1 : 0));
    }

    internal static byte Emote(byte flags)
    {
        int value = (flags >> 1) & 3;
        return (byte)(value <= 2 ? value : 0);
    }
}
