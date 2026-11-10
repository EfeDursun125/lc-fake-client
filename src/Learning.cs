using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LethalFakeClient;

internal sealed class Learning
{
    [Serializable]
    private sealed class Entry
    {
        public string key = "";
        public float mean;
        public int count;
    }

    [Serializable]
    private sealed class SaveData
    {
        public int version = 1;
        public List<Entry> entries = new List<Entry>();
    }

    private const int MaxEntries = 2048;
    private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
    private readonly string file = Path.Combine(BepInEx.Paths.ConfigPath, "lethal-fake-client.learning.json");
    private bool dirty;

    internal Learning()
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 512 * 1024)
                return;

            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(file));
            if (data == null || data.version != 1 || data.entries == null)
                return;

            foreach (var entry in data.entries)
            {
                if (entries.Count >= MaxEntries)
                    break;
                if (entry != null && !string.IsNullOrEmpty(entry.key) && entry.count > 0 && !float.IsNaN(entry.mean))
                {
                    entry.mean = Mathf.Clamp(entry.mean, -1f, 1f);
                    entry.count = Mathf.Min(entry.count, 64);
                    entries[entry.key] = entry;
                }
            }
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not load Director learning: {error.Message}");
        }
    }

    internal float Estimate(string key)
    {
        return entries.TryGetValue(key, out var entry) ? entry.mean * entry.count / (entry.count + 8f) : 0f;
    }

    internal void Observe(string key, float reward)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            if (entries.Count >= MaxEntries)
                return;
            entry = new Entry { key = key };
            entries.Add(key, entry);
        }

        entry.count = Mathf.Min(entry.count + 1, 64);
        entry.mean += (Mathf.Clamp(reward, -1f, 1f) - entry.mean) / Mathf.Clamp(entry.count, 4, 24);
        dirty = true;
    }

    internal void Save()
    {
        if (!dirty)
            return;

        string temporary = file + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(temporary, JsonUtility.ToJson(new SaveData { entries = new List<Entry>(entries.Values) }));
            if (File.Exists(file))
                File.Replace(temporary, file, null);
            else
                File.Move(temporary, file);
            dirty = false;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not save Director learning: {error.Message}");
        }
    }
}
