namespace LethalFakeClient;

internal static class TulipFlightPolicy
{
    internal static float FallVelocity(float velocity, float counteractiveForce, float deltaTime, bool flying, bool grounded)
    {
        if (flying)
        {
            if (grounded)
                velocity = System.Math.Max(velocity, -7f);
            float change = 9f * deltaTime;
            if (velocity > counteractiveForce)
                return System.Math.Max(counteractiveForce, velocity - change);
            return System.Math.Min(counteractiveForce, velocity + change);
        }
        return System.Math.Max(-150f, velocity - 38f * deltaTime);
    }
}
