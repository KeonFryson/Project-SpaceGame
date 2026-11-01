/// <summary>
/// Every balance number for phase 1 lives here so tuning is editing one file.
/// Richness levels: 0 = none, 1 = low, 2 = medium, 3 = high.
/// </summary>
public static class GameConfig
{
    // ---- storage
    public const float DefaultStockCapacity = 2000f;
    public const float HomeStockCapacity = 10000f;

    // ---- anomalies / exotic matter
    public const float AnomalyChance = 0.12f;          // per solar system
    public const float ExoticChance = 0.06f;           // per desert / ice / lava body
    public const float ExoticRareAmount = 400f;
    public const float ExoticAnomalyAmount = 3000f;

    // ---- home planet
    public const float HomeMinerals = 20000f;
    public const float HomeRareMetals = 3000f;

    public static readonly (ResourceType type, float amount)[] StartingStock =
    {
        (ResourceType.Minerals, 1000f),
        (ResourceType.RareMetals, 150f),
        (ResourceType.Gas, 100f),
        (ResourceType.Fuel, 200f),
        (ResourceType.Food, 400f),
    };

    // ---- deposits
    public static float RichnessAmount(int level)
    {
        switch (level)
        {
            case 1: return 800f;
            case 2: return 2500f;
            case 3: return 6000f;
            default: return 0f;
        }
    }

    /// <summary>
    /// Deposit levels per body type, ordered like ResourceInfo.Deposited:
    /// [Minerals, Gas, RareMetals, Exotic]. For Exotic, any non-zero value means
    /// "this type may roll a small exotic deposit" (see ExoticChance).
    /// </summary>
    public static int[] RichnessTable(PlanetType t)
    {
        switch (t)
        {
            case PlanetType.Terran:
            case PlanetType.RiverWorld: return new[] { 2, 0, 1, 0 };
            case PlanetType.Desert: return new[] { 3, 0, 2, 1 };
            case PlanetType.Ice: return new[] { 2, 1, 1, 1 };
            case PlanetType.Lava: return new[] { 3, 0, 3, 1 };
            case PlanetType.Barren: return new[] { 2, 0, 1, 0 };
            case PlanetType.Asteroid: return new[] { 3, 0, 2, 0 };
            case PlanetType.GasGiant:
            case PlanetType.RingedGiant: return new[] { 0, 3, 0, 0 };
            default: return new[] { 0, 0, 0, 0 };   // Star
        }
    }

    /// <summary>0..1. How good the body is for farming (Ice is hydroponics-only, so it's lower).</summary>
    public static float Fertility(PlanetType t)
    {
        switch (t)
        {
            case PlanetType.Terran:
            case PlanetType.RiverWorld: return 1f;
            case PlanetType.Ice: return 0.4f;
            case PlanetType.Desert: return 0.2f;
            default: return 0f;
        }
    }
}
