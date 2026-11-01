using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The persistent game data that sits next to the (regenerated) solar systems:
/// per-body state, ships, empire-wide Research Data, and which planet is home.
/// Created automatically by RTSBootstrap.
/// </summary>
public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    public GalaxyData Galaxy { get; private set; }
    public int HomeSystemId { get; private set; } = -1;
    public int HomeBodyIndex { get; private set; } = -1;

    /// <summary>Empire-wide pool. Research labs add to it in a later phase.</summary>
    public float ResearchData;

    public event Action NewGameStarted;

    readonly Dictionary<long, BodyState> bodies = new Dictionary<long, BodyState>();
    readonly List<BodyState> owned = new List<BodyState>();
    public IReadOnlyList<BodyState> Owned => owned;

    /// <summary>Every ship in the galaxy. Ships in systems that aren't loaded are frozen.</summary>
    public readonly List<ShipData> Ships = new List<ShipData>();
    int nextShipId;
    public int NextShipId => nextShipId;

    void Awake() => Instance = this;

    // ------------------------------------------------------------------ new game

    /// <summary>Resets everything for a freshly generated galaxy and returns the id of the home system to load.</summary>
    public int InitNewGame(GalaxyData galaxy)
    {
        Galaxy = galaxy;
        bodies.Clear();
        owned.Clear();
        Ships.Clear();
        nextShipId = 0;
        ResearchData = 0f;

        FindHome(galaxy, out int sys, out int body);
        HomeSystemId = sys;
        HomeBodyIndex = body;

        var info = galaxy.systems[sys];
        var sd = SolarSystemGenerator.Generate(info.seed, info.id, info.position);
        var st = ResourceGenerator.Generate(sd, body);
        ApplyHomeOverride(st);
        bodies[BodyState.Key(sys, body)] = st;
        owned.Add(st);

        NewGameStarted?.Invoke();
        return sys;
    }

    static void FindHome(GalaxyData g, out int sysId, out int bodyIndex)
    {
        int n = g.systems.Count;
        int start = new System.Random(g.seed ^ 0x5EED).Next(n);
        int fbSys = -1, fbBody = -1;

        for (int k = 0; k < Mathf.Min(n, 250); k++)
        {
            int id = (start + k) % n;
            var info = g.systems[id];
            var sd = SolarSystemGenerator.Generate(info.seed, info.id, info.position);
            int body = FindHomeBody(sd);
            if (body < 0) continue;

            bool goodStar = sd.starClass == StarClass.Orange || sd.starClass == StarClass.Yellow ||
                            sd.starClass == StarClass.White;
            if (goodStar) { sysId = id; bodyIndex = body; return; }
            if (fbSys < 0) { fbSys = id; fbBody = body; }
        }

        if (fbSys >= 0) { sysId = fbSys; bodyIndex = fbBody; return; }

        // last resort: first planet of the start system
        var first = g.systems[start];
        var fsd = SolarSystemGenerator.Generate(first.seed, first.id, first.position);
        sysId = start;
        bodyIndex = 0;
        for (int i = 1; i < fsd.bodies.Count; i++)
            if (!fsd.bodies[i].isBelt && fsd.bodies[i].parentIndex == 0) { bodyIndex = i; break; }
    }

    static int FindHomeBody(SolarSystemData sd)
    {
        int river = -1;
        for (int i = 1; i < sd.bodies.Count; i++)
        {
            var b = sd.bodies[i];
            if (b.parentIndex != 0) continue;           // planets only, not moons
            if (b.type == PlanetType.Terran) return i;
            if (b.type == PlanetType.RiverWorld && river < 0) river = i;
        }
        return river;
    }

    static void ApplyHomeOverride(BodyState st)
    {
        st.deposits[(int)ResourceType.Minerals] = st.depositsMax[(int)ResourceType.Minerals] = GameConfig.HomeMinerals;
        st.deposits[(int)ResourceType.RareMetals] = st.depositsMax[(int)ResourceType.RareMetals] = GameConfig.HomeRareMetals;
        st.deposits[(int)ResourceType.Gas] = st.depositsMax[(int)ResourceType.Gas] = 0f;
        st.deposits[(int)ResourceType.ExoticMatter] = st.depositsMax[(int)ResourceType.ExoticMatter] = 0f;
        st.anomalyName = null;
        st.fertility = 1f;
        st.owned = true;
        st.isHome = true;
        st.stock.capacity = GameConfig.HomeStockCapacity;
        foreach (var (type, amount) in GameConfig.StartingStock) st.stock.Add(type, amount);

        // the home planet starts with a finished Colony Hub and its first people
        st.buildings[(int)BuildingType.ColonyHub] = 1;
        st.population = Colony.StartPopulation;
    }

    // ------------------------------------------------------------------ claiming / ships

    /// <summary>Takes ownership of a body (a ship docked there pressed Claim). No cost.</summary>
    public void Claim(BodyState st)
    {
        if (st == null || st.owned) return;
        st.owned = true;
        owned.Add(st);
    }

    /// <summary>Adds a freshly built ship, docked at the given body, with 40% fuel.</summary>
    public ShipData AddShip(ShipClass cls, int systemId, int bodyIndex, Vector2 dockOffset)
    {
        var def = ShipDefs.Get(cls);
        int id = nextShipId++;
        var s = new ShipData
        {
            id = id,
            cls = (int)cls,
            name = def.name + " " + (id + 1),
            systemId = systemId,
            fuel = def.fuelTank * ShipDefs.LaunchFuel,
            mode = ShipData.Docked,
            dockedBody = bodyIndex,
            dockOffset = dockOffset
        };
        s.EnsureCargo();
        Ships.Add(s);
        return s;
    }

    // ------------------------------------------------------------------ save / load

    public void ExportSave(SaveData d)
    {
        d.homeSystemId = HomeSystemId;
        d.homeBodyIndex = HomeBodyIndex;
        d.researchData = ResearchData;
        d.ships = new List<ShipData>(Ships);
        d.nextShipId = nextShipId;
        d.bodies = new List<BodySave>(bodies.Count);

        foreach (var st in bodies.Values)
        {
            var b = new BodySave
            {
                systemId = st.systemId,
                bodyIndex = st.bodyIndex,
                deposits = (float[])st.deposits.Clone(),
                depositsMax = (float[])st.depositsMax.Clone(),
                fertility = st.fertility,
                anomalyName = st.anomalyName ?? "",
                owned = st.owned,
                isHome = st.isHome,
                stockCapacity = st.stock.capacity,
                stock = new float[ResourceInfo.Count],
                slots = st.slots,
                buildings = (int[])st.buildings.Clone(),
                solar = st.solar,
                battery = st.battery,
                population = st.population
            };
            for (int i = 0; i < ResourceInfo.Count; i++) b.stock[i] = st.stock.Get((ResourceType)i);
            st.ExportQueue(out b.queueTypes, out b.queueRemaining);
            st.ExportShipQueue(out b.shipQueueTypes, out b.shipQueueRemaining);
            d.bodies.Add(b);
        }
    }

    public void ImportSave(GalaxyData galaxy, SaveData d)
    {
        Galaxy = galaxy;
        bodies.Clear();
        owned.Clear();
        HomeSystemId = d.homeSystemId;
        HomeBodyIndex = d.homeBodyIndex;
        ResearchData = d.researchData;

        foreach (var b in d.bodies)
        {
            var st = new BodyState(b.systemId, b.bodyIndex);
            CopyInto(b.deposits, st.deposits);
            CopyInto(b.depositsMax, st.depositsMax);
            st.fertility = b.fertility;
            st.anomalyName = string.IsNullOrEmpty(b.anomalyName) ? null : b.anomalyName;
            st.owned = b.owned;
            st.isHome = b.isHome;
            st.stock.capacity = b.stockCapacity > 0f ? b.stockCapacity : GameConfig.DefaultStockCapacity;
            if (b.stock != null)
                for (int i = 0; i < Mathf.Min(b.stock.Length, ResourceInfo.Count); i++)
                    st.stock.Set((ResourceType)i, b.stock[i]);

            st.slots = b.slots;
            st.solar = b.solar;
            st.battery = b.battery;
            st.population = b.population;
            st.LoadBuildings(b.buildings);
            st.LoadQueue(b.queueTypes, b.queueRemaining);
            st.LoadShipQueue(b.shipQueueTypes, b.shipQueueRemaining);

            // older saves lack slots / solar (and the slot formula changed in v3): regenerate them
            if (d.version < 3 || st.slots <= 0 || st.solar <= 0f) FixLegacy(galaxy, b, st);

            bodies[BodyState.Key(b.systemId, b.bodyIndex)] = st;
            if (st.owned) owned.Add(st);
        }

        // v1 saves had no buildings: give the home planet its Hub
        if (d.version < 2 && bodies.TryGetValue(BodyState.Key(HomeSystemId, HomeBodyIndex), out var home)
            && home.Built(BuildingType.ColonyHub) == 0)
            home.buildings[(int)BuildingType.ColonyHub] = 1;

        Ships.Clear();
        if (d.ships != null)
            foreach (var s in d.ships) { s.EnsureCargo(); Ships.Add(s); }
        nextShipId = d.nextShipId;
    }

    static void FixLegacy(GalaxyData g, BodySave b, BodyState st)
    {
        st.slots = 4;
        st.solar = 1f;
        if (b.systemId < 0 || b.systemId >= g.systems.Count) return;

        var info = g.systems[b.systemId];
        var sd = SolarSystemGenerator.Generate(info.seed, info.id, info.position);
        if (b.bodyIndex < 0 || b.bodyIndex >= sd.bodies.Count) return;

        st.slots = Buildings.SlotsFor(sd.bodies[b.bodyIndex].diameter);
        st.solar = ResourceGenerator.SolarFor(sd, b.bodyIndex);
    }

    static void CopyInto(float[] src, float[] dst)
    {
        if (src != null) Array.Copy(src, dst, Mathf.Min(src.Length, dst.Length));
    }

    // ------------------------------------------------------------------ queries

    /// <summary>State for a body, generated and cached on first access.</summary>
    public BodyState GetBodyState(SolarSystemData sd, int bodyIndex)
    {
        long key = BodyState.Key(sd.id, bodyIndex);
        if (!bodies.TryGetValue(key, out var st))
        {
            st = ResourceGenerator.Generate(sd, bodyIndex);
            bodies[key] = st;
        }
        return st;
    }

    /// <summary>Sum of a physical resource across every owned location.</summary>
    public float GetEmpireTotal(ResourceType t)
    {
        if (t == ResourceType.ResearchData) return ResearchData;
        if (!ResourceInfo.IsPhysical(t)) return 0f;
        float sum = 0f;
        for (int i = 0; i < owned.Count; i++) sum += owned[i].stock.Get(t);
        return sum;
    }

    /// <summary>Top-bar text: energy made / used across all owned locations (red when short).</summary>
    public string EnergyText()
    {
        float made = 0f, used = 0f;
        for (int i = 0; i < owned.Count; i++) { made += owned[i].energyMade; used += owned[i].energyUsed; }
        string col = used > made + 0.01f ? "#ff7777" : "#ffffff";
        return "<color=" + col + ">" + made.ToString("0") + "/" + used.ToString("0") + "</color>";
    }
}
