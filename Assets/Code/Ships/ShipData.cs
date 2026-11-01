using System;
using UnityEngine;

/// <summary>
/// A ship as plain data (saved with the game). The Ship component is only a view of this,
/// spawned when the ship's system is loaded. Ships in other systems are frozen.
/// Positions are system-local (relative to the SolarSystemSpawner).
///
/// Automation: a ship can shuttle between two OWNED bodies (A and B) in its system, moving resources
/// by per-resource rules and refuelling itself. Old saves simply have these fields at their defaults.
/// </summary>
[Serializable]
public class ShipData
{
    public const int Idle = 0, Moving = 1, Docked = 2;

    // per-resource route rule
    public const int RuleOff = 0, RuleAToB = 1, RuleBToA = 2, RuleBalance = 3, RuleCount = 4;

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

    // ---- automation
    public bool autoOn;
    public int autoA = -1, autoB = -1;                              // body indices in this ship's system
    public int[] autoRules = new int[ResourceInfo.Count];           // indexed by (int)ResourceType, values Rule*
    public bool autoRefuel = true;                                  // top up the tank from the local stockpile
    public float autoReserve = 100f;                                // never take the source stockpile below this

    public bool RouteValid => autoA >= 0 && autoB >= 0 && autoA != autoB;

    public void EnsureCargo()
    {
        if (cargo == null || cargo.Length != ResourceInfo.Count)
        {
            var n = new float[ResourceInfo.Count];
            if (cargo != null) Array.Copy(cargo, n, Math.Min(cargo.Length, n.Length));
            cargo = n;
        }
        EnsureAuto();
    }

    public void EnsureAuto()
    {
        if (autoRules != null && autoRules.Length == ResourceInfo.Count) return;
        var n = new int[ResourceInfo.Count];
        if (autoRules != null) Array.Copy(autoRules, n, Math.Min(autoRules.Length, n.Length));
        autoRules = n;
    }

    /// <summary>Copies the route setup (not the on/off switch) from another ship.</summary>
    public void CopyRouteFrom(ShipData o)
    {
        if (o == null || o == this) return;
        o.EnsureAuto();
        EnsureAuto();
        autoA = o.autoA;
        autoB = o.autoB;
        autoRefuel = o.autoRefuel;
        autoReserve = o.autoReserve;
        Array.Copy(o.autoRules, autoRules, autoRules.Length);
    }

    public float CargoTotal()
    {
        float sum = 0f;
        foreach (var t in ResourceInfo.Physical) sum += cargo[(int)t];
        return sum;
    }
}
