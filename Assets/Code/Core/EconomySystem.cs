using System;
using UnityEngine;

/// <summary>
/// Ticks every OWNED body with game time, whether or not its system is currently loaded:
/// construction, power / population / staffing (Colony), ship production, then resource production.
/// Nothing here needs GameObjects, so far-away colonies keep producing. Created by RTSBootstrap.
/// </summary>
public class EconomySystem : MonoBehaviour
{
    public static event Action<BodyState, BuildingType> BuildingCompleted;
    public static event Action<BodyState, ShipClass> ShipCompleted;

    void Update()
    {
        var clock = GameClock.Instance;
        var gs = GameState.Instance;
        if (clock == null || gs == null) return;

        float dt = clock.Dt;          // 0 while paused / in menus
        if (dt <= 0f) return;

        var owned = gs.Owned;
        for (int i = 0; i < owned.Count; i++) Tick(owned[i], dt);
    }

    static void Tick(BodyState st, float dt)
    {
        // 1. construction: one job at a time, front of the queue
        if (st.queue.Count > 0)
        {
            var job = st.queue[0];
            job.remaining -= dt;
            if (job.remaining <= 0f)
            {
                st.buildings[(int)job.type]++;
                st.queue.RemoveAt(0);
                BuildingCompleted?.Invoke(st, job.type);
            }
        }

        // 2. power, batteries, Food, population, staffing
        Colony.Tick(st, dt);

        // 3. ship production (slowed by brownouts and by missing crew)
        if (st.shipQueue.Count > 0 && st.Built(BuildingType.Shipyard) > 0)
        {
            var job = st.shipQueue[0];
            job.remaining -= dt * st.efficiency * st.staffing;
            if (job.remaining <= 0f)
            {
                st.shipQueue.RemoveAt(0);
                ShipCompleted?.Invoke(st, job.cls);
            }
        }

        // 4. resource production
        float mult = Colony.OutputMultiplier(st);
        if (mult <= 0f) return;

        for (int i = 0; i < Buildings.Count; i++)
        {
            int n = st.buildings[i];
            var d = Buildings.Defs[i];
            if (n == 0 || !d.hasOutput) continue;

            // diminishing returns: n copies are worth EffectiveCount(d, n), not n
            float eff = Buildings.EffectiveCount(d, n);
            float full = Buildings.EffectiveRate(st, d) * eff * dt * mult;
            if (full <= 0f) continue;

            // never make more than fits in the stockpile
            float frac = Mathf.Min(full, st.stock.capacity - st.stock.Get(d.output)) / full;
            if (frac <= 0f) continue;

            if (d.hasInput)   // converters (Refinery): limited by how much input is in stock
            {
                float need = d.inputRate * eff * dt * mult * frac;
                float got = st.stock.Remove(d.input, need);
                frac *= need > 0f ? got / need : 0f;
            }

            float amount = full * frac;
            if (d.extractsDeposit) amount = st.Extract(d.output, amount);
            st.stock.Add(d.output, amount);
        }
    }
}
