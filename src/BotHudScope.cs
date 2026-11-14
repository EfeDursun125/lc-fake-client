using System;

namespace LethalFakeClient;

internal static class BotHudScope
{
    [ThreadStatic]
    private static int depth;

    internal static bool Active => depth > 0;

    internal static Scope Enter()
    {
        depth++;
        return new Scope();
    }

    internal readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            depth--;
        }
    }
}
