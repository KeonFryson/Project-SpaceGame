using System;
using System.Collections.Generic;
using UnityEngine;

public enum StarClass { RedDwarf, Orange, Yellow, White, BlueGiant }

/// <summary>One star, planet, moon or asteroid. Pure data - no GameObjects.</summary>
[Serializable]
public class BodyData
{
    public string name;
    public PlanetType type;
    public int seed;
    public float diameter;
    public int parentIndex = -1;   // index into SolarSystemData.bodies; -1 = system centre
    public float orbitRadius;      // distance from parent
    public float orbitPeriod;      // seconds for one orbit at OrbitTimeScale 1 (0 = static)
    public float startAngle;       // radians
    public bool isBelt;            // member of an asteroid belt
}

/// <summary>A fully generated solar system. bodies[0] is always the star.</summary>
[Serializable]
public class SolarSystemData
{
    public int id;
    public int seed;
    public string name;
    public Vector2 galaxyPosition;
    public StarClass starClass;
    public float radius;           // outer extent - handy for camera bounds
    public List<BodyData> bodies = new List<BodyData>();
}

/// <summary>Lightweight per-system record kept in the galaxy (the full system is generated on demand).</summary>
[Serializable]
public struct StarSystemInfo
{
    public int id;
    public int seed;
    public Vector2 position;
    public StarClass starClass;
}

public static class SeedUtil
{
    /// <summary>Deterministic integer hash of two ints (non-negative result).</summary>
    public static int Mix(int a, int b)
    {
        unchecked
        {
            uint x = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA6Bu;
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return (int)(x & 0x7FFFFFFF);
        }
    }
}

public static class NameGenerator
{
    static readonly string[] Start =
    {
        "Al", "Be", "Cor", "Dra", "El", "Fen", "Gal", "Hel", "Is", "Jor", "Kra", "Lum", "Mer",
        "Nov", "Or", "Pyr", "Quo", "Rig", "Sol", "Tar", "Ur", "Vex", "Xan", "Yor", "Zen"
    };
    static readonly string[] Mid = { "a", "e", "i", "o", "u", "ar", "en", "or", "ix", "an", "ul" };
    static readonly string[] End = { "is", "on", "us", "ara", "ion", "ax", "eth", "ia", "or", "um", "os" };
    static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };

    public static string SystemName(int seed)
    {
        var r = new System.Random(seed ^ 0x2F1B3C4D);
        string n = Start[r.Next(Start.Length)];
        if (r.NextDouble() < 0.5) n += Mid[r.Next(Mid.Length)];
        return n + End[r.Next(End.Length)];
    }

    public static string PlanetName(string system, int number) =>
        system + " " + Roman[Mathf.Min(number, Roman.Length - 1)];

    public static string MoonName(string planet, int index) =>
        planet + "-" + (char)('a' + index);
}
