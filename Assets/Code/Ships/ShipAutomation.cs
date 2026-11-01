using UnityEngine;

/// <summary>
/// Runs one ship's automatic route between two OWNED bodies (A and B) in the loaded system.
/// At each stop the ship: delivers cargo the rules send here, refuels its own tank, loads cargo the
/// rules send onward, waits a moment, then departs for the other end if there is a reason to go
/// (something to deliver, or something to fetch) and it has enough fuel to get there and back.
///
/// Per-resource rules (ShipData.autoRules):
///   A > B     carry from A's stockpile to B's
///   B > A     carry from B's stockpile to A's
///   Balance   carry from whichever end has more, moving half the difference
/// Loading never takes a stockpile below ShipData.autoReserve and never overfills the destination.
/// A ship only runs routes while its system is loaded (ships in other systems are frozen).
/// </summary>
public class ShipAutomation
{
    const float Dwell = 1.5f;        // game seconds spent at a stop before acting
    const float Recheck = 2f;        // game seconds between checks while waiting
    const float FuelMargin = 1.1f;   // fuel safety factor on a leg
    const float MinTrip = 5f;        // don't make a trip for less than this much cargo

    readonly Ship ship;
    readonly ShipManager mgr;
    int processedAt = -1;
    float timer;

    public string Status { get; private set; } = "";

    public ShipAutomation(Ship ship, ShipManager mgr)
    {
        this.ship = ship;
        this.mgr = mgr;
    }

    public void Reset()
    {
        processedAt = -1;
        timer = 0f;
        Status = "";
    }

    // ------------------------------------------------------------------ tick

    public void Tick(float dt)
    {
        var d = ship.data;
        if (!d.autoOn)
        {
            if (Status.Length > 0) Reset();
            return;
        }
        d.EnsureAuto();

        if (!d.RouteValid) { Status = "Set both ends of the route"; return; }
        var sa = mgr.StateOf(d.autoA);
        var sb = mgr.StateOf(d.autoB);
        if (sa == null || sb == null || !sa.owned || !sb.owned)
        {
            Status = "Both ends must be planets you own";
            return;
        }

        if (d.mode == ShipData.Moving)
        {
            processedAt = -1;
            Status = d.targetBody >= 0 ? "Hauling to " + mgr.BodyName(d.targetBody) : "Moving";
            return;
        }

        if (d.mode == ShipData.Idle)
        {
            processedAt = -1;
            GoTo(Nearest(d));
            return;
        }

        // docked
        int here = d.dockedBody;
        if (here != d.autoA && here != d.autoB)
        {
            var hs = mgr.StateOf(here);
            if (hs != null && hs.owned && d.autoRefuel) ShipOps.Refuel(d, ship.def, hs);
            processedAt = -1;
            GoTo(Nearest(d));
            return;
        }

        bool hereIsA = here == d.autoA;
        int otherIdx = hereIsA ? d.autoB : d.autoA;
        var hereS = hereIsA ? sa : sb;
        var otherS = hereIsA ? sb : sa;

        if (processedAt != here)
        {
            processedAt = here;
            timer = Dwell;
            Status = "Loading at " + mgr.BodyName(here);
        }

        timer -= dt;
        if (timer > 0f) return;

        Transfer(d, hereS, hereIsA, otherS);

        if (!ShouldDepart(d, hereS, hereIsA, otherS))
        {
            Status = "Waiting for cargo at " + mgr.BodyName(here);
            timer = Recheck;
            return;
        }

        // fuel: enough to get there, and back too unless the far end can refuel us
        float leg = ship.FuelToReach(otherIdx) * FuelMargin;
        bool farEndHasFuel = d.autoRefuel && otherS.stock.Get(ResourceType.Fuel) >= leg;
        float need = farEndHasFuel ? leg : leg * 2f;
        if (d.fuel + 0.01f < need)
        {
            Status = ship.def.fuelTank < need ? "Waiting for Fuel (route too long for this tank)" : "Waiting for Fuel";
            timer = Recheck;
            return;
        }

        if (ship.AutoDock(otherIdx))
        {
            Status = "Hauling to " + mgr.BodyName(otherIdx);
            processedAt = -1;
        }
        else
        {
            Status = "Cannot reach " + mgr.BodyName(otherIdx);
            timer = Recheck;
        }
    }

    // ------------------------------------------------------------------ helpers

    void GoTo(int bodyIndex)
    {
        Status = ship.AutoDock(bodyIndex)
            ? "Heading to " + mgr.BodyName(bodyIndex)
            : "Needs Fuel to reach " + mgr.BodyName(bodyIndex);
    }

    int Nearest(ShipData d) =>
        ship.DistanceTo(d.autoA) <= ship.DistanceTo(d.autoB) ? d.autoA : d.autoB;

    // rule helpers: does this stop load / unload the resource?
    static bool PicksUpAt(int rule, bool atA) =>
        rule == ShipData.RuleBalance || (rule == ShipData.RuleAToB && atA) || (rule == ShipData.RuleBToA && !atA);

    static bool DeliversAt(int rule, bool atA) =>
        rule == ShipData.RuleBalance || (rule == ShipData.RuleAToB && !atA) || (rule == ShipData.RuleBToA && atA);

    static float Free(BodyState s, ResourceType t) => s.stock.capacity - s.stock.Get(t);

    /// <summary>Deliver, refuel, then load, in that order.</summary>
    void Transfer(ShipData d, BodyState here, bool hereIsA, BodyState other)
    {
        foreach (var t in ResourceInfo.Physical)
        {
            int rule = d.autoRules[(int)t];
            if (rule == ShipData.RuleOff) continue;
            if (DeliversAt(rule, hereIsA) && d.cargo[(int)t] > 0.01f)
                ShipOps.Unload(d, here, t, float.MaxValue);
        }

        if (d.autoRefuel) ShipOps.Refuel(d, ship.def, here);

        foreach (var t in ResourceInfo.Physical)
        {
            int rule = d.autoRules[(int)t];
            if (rule == ShipData.RuleOff || !PicksUpAt(rule, hereIsA)) continue;

            float have = here.stock.Get(t);
            float avail = have - d.autoReserve;
            if (rule == ShipData.RuleBalance)
                avail = Mathf.Min(avail, (have - other.stock.Get(t)) * 0.5f);

            // don't promise more than the far end can hold (counting what we already carry)
            float room = Free(other, t) - d.cargo[(int)t];
            float amount = Mathf.Min(avail, room);
            if (amount > 1f) ShipOps.Load(d, ship.def, here, t, amount);
        }
    }

    /// <summary>True if there is cargo the far end will take, or cargo worth fetching from it.</summary>
    bool ShouldDepart(ShipData d, BodyState here, bool hereIsA, BodyState other)
    {
        float shipFree = ship.def.cargo - d.CargoTotal();

        foreach (var t in ResourceInfo.Physical)
        {
            int rule = d.autoRules[(int)t];
            if (rule == ShipData.RuleOff) continue;
            int i = (int)t;

            // carrying something the other end accepts
            if (DeliversAt(rule, !hereIsA) && d.cargo[i] > 0.5f && Free(other, t) > 1f) return true;

            // something worth fetching from the other end, with room for it back here
            if (PicksUpAt(rule, !hereIsA) && shipFree > MinTrip)
            {
                float have = other.stock.Get(t);
                float avail = have - d.autoReserve;
                if (rule == ShipData.RuleBalance)
                    avail = Mathf.Min(avail, (have - here.stock.Get(t)) * 0.5f);
                if (avail > MinTrip && Free(here, t) - d.cargo[i] > MinTrip) return true;
            }
        }
        return false;
    }
}
