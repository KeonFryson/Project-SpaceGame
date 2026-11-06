using System.Collections.Generic;
using UnityEngine;

public enum GalaxyShape { Spiral, Elliptical }

/// <summary>
/// A galaxy is just a list of tiny StarSystemInfo records (id, seed, position, star class) plus a
/// spatial hash grid. Full solar systems are generated on demand from their seed, so a galaxy with
/// 100k+ systems costs a few MB and nothing is instantiated until a system is actually opened.
/// </summary>
public class GalaxyData
{
    public readonly int seed;
    public readonly float radius;
    public readonly float cellSize;
    public readonly List<StarSystemInfo> systems = new List<StarSystemInfo>();

    readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();

    public GalaxyData(int seed, float radius, float cellSize)
    {
        this.seed = seed;
        this.radius = radius;
        this.cellSize = cellSize;
    }

    Vector2Int Cell(Vector2 p) =>
        new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));

    /// <summary>Adds a system. Its id must equal its index in the list.</summary>
    public void Add(StarSystemInfo s)
    {
        systems.Add(s);
        var c = Cell(s.position);
        if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
        list.Add(systems.Count - 1);
    }

    /// <summary>True if any system is closer than minDist (minDist must be &lt;= cellSize).</summary>
    public bool HasNeighbour(Vector2 p, float minDist)
    {
        var c = Cell(p);
        float sq = minDist * minDist;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dy), out var list))
                    foreach (int i in list)
                        if ((systems[i].position - p).sqrMagnitude < sq) return true;
        return false;
    }

    /// <summary>Id of the closest system within maxDist of p, or -1.</summary>
    public int FindNearest(Vector2 p, float maxDist)
    {
        int reach = Mathf.Min(Mathf.CeilToInt(maxDist / cellSize), 20);
        var c = Cell(p);
        int best = -1;
        float bestSq = maxDist * maxDist;
        for (int dx = -reach; dx <= reach; dx++)
            for (int dy = -reach; dy <= reach; dy++)
                if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dy), out var list))
                    foreach (int i in list)
                    {
                        float sq = (systems[i].position - p).sqrMagnitude;
                        if (sq <= bestSq) { bestSq = sq; best = i; }
                    }
        return best;
    }
}

public static class GalaxyGenerator
{
    public static GalaxyData Generate(int seed, int systemCount, float radius,
                                      GalaxyShape shape = GalaxyShape.Spiral, int arms = 4,
                                      float twist = 3.5f, float minSystemDistance = 12f)
    {
        var rng = new System.Random(seed);
        var galaxy = new GalaxyData(seed, radius, Mathf.Max(minSystemDistance, 10f));

        for (int i = 0; i < systemCount; i++)
        {
            Vector2 pos = default;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                pos = shape == GalaxyShape.Spiral
                    ? SpiralPoint(rng, radius, arms, twist)
                    : EllipticalPoint(rng, radius);
                if (!galaxy.HasNeighbour(pos, minSystemDistance)) break;
            }

            int sysSeed = SeedUtil.Mix(seed, i + 1);
            galaxy.Add(new StarSystemInfo
            {
                id = i,
                seed = sysSeed,
                position = pos,
                starClass = SolarSystemGenerator.PeekStar(sysSeed)
            });
        }
        return galaxy;
    }

    static Vector2 SpiralPoint(System.Random rng, float radius, int arms, float twist)
    {
        float angle, dist;
        if (rng.NextDouble() < 0.18)   // central bulge
        {
            dist = radius * 0.22f * Mathf.Sqrt((float)rng.NextDouble());
            angle = (float)rng.NextDouble() * Mathf.PI * 2f;
        }
        else
        {
            float t = Mathf.Pow((float)rng.NextDouble(), 0.75f);
            dist = radius * t;
            int arm = rng.Next(arms);
            angle = arm * Mathf.PI * 2f / arms + t * twist + Gauss(rng) * (0.12f + 0.12f * t);
        }
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
    }

    static Vector2 EllipticalPoint(System.Random rng, float radius)
    {
        var p = new Vector2(Gauss(rng) * radius * 0.33f, Gauss(rng) * radius * 0.2f);
        float c = Mathf.Cos(0.6f), s = Mathf.Sin(0.6f);
        p = new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
        return p.magnitude > radius ? p.normalized * radius : p;
    }

    static float Gauss(System.Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }
}
