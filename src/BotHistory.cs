using System;
using System.IO;
using UnityEngine;

namespace LethalFakeClient;

internal sealed class BotHistory
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private readonly string file = Path.Combine(BepInEx.Paths.ConfigPath, "lethal-fake-client.history.log");
    private bool disabled;

    internal void Record(IBot bot, string eventName, string details)
    {
        if (disabled)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (File.Exists(file) && new FileInfo(file).Length >= MaxBytes)
            {
                string previous = file + ".old";
                if (File.Exists(previous))
                    File.Delete(previous);
                File.Move(file, previous);
            }
            string moon = StartOfRound.Instance != null && StartOfRound.Instance.currentLevel != null
                ? StartOfRound.Instance.currentLevel.PlanetName : "unknown";
            File.AppendAllText(file, $"{DateTime.UtcNow:O} bot={bot.Body.playerClientId} moon={moon} event={eventName} task={bot.Order.Kind} inside={bot.Inside} position={bot.Position} {details}\n");
        }
        catch (Exception error)
        {
            disabled = true;
            Plugin.Log.LogWarning($"Could not write bot history: {error.Message}");
        }
    }
}
