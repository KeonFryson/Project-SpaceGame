using System.Text;
using UnityEngine;

/// <summary>
/// Per-location power grid and population. Energy is local: it is never carried by ships.
/// Brownout: when demand exceeds supply (and the batteries are empty) ALL production on that
/// body slows in proportion (BodyState.efficiency). Nothing stops outright.
/// Staffing works the same way: if there are fewer people than the buildings need, ALL production
/// slows in proportion (BodyState.staffing).
/// Population eats Food, grows when fed (faster the bigger it is), and starves when not.
/// Housing (Colony Hubs) is the real cap on how much you can run.
/// </summary>
public static class Colony
{
    // ---- tuning (population)
    public const float HousingPerHub = 100f;
    public const float StartPopulation = 20f;
    public const float FoodPerPerson = 0.02f;      // Food per person per second
    public const float GrowthBase = 0.2f;          // people per second while fed
    public const float GrowthPerPerson = 0.01f;    // extra people per second, per person already there
    public const float DeclinePerSec = 0.2f;       // people per second while starving
    public const float MinPopulation = 1f;
    public const float StarvationOutput = 0.5f;    // output multiplier while starving

    public static float BatteryCapacity(BodyState st)
    {
        float cap = 0f;
        for (int i = 0; i < Buildings.Count; i++)
            cap += Buildings.Defs[i].batteryCapacity * st.buildings[i];
        return cap;
    }

    /// <summary>Updates energy, efficiency, batteries, Food, population and staffing. dt &gt; 0.</summary>
    public static void Tick(BodyState st, float dt)
    {
        // ---------------- power
        float made = 0f, used = 0f, cap = 0f;
        for (int i = 0; i < Buildings.Count; i++)
        {
            int n = st.buildings[i];
            if (n == 0) continue;
            var d = Buildings.Defs[i];

            if (d.power > 0f)
            {
                float p = d.power * n;
                if (d.scalesWithSolar) p *= st.solar;
                if (d.gasBurn > 0f)                       // Power Plants burn Gas whether or not it's needed
                {
                    float need = d.gasBurn * n * dt;
                    float got = st.stock.Remove(ResourceType.Gas, need);
                    p *= need > 0f ? got / need : 0f;     // no Gas, no power
                }
                made += p;
            }
            else if (d.power < 0f)
            {
                used += -d.power * n;
            }
            cap += d.batteryCapacity * n;
        }

        st.energyMade = made;
        st.energyUsed = used;
        st.battery = Mathf.Min(st.battery, cap);

        float eff = 1f;
        if (made >= used)
        {
            st.battery = Mathf.Min(cap, st.battery + (made - used) * dt);
        }
        else
        {
            float need = (used - made) * dt;
            if (st.battery >= need) st.battery -= need;
            else
            {
                eff = (made + st.battery / dt) / used;     // batteries cover part of the gap
                st.battery = 0f;
            }
        }
        st.efficiency = Mathf.Clamp01(eff);

        // ---------------- workers needed
        float workersNeeded = 0f;
        for (int i = 0; i < Buildings.Count; i++)
            workersNeeded += Buildings.Defs[i].workers * st.buildings[i];
        st.workersNeeded = workersNeeded;

        // ---------------- population
        int hubs = st.Built(BuildingType.ColonyHub);
        if (hubs == 0)
        {
            st.population = 0f;
            st.starving = false;
            st.staffing = 1f;
            return;
        }

        float housing = hubs * HousingPerHub;
        if (st.population <= 0f) st.population = StartPopulation;

        float eat = st.population * FoodPerPerson * dt;
        float have = st.stock.Get(ResourceType.Food);
        if (have >= eat)
        {
            st.stock.Remove(ResourceType.Food, eat);
            st.starving = false;
            if (st.population < housing)
            {
                float growth = GrowthBase + st.population * GrowthPerPerson;
                st.population = Mathf.Min(housing, st.population + growth * dt);
            }
        }
        else
        {
            st.stock.Remove(ResourceType.Food, have);
            st.starving = true;
            st.population = Mathf.Max(MinPopulation, st.population - DeclinePerSec * dt);
        }

        // ---------------- staffing
        st.staffing = workersNeeded > 0f ? Mathf.Clamp01(st.population / workersNeeded) : 1f;
    }

    /// <summary>Output multiplier from brownout, staffing and starvation.</summary>
    public static float OutputMultiplier(BodyState st) =>
        st.efficiency * st.staffing * (st.starving ? StarvationOutput : 1f);

    public static string Describe(BodyState st)
    {
        var sb = new StringBuilder();
        sb.Append("Energy: ").Append(st.energyMade.ToString("0.0")).Append(" made / ")
          .Append(st.energyUsed.ToString("0.0")).Append(" used");
        if (st.efficiency < 0.999f)
            sb.Append("  <color=#ff7777>BROWNOUT ").Append(Mathf.RoundToInt(st.efficiency * 100f)).Append("%</color>");
        sb.AppendLine();

        float cap = BatteryCapacity(st);
        if (cap > 0f) sb.AppendLine("Battery: " + st.battery.ToString("0") + " / " + cap.ToString("0"));
        sb.AppendLine("<color=#888888>Solar output x" + st.solar.ToString("0.00") + "</color>");

        int hubs = st.Built(BuildingType.ColonyHub);
        if (hubs > 0)
        {
            sb.AppendLine("Population: " + st.population.ToString("0") + " / " + (hubs * HousingPerHub).ToString("0") +
                          "  <color=#888888>eats " + (st.population * FoodPerPerson).ToString("0.0") + " Food/s</color>");
            if (st.starving) sb.AppendLine("<color=#ff7777>STARVING - output halved</color>");
        }
        return sb.ToString();
    }
}
