using GameNetcodeStuff;
using UnityEngine;

namespace LethalFakeClient;

internal static class SuitSelector
{
    internal static void Assign(PlayerControllerB bot, StartOfRound round)
    {
        UnlockableSuit? selected = null;
        int leastUsed = int.MaxValue;
        int ties = 0;
        foreach (var suit in Object.FindObjectsOfType<UnlockableSuit>())
        {
            if (suit == null || !suit.isActiveAndEnabled || !suit.IsSpawned || suit.suitID < 0 ||
                suit.suitID != suit.syncedSuitID.Value || suit.suitID >= round.unlockablesList.unlockables.Count ||
                round.unlockablesList.unlockables[suit.suitID].suitMaterial == null)
                continue;

            int uses = 0;
            foreach (var player in round.allPlayerScripts)
                if (player != null && player != bot && player.isPlayerControlled && !player.isPlayerDead && player.currentSuitID == suit.suitID)
                    uses++;
            if (uses < leastUsed)
            {
                leastUsed = uses;
                selected = suit;
                ties = 1;
            }
            else if (uses == leastUsed && Random.Range(0, ++ties) == 0)
                selected = suit;
        }
        if (selected != null)
            selected.SwitchSuitToThis(bot);
    }

    internal static void Restore(PlayerControllerB bot, int suitId)
    {
        if (bot.currentSuitID == suitId)
            return;
        foreach (var suit in Object.FindObjectsOfType<UnlockableSuit>())
        {
            if (suit != null && suit.isActiveAndEnabled && suit.IsSpawned && suit.suitID == suitId && suit.syncedSuitID.Value == suitId)
            {
                suit.SwitchSuitToThis(bot);
                return;
            }
        }
    }
}
