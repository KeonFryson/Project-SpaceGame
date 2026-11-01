using UnityEngine;

/// <summary>
/// Generates a solar system purely as data from a seed (same seed = same system, always).
/// Planet types depend on how far the orbit is from the star's habitable zone, so you get
/// lava/barren worlds close in, terran worlds in the middle and gas/ice giants further out.
/// Sizes are in world units; tweak the tables below to change the scale of your game.
/// </summary>
public static class SolarSystemGenerator
{
    // indexed by (int)StarClass
    static readonly float[] StarWeights = { 30f, 25f, 20f, 17f, 8f };
    static readonly Vector2[] StarDiameter =
    {
        new Vector2(7, 10), new Vector2(9, 13), new Vector2(11, 15), new Vector2(13, 18), new Vector2(20, 28)
    };
    public static readonly float[] HabitableRadius = { 45f, 65f, 90f, 130f, 220f };

    const float TwoPi = 6.2831853f;

    public static StarClass RollStar(System.Random rng)
    {
        float total = 0f;
        foreach (var w in StarWeights) total += w;
        float roll = (float)rng.NextDouble() * total;
        for (int i = 0; i < StarWeights.Length; i++)
        {
            roll -= StarWeights[i];
            if (roll <= 0f) return (StarClass)i;
        }
        return StarClass.Yellow;
    }

    /// <summary>Star class of a system without generating the whole thing (used for the galaxy map).</summary>
    public static StarClass PeekStar(int systemSeed) => RollStar(new System.Random(systemSeed));

    public static SolarSystemData Generate(int seed, int id = 0, Vector2 galaxyPosition = default)
    {
        var rng = new System.Random(seed);
        var starClass = RollStar(rng);   // must stay the first roll - PeekStar relies on it

        var d = new SolarSystemData
        {
            id = id,
            seed = seed,
            galaxyPosition = galaxyPosition,
            starClass = starClass,
            name = NameGenerator.SystemName(seed)
        };

        float hab = HabitableRadius[(int)starClass];
        var range = StarDiameter[(int)starClass];
        float starDia = R(rng, range.x, range.y);

        d.bodies.Add(new BodyData
        {
            name = d.name,
            type = PlanetType.Star,
            seed = SeedUtil.Mix(seed, 0),
            diameter = starDia,
            parentIndex = -1
        });

        int planetCount = rng.Next(2, 9);   // 2..8
        bool hasBelt = planetCount >= 4 && rng.NextDouble() < 0.4;
        int beltSlot = hasBelt ? rng.Next(2, planetCount - 1) : -1;
        int slots = planetCount + (hasBelt ? 1 : 0);

        float r = Mathf.Max(hab * R(rng, 0.3f, 0.45f), starDia * 0.5f + 15f);
        int planetNo = 0;

        for (int slot = 0; slot < slots; slot++)
        {
            float extent = slot == beltSlot
                ? AddBelt(d, rng, seed, r, hab)
                : AddPlanet(d, rng, seed, planetNo++, r, hab);

            // next orbit: geometric growth, but always clear of this planet's moons / ring
            r = Mathf.Max(r * R(rng, 1.35f, 1.7f), r + extent + 12f);
        }

        d.radius = r + 10f;
        return d;
    }

    // ------------------------------------------------------------------ bodies

    static float AddPlanet(SolarSystemData d, System.Random rng, int seed, int number, float r, float hab)
    {
        float zone = r / hab;
        var type = PickType(rng, zone);

        float dia = type switch
        {
            PlanetType.GasGiant => R(rng, 9f, 15f),
            PlanetType.RingedGiant => R(rng, 6f, 9f),
            PlanetType.Barren => R(rng, 2f, 4.5f),
            _ => R(rng, 3f, 6f)
        };

        int idx = d.bodies.Count;
        string pname = NameGenerator.PlanetName(d.name, number);
        d.bodies.Add(new BodyData
        {
            name = pname,
            type = type,
            seed = SeedUtil.Mix(seed, 100 + idx),
            diameter = dia,
            parentIndex = 0,
            orbitRadius = r,
            orbitPeriod = 240f * Mathf.Pow(zone, 1.5f),
            startAngle = R(rng, 0f, TwoPi)
        });

        bool gas = type == PlanetType.GasGiant || type == PlanetType.RingedGiant;
        int moons;
        if (gas) moons = rng.Next(0, 5);
        else if (type == PlanetType.Lava || zone < 0.6f) moons = 0;
        else { double p = rng.NextDouble(); moons = p < 0.55 ? 0 : p < 0.85 ? 1 : 2; }

        float clearance = dia * 0.5f * (type == PlanetType.RingedGiant ? 3f : 1f) + 1.5f;
        float mr = clearance;
        for (int m = 0; m < moons; m++)
        {
            float mdia = gas ? R(rng, 1f, 2.4f) : R(rng, 0.9f, 1.7f);
            mr += mdia * 0.5f + R(rng, 0.5f, 1.5f);
            var mtype = zone > 1.5f && rng.NextDouble() < 0.2 ? PlanetType.Ice : PlanetType.Barren;

            d.bodies.Add(new BodyData
            {
                name = NameGenerator.MoonName(pname, m),
                type = mtype,
                seed = SeedUtil.Mix(seed, 100 + d.bodies.Count),
                diameter = mdia,
                parentIndex = idx,
                orbitRadius = mr,
                orbitPeriod = 25f * Mathf.Pow(mr / 4f, 1.5f),
                startAngle = R(rng, 0f, TwoPi)
            });
            mr += mdia * 0.5f + R(rng, 1f, 3f);
        }

        return mr;   // how far from the planet centre things extend
    }

    static float AddBelt(SolarSystemData d, System.Random rng, int seed, float r, float hab)
    {
        float width = R(rng, 5f, 9f);
        int count = rng.Next(22, 36);
        for (int i = 0; i < count; i++)
        {
            float orbit = r + R(rng, -width, width);
            d.bodies.Add(new BodyData
            {
                name = d.name + " Belt " + (i + 1),
                type = PlanetType.Asteroid,
                seed = SeedUtil.Mix(seed, 100 + d.bodies.Count),
                diameter = R(rng, 0.6f, 1.8f),
                parentIndex = 0,
                orbitRadius = orbit,
                orbitPeriod = 240f * Mathf.Pow(orbit / hab, 1.5f),
                startAngle = R(rng, 0f, TwoPi),
                isBelt = true
            });
        }
        return width + 2f;
    }

    // ------------------------------------------------------------------ type selection

    static PlanetType PickType(System.Random rng, float zone)
    {
        if (zone < 0.55f)
            return Pick(rng, (PlanetType.Lava, 50f), (PlanetType.Barren, 30f), (PlanetType.Desert, 20f));
        if (zone < 0.8f)
            return Pick(rng, (PlanetType.Desert, 40f), (PlanetType.Lava, 20f), (PlanetType.Barren, 20f),
                             (PlanetType.Terran, 10f), (PlanetType.RiverWorld, 10f));
        if (zone < 1.5f)
            return Pick(rng, (PlanetType.Terran, 35f), (PlanetType.RiverWorld, 25f),
                             (PlanetType.Desert, 25f), (PlanetType.Barren, 15f));
        if (zone < 2.8f)
            return Pick(rng, (PlanetType.Barren, 25f), (PlanetType.Ice, 20f), (PlanetType.GasGiant, 20f),
                             (PlanetType.Desert, 15f), (PlanetType.RingedGiant, 10f), (PlanetType.Terran, 10f));
        if (zone < 6f)
            return Pick(rng, (PlanetType.GasGiant, 35f), (PlanetType.RingedGiant, 25f),
                             (PlanetType.Ice, 25f), (PlanetType.Barren, 15f));
        return Pick(rng, (PlanetType.Ice, 45f), (PlanetType.Barren, 30f),
                         (PlanetType.GasGiant, 15f), (PlanetType.RingedGiant, 10f));
    }

    static PlanetType Pick(System.Random rng, params (PlanetType type, float weight)[] options)
    {
        float total = 0f;
        foreach (var o in options) total += o.weight;
        float roll = (float)rng.NextDouble() * total;
        foreach (var o in options)
        {
            roll -= o.weight;
            if (roll <= 0f) return o.type;
        }
        return options[options.Length - 1].type;
    }

    static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
}
