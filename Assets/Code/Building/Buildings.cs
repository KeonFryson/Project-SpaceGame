using System.Collections.Generic;
using System.Text;
using UnityEngine;

// NOTE: new types are appended at the END so old saves (which store counts by enum index) stay valid.
public enum BuildingType
{
    ColonyHub, MineralMine, RareMetalMine, Farm, GasSkimmer,
    SolarCollector, PowerPlant, BatteryBank, Refinery, Shipyard
}

public struct Cost
{
    public ResourceType type;
    public float amount;
    public Cost(ResourceType type, float amount) { this.type = type; this.amount = amount; }
}

/// <summary>One entry in the construction queue of a body.</summary>
public class BuildJob
{
    public BuildingType type;
    public float remaining;   // game seconds
}

public class BuildingDef
{
    public BuildingType type;
    public string name, description, tag;
    public Cost[] cost;
    public float buildTime;              // game seconds
    public int maxPerBody = 99;
    public int slotCost = 1;             // 0 = core building, uses no slot
    public bool needsHub = true;

    // power: + makes energy, - uses energy (per building, per second)
    public float power;
    public bool scalesWithSolar;         // output is multiplied by the body's solar factor
    public float gasBurn;                // Gas burned per second to produce the power (Power Plant)
    public float batteryCapacity;

    // production (per building, per game second)
    public bool hasOutput;
    public ResourceType output;
    public float rate;
    public bool hasInput;                // converts: consumes `input` at inputRate to make `output` (Refinery)
    public ResourceType input;
    public float inputRate;
    public bool extractsDeposit;         // drains the body's deposit of the same resource
    public bool scalesWithFertility;
    public bool needsFertility;
}

/// <summary>
/// Building data and rules. ALL building tuning lives here (costs, times, rates, power, caps).
/// Defs must stay in the same order as the BuildingType enum.
/// </summary>
public static class Buildings
{
    public const int MaxQueue = 4;
    public static readonly int Count = System.Enum.GetValues(typeof(BuildingType)).Length;

    static Cost M(float a) => new Cost(ResourceType.Minerals, a);
    static Cost R(float a) => new Cost(ResourceType.RareMetals, a);

    public static readonly BuildingDef[] Defs =
    {
        new BuildingDef { type = BuildingType.ColonyHub, name = "Colony Hub", description = "Core. Unlocks other buildings.",
            tag = "Housing 100", cost = new[] { M(400f), R(60f) }, buildTime = 30f, maxPerBody = 1, slotCost = 0,
            needsHub = false, power = 6f },

        new BuildingDef { type = BuildingType.MineralMine, name = "Mineral Mine", description = "Mines Minerals.",
            cost = new[] { M(150f) }, buildTime = 15f, maxPerBody = 4, power = -3f,
            hasOutput = true, output = ResourceType.Minerals, rate = 2f, extractsDeposit = true },

        new BuildingDef { type = BuildingType.RareMetalMine, name = "Rare Metal Mine", description = "Mines Rare Metals (slow).",
            cost = new[] { M(250f), R(40f) }, buildTime = 25f, maxPerBody = 2, power = -4f,
            hasOutput = true, output = ResourceType.RareMetals, rate = 0.4f, extractsDeposit = true },

        new BuildingDef { type = BuildingType.Farm, name = "Farm", description = "Grows Food.",
            cost = new[] { M(120f) }, buildTime = 12f, maxPerBody = 4, power = -2f,
            hasOutput = true, output = ResourceType.Food, rate = 1.5f, scalesWithFertility = true, needsFertility = true },

        new BuildingDef { type = BuildingType.GasSkimmer, name = "Gas Skimmer", description = "Skims Gas from a gas giant.",
            cost = new[] { M(200f), R(30f) }, buildTime = 20f, maxPerBody = 3, power = -4f,
            hasOutput = true, output = ResourceType.Gas, rate = 1f, extractsDeposit = true },

        new BuildingDef { type = BuildingType.SolarCollector, name = "Solar Collector", description = "Energy from starlight.",
            cost = new[] { M(100f) }, buildTime = 10f, maxPerBody = 6, power = 5f, scalesWithSolar = true },

        new BuildingDef { type = BuildingType.PowerPlant, name = "Power Plant", description = "Steady Energy, burns Gas.",
            cost = new[] { M(250f), R(40f) }, buildTime = 25f, maxPerBody = 2, power = 15f, gasBurn = 0.25f },

        new BuildingDef { type = BuildingType.BatteryBank, name = "Battery Bank", description = "Stores surplus Energy.",
            cost = new[] { M(150f), R(20f) }, buildTime = 15f, maxPerBody = 3, batteryCapacity = 400f },

        new BuildingDef { type = BuildingType.Refinery, name = "Refinery", description = "Turns Gas into Fuel.",
            cost = new[] { M(200f), R(30f) }, buildTime = 20f, maxPerBody = 2, power = -4f,
            hasInput = true, input = ResourceType.Gas, inputRate = 1f,
            hasOutput = true, output = ResourceType.Fuel, rate = 0.5f },

        new BuildingDef { type = BuildingType.Shipyard, name = "Shipyard", description = "Builds small ships.",
            tag = "Builds ships", cost = new[] { M(300f), R(60f) }, buildTime = 25f, maxPerBody = 1, power = -3f },
    };

    public static BuildingDef Def(BuildingType t) => Defs[(int)t];

    /// <summary>Building slots from body diameter (planets 3-6, gas giants 9-15, moons about 1-2).</summary>
    public static int SlotsFor(float diameter) => Mathf.Clamp(Mathf.RoundToInt(diameter * 1.5f), 3, 12);

    /// <summary>Built plus queued.</summary>
    public static int Total(BodyState st, BuildingType t) => st.buildings[(int)t] + st.Queued(t);

    public static int SlotsUsed(BodyState st)
    {
        int used = 0;
        for (int i = 0; i < Count; i++)
            used += Total(st, (BuildingType)i) * Defs[i].slotCost;
        return used;
    }

    public static float EffectiveRate(BodyState st, BuildingDef d) =>
        d.rate * (d.scalesWithFertility ? st.fertility : 1f);

    /// <summary>Null if the building can be queued right now, otherwise the reason it can't.</summary>
    public static string CanBuild(BodyState st, BuildingDef d)
    {
        if (!st.owned) return "Not owned";
        if (d.needsHub && st.Built(BuildingType.ColonyHub) == 0) return "Needs a Colony Hub";
        if (d.extractsDeposit)
        {
            if (st.DepositMax(d.output) <= 0f) return "No " + ResourceInfo.Name(d.output) + " deposit here";
            if (st.Deposit(d.output) <= 0f) return "Deposit depleted";
        }
        if (d.needsFertility && st.fertility <= 0f) return "Nothing will grow here";
        if (Total(st, d.type) >= d.maxPerBody)
            return d.maxPerBody == 1 ? "Already built or queued" : "Limit reached (" + d.maxPerBody + ")";
        if (d.slotCost > 0 && SlotsUsed(st) + d.slotCost > st.slots) return "No free building slots";
        if (st.queue.Count >= MaxQueue) return "Build queue is full";
        foreach (var c in d.cost)
            if (!st.stock.Has(c.type, c.amount))
                return "Needs " + Mathf.CeilToInt(c.amount - st.stock.Get(c.type)) + " more " + ResourceInfo.Name(c.type);
        return null;
    }

    /// <summary>Pays the cost from this body's own stockpile and queues the building.</summary>
    public static bool TryQueue(BodyState st, BuildingType t, out string reason)
    {
        var d = Def(t);
        reason = CanBuild(st, d);
        if (reason != null) return false;
        foreach (var c in d.cost) st.stock.TryRemove(c.type, c.amount);
        st.queue.Add(new BuildJob { type = t, remaining = d.buildTime });
        return true;
    }

    /// <summary>Removes a queued job and refunds its full cost.</summary>
    public static void Cancel(BodyState st, int index)
    {
        if (index < 0 || index >= st.queue.Count) return;
        foreach (var c in Def(st.queue[index].type).cost) st.stock.Add(c.type, c.amount);
        st.queue.RemoveAt(index);
    }

    // ------------------------------------------------------------------ text

    public static string CostText(BodyState st, BuildingDef d)
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

    public static string EffectText(BodyState st, BuildingDef d)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(d.tag)) parts.Add(d.tag);
        if (d.hasOutput) parts.Add("+" + EffectiveRate(st, d).ToString("0.0") + "/s " + ResourceInfo.Name(d.output));
        if (d.hasInput) parts.Add("uses " + d.inputRate.ToString("0.0") + "/s " + ResourceInfo.Name(d.input));
        if (d.power > 0f) parts.Add("+" + (d.power * (d.scalesWithSolar ? st.solar : 1f)).ToString("0.#") + " energy");
        if (d.power < 0f) parts.Add("uses " + (-d.power).ToString("0.#") + " energy");
        if (d.gasBurn > 0f) parts.Add("burns " + d.gasBurn.ToString("0.##") + " Gas/s");
        if (d.batteryCapacity > 0f) parts.Add("stores " + d.batteryCapacity.ToString("0") + " energy");
        return parts.Count > 0 ? string.Join("  -  ", parts) : d.description;
    }

    /// <summary>Rich text for the info panel: slots, finished buildings, construction and ship production.</summary>
    public static string Describe(BodyState st)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Slots: " + SlotsUsed(st) + " / " + st.slots);

        bool any = false;
        for (int i = 0; i < Count; i++)
        {
            int n = st.buildings[i];
            if (n == 0) continue;
            any = true;
            var d = Defs[i];
            sb.Append("<b>").Append(d.name).Append("</b> x").Append(n);
            if (d.hasOutput)
            {
                bool dry = d.extractsDeposit && st.Deposit(d.output) <= 0f;
                sb.Append("  <color=#888888>")
                  .Append(dry ? "depleted" : "+" + (EffectiveRate(st, d) * n).ToString("0.0") + "/s " + ResourceInfo.Name(d.output))
                  .Append("</color>");
            }
            sb.AppendLine();
        }
        if (!any) sb.AppendLine("<color=#888888>None</color>");

        if (st.queue.Count > 0)
        {
            sb.AppendLine("<color=#ffcc55>Under construction</color>");
            for (int i = 0; i < st.queue.Count; i++)
                sb.AppendLine(Def(st.queue[i].type).name + "  <color=#888888>" +
                              (i == 0 ? Mathf.CeilToInt(st.queue[i].remaining) + "s left" : "waiting") + "</color>");
        }
        if (st.shipQueue.Count > 0)
        {
            sb.AppendLine("<color=#ffcc55>Ship production</color>");
            for (int i = 0; i < st.shipQueue.Count; i++)
                sb.AppendLine(ShipDefs.Get(st.shipQueue[i].cls).name + "  <color=#888888>" +
                              (i == 0 ? Mathf.CeilToInt(st.shipQueue[i].remaining) + "s left" : "waiting") + "</color>");
        }
        return sb.ToString();
    }
}
