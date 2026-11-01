using System.Text;
using UnityEngine;

public enum ShipClass
{
    Packet, Cutter, Collier, Oiler, Tender, Transport,          // Support
    Corvette, Frigate, Destroyer, Cruiser,                      // Combat
    Battlecruiser, Battleship, Carrier, Dreadnought             // Capital
}

public enum ShipRole { Support, Combat, Capital }
public enum ShipYard { Shipyard, SpaceDock, CapitalSlipway }

/// <summary>One entry in a body's ship production queue.</summary>
public class ShipJob
{
    public ShipClass cls;
    public float remaining;   // game seconds
}

public class ShipDef
{
    public ShipClass cls;
    public string name;
    public ShipRole role;
    public ShipYard yard;
    public string sizeName;
    public float cargo;          // physical resources only
    public float fuelTank;
    public float speed;          // world units per game second
    public float fuelPerUnit;    // fuel burned per world unit travelled
    public float buildTime;
    public float scale;          // visual size multiplier
    public bool buildable;       // false = defined but locked until its yard / combat exists
    public Cost[] cost;
}

/// <summary>
/// All ship stats and rules. Tuning lives here. Defs are in the same order as ShipClass.
/// Only the small Support ships are buildable for now: the Space Dock, Capital Slipway and
/// combat are Planned (see the GDD roadmap).
/// </summary>
public static class ShipDefs
{
    public const int MaxQueue = 3;
    public const float LaunchFuel = 0.4f;   // new ships launch with 40% fuel
    public static readonly int Count = System.Enum.GetValues(typeof(ShipClass)).Length;

    static Cost M(float a) => new Cost(ResourceType.Minerals, a);
    static Cost R(float a) => new Cost(ResourceType.RareMetals, a);
    static Cost G(float a) => new Cost(ResourceType.Gas, a);
    static Cost X(float a) => new Cost(ResourceType.ExoticMatter, a);

    static ShipDef D(ShipClass c, ShipRole role, ShipYard yard, string size, float cargo, float tank,
                     float speed, float burn, float time, float scale, bool buildable, params Cost[] cost) =>
        new ShipDef
        {
            cls = c, name = c.ToString(), role = role, yard = yard, sizeName = size, cargo = cargo,
            fuelTank = tank, speed = speed, fuelPerUnit = burn, buildTime = time, scale = scale,
            buildable = buildable, cost = cost
        };

    public static readonly ShipDef[] Defs =
    {
        //   class                role              yard                    size      cargo tank speed burn  time scale buildable cost
        D(ShipClass.Packet,       ShipRole.Support, ShipYard.Shipyard,      "Small",   200, 100,  8f, 0.15f,  30f, 1.0f, true,  M(200), R(20)),
        D(ShipClass.Cutter,       ShipRole.Support, ShipYard.Shipyard,      "Small",    40, 150, 16f, 0.08f,  25f, 0.8f, true,  M(150), R(30), G(20)),
        D(ShipClass.Collier,      ShipRole.Support, ShipYard.SpaceDock,     "Medium", 1000, 300,  6f, 0.40f,  60f, 1.6f, false, M(600), R(100)),
        D(ShipClass.Oiler,        ShipRole.Support, ShipYard.SpaceDock,     "Medium",  600, 400,  6f, 0.30f,  60f, 1.5f, false, M(500), R(80), G(100)),
        D(ShipClass.Tender,       ShipRole.Support, ShipYard.SpaceDock,     "Medium",  100, 250,  5f, 0.30f,  70f, 1.5f, false, M(700), R(150)),
        D(ShipClass.Transport,    ShipRole.Support, ShipYard.SpaceDock,     "Medium",  400, 300,  7f, 0.30f,  60f, 1.5f, false, M(500), R(100)),
        D(ShipClass.Corvette,     ShipRole.Combat,  ShipYard.Shipyard,      "Small",    20, 120, 14f, 0.10f,  30f, 0.9f, false, M(250), R(60)),
        D(ShipClass.Frigate,      ShipRole.Combat,  ShipYard.Shipyard,      "Small",    30, 150, 12f, 0.12f,  40f, 1.1f, false, M(350), R(90)),
        D(ShipClass.Destroyer,    ShipRole.Combat,  ShipYard.SpaceDock,     "Medium",   50, 250, 10f, 0.20f,  60f, 1.5f, false, M(600), R(180)),
        D(ShipClass.Cruiser,      ShipRole.Combat,  ShipYard.SpaceDock,     "Medium",   80, 350,  8f, 0.30f,  80f, 1.8f, false, M(900), R(300)),
        D(ShipClass.Battlecruiser,ShipRole.Capital, ShipYard.CapitalSlipway,"Capital", 100, 600,  9f, 0.50f, 120f, 2.5f, false, M(2000), R(800), X(50)),
        D(ShipClass.Battleship,   ShipRole.Capital, ShipYard.CapitalSlipway,"Capital", 150, 600,  5f, 0.60f, 150f, 3.0f, false, M(3000), R(1000), X(80)),
        D(ShipClass.Carrier,      ShipRole.Capital, ShipYard.CapitalSlipway,"Capital", 300, 700,  5f, 0.60f, 150f, 3.0f, false, M(2500), R(900), X(80)),
        D(ShipClass.Dreadnought,  ShipRole.Capital, ShipYard.CapitalSlipway,"Capital", 300, 900,  4f, 0.80f, 240f, 3.5f, false, M(5000), R(2000), X(200)),
    };

    public static ShipDef Get(ShipClass c) => Defs[(int)c];
    public static ShipDef Get(int cls) => Defs[Mathf.Clamp(cls, 0, Defs.Length - 1)];

    static string YardName(ShipYard y) =>
        y == ShipYard.Shipyard ? "Shipyard" : y == ShipYard.SpaceDock ? "Space Dock" : "Capital Slipway";

    // ------------------------------------------------------------------ building ships

    /// <summary>Null if the ship can be queued here right now, otherwise the reason it can't.</summary>
    public static string CanBuild(BodyState st, ShipDef d)
    {
        if (!st.owned) return "Not owned";
        if (!d.buildable) return d.role == ShipRole.Combat ? "Combat arrives later" : "Needs a " + YardName(d.yard) + " (planned)";
        if (st.Built(BuildingType.Shipyard) == 0) return "Needs a Shipyard";
        if (st.shipQueue.Count >= MaxQueue) return "Ship queue is full";
        foreach (var c in d.cost)
            if (!st.stock.Has(c.type, c.amount))
                return "Needs " + Mathf.CeilToInt(c.amount - st.stock.Get(c.type)) + " more " + ResourceInfo.Name(c.type);
        return null;
    }

    public static bool TryQueue(BodyState st, ShipClass c, out string reason)
    {
        var d = Get(c);
        reason = CanBuild(st, d);
        if (reason != null) return false;
        foreach (var cost in d.cost) st.stock.TryRemove(cost.type, cost.amount);
        st.shipQueue.Add(new ShipJob { cls = c, remaining = d.buildTime });
        return true;
    }

    /// <summary>Removes a queued ship and refunds its full cost.</summary>
    public static void Cancel(BodyState st, int index)
    {
        if (index < 0 || index >= st.shipQueue.Count) return;
        foreach (var c in Get(st.shipQueue[index].cls).cost) st.stock.Add(c.type, c.amount);
        st.shipQueue.RemoveAt(index);
    }

    public static string CostText(BodyState st, ShipDef d)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < d.cost.Length; i++)
        {
            var c = d.cost[i];
            if (i > 0) sb.Append("  ");
            bool ok = st.stock.Has(c.type, c.amount);
            sb.Append(ok ? "" : "<color=#ff7777>").Append(c.amount.ToString("0")).Append(' ')
              .Append(ResourceInfo.Name(c.type)).Append(ok ? "" : "</color>");
        }
        return sb.ToString();
    }

    public static string StatsText(ShipDef d) =>
        "Cargo " + d.cargo.ToString("0") + "  -  Fuel " + d.fuelTank.ToString("0") + "  -  Speed " + d.speed.ToString("0");
}

/// <summary>Instant cargo and fuel transfers between a docked ship and a body's stockpile.</summary>
public static class ShipOps
{
    /// <summary>Moves up to amount from the stockpile into the hold. Returns how much moved.</summary>
    public static float Load(ShipData s, ShipDef def, BodyState st, ResourceType t, float amount)
    {
        float free = def.cargo - s.CargoTotal();
        float n = Mathf.Min(amount, st.stock.Get(t), free);
        if (n <= 0f) return 0f;
        st.stock.Remove(t, n);
        s.cargo[(int)t] += n;
        return n;
    }

    /// <summary>Moves up to amount from the hold into the stockpile (limited by free space there).</summary>
    public static float Unload(ShipData s, BodyState st, ResourceType t, float amount)
    {
        float n = Mathf.Min(amount, s.cargo[(int)t]);
        if (n <= 0f) return 0f;
        float stored = st.stock.Add(t, n);
        s.cargo[(int)t] -= stored;
        return stored;
    }

    /// <summary>Fills the tank from the stockpile's Fuel.</summary>
    public static float Refuel(ShipData s, ShipDef def, BodyState st)
    {
        float need = def.fuelTank - s.fuel;
        if (need <= 0f) return 0f;
        float got = st.stock.Remove(ResourceType.Fuel, need);
        s.fuel += got;
        return got;
    }
}
