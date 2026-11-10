namespace LethalFakeClient;

internal static class CombatRules
{
    internal static int HealthAfterDamage(int health, int damage)
    {
        if (health <= 0)
            return 0;
        if (damage <= 0)
            return health;
        return damage >= health ? 0 : health - damage;
    }

    internal static int ShotgunDamage(float distance)
    {
        return distance < 15f ? 100 : distance < 23f ? 40 : distance < 30f ? 20 : 0;
    }
}
