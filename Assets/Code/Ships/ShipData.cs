using System;
using UnityEngine;

/// <summary>
/// A ship as plain data (saved with the game). The Ship component is only a view of this,
/// spawned when the ship's system is loaded. Ships in other systems are frozen.
/// Positions are system-local (relative to the SolarSystemSpawner).
/// </summary>
[Serializable]
public class ShipData
{
    public const int Idle = 0, Moving = 1, Docked = 2;

    public int id;
    public int cls;                  // (int)ShipClass
    public string name;
    public int systemId;

    public Vector2 pos;
    public float fuel;
    public float[] cargo = new float[ResourceInfo.Count];

    public int mode;                 // Idle / Moving / Docked
    public int dockedBody = -1;      // body index while Docked
    public Vector2 dockOffset;       // where the ship sits relative to that body
    public int targetBody = -1;      // dock order: body index; -1 = move to targetPos
    public Vector2 targetPos;

    public void EnsureCargo()
    {
        if (cargo != null && cargo.Length == ResourceInfo.Count) return;
        var n = new float[ResourceInfo.Count];
        if (cargo != null) Array.Copy(cargo, n, Math.Min(cargo.Length, n.Length));
        cargo = n;
    }

    public float CargoTotal()
    {
        float sum = 0f;
        foreach (var t in ResourceInfo.Physical) sum += cargo[(int)t];
        return sum;
    }
}
