using System;

namespace LethalFakeClient;

internal static class BotIdentityPolicy
{
    internal static bool ValidWireLength(int length, int available) =>
        length >= 0 && length <= 125 && length <= available;

    internal static string NetworkName(string value)
    {
        while (System.Text.Encoding.UTF8.GetByteCount(value) > 125)
        {
            int length = value.Length - 1;
            if (length > 0 && char.IsLowSurrogate(value[length]) && char.IsHighSurrogate(value[length - 1]))
                length--;
            value = value.Substring(0, length);
        }
        return value;
    }

}
