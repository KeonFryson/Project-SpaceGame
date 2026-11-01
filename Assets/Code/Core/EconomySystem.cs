using System;
using UnityEngine;

/// <summary>
/// Ticks every OWNED body with game time, whether or not its system is currently loaded:
/// construction queue first, then production. Created by RTSBootstrap.
/// Nothing here needs GameObjects, so far-away colonies keep producing.
/// </summary>
public class EconomySystem : MonoBehaviour
{
    public static event Action<BodyState, BuildingType> BuildingCompleted;

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
        // construction: one job at a time, front of the queue
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

        // production
        for (int i = 0; i < Buildings.Count; i++)
        {
            int n = st.buildings[i];
            var d = Buildings.Defs[i];
            if (n == 0 || !d.hasOutput) continue;

            float amount = Buildings.EffectiveRate(st, d) * n * dt;
            if (d.extractsDeposit)
            {
                float free = st.stock.capacity - st.stock.Get(d.output);
                amount = Mathf.Min(amount, free);                 // never mine more than fits
                st.stock.Add(d.output, st.Extract(d.output, amount));
            }
            else
            {
                st.stock.Add(d.output, amount);
            }
        }
    }
}
