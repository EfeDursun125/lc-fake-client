using System;

namespace LethalFakeClient;

internal static class LevelPolicy
{
    internal static bool IsCompany(string? sceneName, int levelId, bool spawnEnemiesAndScrap)
    {
        return string.Equals(sceneName, "CompanyBuilding", StringComparison.OrdinalIgnoreCase) ||
            levelId == 3 && !spawnEnemiesAndScrap;
    }
}
