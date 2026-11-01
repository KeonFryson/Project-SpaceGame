using System.Collections.Generic;

/// <summary>
/// Everything that must survive a solar system being regenerated / destroyed: remaining deposits,
/// the stockpile, ownership, buildings, power, population and the build queues.
/// Keyed by (systemId, bodyIndex). Pure data, no GameObjects.
/// </summary>
public class BodyState
{
    public readonly int systemId;
    public readonly int bodyIndex;

    /// <summary>Indexed by (int)ResourceType; only the Deposited types are used.</summary>
    public readonly float[] deposits = new float[ResourceInfo.Count];
    public readonly float[] depositsMax = new float[ResourceInfo.Count];

    public float fertility;            // 0..1
    public string anomalyName;         // null = none
    public bool owned;
    public bool isHome;
    public readonly ResourceStock stock = new ResourceStock();

    // ---- buildings
    public int slots;                                                   // from body size
    public readonly int[] buildings = new int[Buildings.Count];        // finished, indexed by (int)BuildingType
    public readonly List<BuildJob> queue = new List<BuildJob>();       // front = being built
    public readonly List<ShipJob> shipQueue = new List<ShipJob>();     // front = being built (needs a Shipyard)

    // ---- power and population
    public float solar = 1f;           // solar output multiplier (star class and orbit distance)
    public float battery;              // stored energy (saved)
    public float population;           // saved
    // recomputed every tick, NOT saved
    public float energyMade, energyUsed, efficiency = 1f;
    public bool starving;

    public BodyState(int systemId, int bodyIndex)
    {
        this.systemId = systemId;
        this.bodyIndex = bodyIndex;
    }

    public static long Key(int systemId, int bodyIndex) => ((long)systemId << 32) | (uint)bodyIndex;

    public float Deposit(ResourceType t) => deposits[(int)t];
    public float DepositMax(ResourceType t) => depositsMax[(int)t];

    public bool HasAnyDeposit
    {
        get
        {
            foreach (var t in ResourceInfo.Deposited)
                if (depositsMax[(int)t] > 0f) return true;
            return false;
        }
    }

    /// <summary>Takes up to amount out of the ground. Returns what was taken.</summary>
    public float Extract(ResourceType t, float amount)
    {
        int i = (int)t;
        float taken = amount < 0f ? 0f : (amount > deposits[i] ? deposits[i] : amount);
        deposits[i] -= taken;
        return taken;
    }

    // ------------------------------------------------------------------ buildings

    public int Built(BuildingType t) => buildings[(int)t];

    public int Queued(BuildingType t)
    {
        int n = 0;
        for (int i = 0; i < queue.Count; i++) if (queue[i].type == t) n++;
        return n;
    }

    // ------------------------------------------------------------------ save helpers

    public void ExportQueue(out int[] types, out float[] remaining)
    {
        types = new int[queue.Count];
        remaining = new float[queue.Count];
        for (int i = 0; i < queue.Count; i++)
        {
            types[i] = (int)queue[i].type;
            remaining[i] = queue[i].remaining;
        }
    }

    public void ExportShipQueue(out int[] types, out float[] remaining)
    {
        types = new int[shipQueue.Count];
        remaining = new float[shipQueue.Count];
        for (int i = 0; i < shipQueue.Count; i++)
        {
            types[i] = (int)shipQueue[i].cls;
            remaining[i] = shipQueue[i].remaining;
        }
    }

    public void LoadBuildings(int[] src)
    {
        if (src == null) return;
        System.Array.Copy(src, buildings, System.Math.Min(src.Length, buildings.Length));
    }

    public void LoadQueue(int[] types, float[] remaining)
    {
        queue.Clear();
        if (types == null || remaining == null) return;
        int n = System.Math.Min(types.Length, remaining.Length);
        for (int i = 0; i < n; i++)
            if (types[i] >= 0 && types[i] < Buildings.Count)
                queue.Add(new BuildJob { type = (BuildingType)types[i], remaining = remaining[i] });
    }

    public void LoadShipQueue(int[] types, float[] remaining)
    {
        shipQueue.Clear();
        if (types == null || remaining == null) return;
        int n = System.Math.Min(types.Length, remaining.Length);
        for (int i = 0; i < n; i++)
            if (types[i] >= 0 && types[i] < ShipDefs.Count)
                shipQueue.Add(new ShipJob { cls = (ShipClass)types[i], remaining = remaining[i] });
    }
}
