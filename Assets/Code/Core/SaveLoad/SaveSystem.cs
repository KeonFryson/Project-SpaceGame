using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class BodySave
{
    public int systemId, bodyIndex;
    public float[] deposits, depositsMax, stock;
    public float fertility, stockCapacity;
    public string anomalyName;      // JsonUtility turns null into ""
    public bool owned, isHome;

    // v2: buildings
    public int slots;
    public int[] buildings, queueTypes;
    public float[] queueRemaining;

    // v3: power, population, ship production
    public float solar, battery, population;
}

[Serializable]
public class SaveData
{
    public int version = SaveSystem.CurrentVersion;
    public string timestamp;
    public long ticks;

    // galaxy generation (so a changed inspector value can't corrupt an old save)
    public int galaxySeed, systemCount, shape, spiralArms;
    public float galaxyRadius, spiralTwist, minSystemDistance;

    public int currentSystemId;
    public string currentSystemName;

    public int homeSystemId, homeBodyIndex;
    public float researchData;
    public float gameTime;
    public int speedIndex = 1;

    public bool hasCamera;
    public float camX, camY, camZ, orthoSize;

    public List<BodySave> bodies = new List<BodySave>();
}

/// <summary>JSON files in persistentDataPath/Saves. Slot 0 = autosave, 1..SlotCount = manual slots.</summary>
public static class SaveSystem
{
    public const int SlotCount = 3;
    public const int CurrentVersion = 3;

    static string Dir => Path.Combine(Application.persistentDataPath, "Saves");

    public static string PathFor(int slot) =>
        Path.Combine(Dir, slot == 0 ? "autosave.json" : "slot" + slot + ".json");

    public static bool Exists(int slot) => File.Exists(PathFor(slot));

    public static bool Write(int slot, SaveData data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string path = PathFor(slot), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("SaveSystem.Write failed: " + e.Message);
            return false;
        }
    }

    public static SaveData Read(int slot)
    {
        try
        {
            if (!Exists(slot)) return null;
            var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFor(slot)));
            return d == null || d.version > CurrentVersion ? null : d;
        }
        catch (Exception e)
        {
            Debug.LogError("SaveSystem.Read failed: " + e.Message);
            return null;
        }
    }

    public static void Delete(int slot)
    {
        try { if (Exists(slot)) File.Delete(PathFor(slot)); }
        catch (Exception e) { Debug.LogError("SaveSystem.Delete failed: " + e.Message); }
    }

    /// <summary>Slot with the newest save, or -1 if there are none.</summary>
    public static int MostRecentSlot()
    {
        int best = -1;
        long bestTicks = long.MinValue;
        for (int s = 0; s <= SlotCount; s++)
        {
            var d = Read(s);
            if (d != null && d.ticks >= bestTicks) { bestTicks = d.ticks; best = s; }
        }
        return best;
    }
}
