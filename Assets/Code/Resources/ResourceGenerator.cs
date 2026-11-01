using System.Collections.Generic;
using UnityEngine;

public struct AnomalyInfo
{
    public bool present;
    public int bodyIndex;     // the body that carries the exotic deposit
    public string name;
}

/// <summary>
/// Turns a body's type + seed into deposits, deterministically (same seed = same deposits).
/// It uses its OWN random streams (seeded via SeedUtil.Mix), so SolarSystemGenerator's rolls,
/// and therefore every existing seed, are untouched.
/// </summary>
public static class ResourceGenerator
{
    static readonly string[] AnomalyNames =
    {
        "Gravitic Rift", "Temporal Echo", "Dark Matter Cloud", "Derelict Beacon", "Quantum Scar", "Void Bloom"
    };

    /// <summary>Does this system have an anomaly, and which body carries it?</summary>
    public static AnomalyInfo RollAnomaly(SolarSystemData sd)
    {
        var rng = new System.Random(SeedUtil.Mix(sd.seed, 7777));
        if (rng.NextDouble() >= GameConfig.AnomalyChance) return default;

        var candidates = new List<int>();
        for (int i = 1; i < sd.bodies.Count; i++)
        {
            var b = sd.bodies[i];
            if (b.isBelt) continue;
            if (b.type == PlanetType.Star || b.type == PlanetType.Asteroid ||
                b.type == PlanetType.GasGiant || b.type == PlanetType.RingedGiant) continue;
            candidates.Add(i);
        }
        if (candidates.Count == 0) return default;

        return new AnomalyInfo
        {
            present = true,
            bodyIndex = candidates[rng.Next(candidates.Count)],
            name = AnomalyNames[rng.Next(AnomalyNames.Length)]
        };
    }

    public static BodyState Generate(SolarSystemData sd, int bodyIndex)
    {
        var b = sd.bodies[bodyIndex];
        var st = new BodyState(sd.id, bodyIndex);
        if (b.type == PlanetType.Star) return st;

        var rng = new System.Random(SeedUtil.Mix(b.seed, 4242));
        var table = GameConfig.RichnessTable(b.type);
        float size = Mathf.Clamp(b.diameter / 5f, 0.5f, 2f);
        var anomaly = RollAnomaly(sd);
        bool isAnomalyBody = anomaly.present && anomaly.bodyIndex == bodyIndex;

        for (int k = 0; k < ResourceInfo.Deposited.Length; k++)
        {
            var t = ResourceInfo.Deposited[k];
            // always draw both numbers so the stream stays aligned whatever the branch
            float jitter = 0.75f + 0.5f * (float)rng.NextDouble();
            float roll = (float)rng.NextDouble();

            float amount;
            if (t == ResourceType.ExoticMatter)
            {
                amount = 0f;
                if (isAnomalyBody) amount = GameConfig.ExoticAnomalyAmount * jitter;
                else if (table[k] > 0 && roll < GameConfig.ExoticChance) amount = GameConfig.ExoticRareAmount * jitter;
            }
            else
            {
                amount = GameConfig.RichnessAmount(table[k]) * size * jitter;
            }

            amount = Mathf.Round(amount);
            st.deposits[(int)t] = amount;
            st.depositsMax[(int)t] = amount;
        }

        st.fertility = GameConfig.Fertility(b.type);
        if (isAnomalyBody) st.anomalyName = anomaly.name;
        st.slots = Buildings.SlotsFor(b.diameter);
        Buildings.SlotsFor(b.diameter);
        return st;
    }
}