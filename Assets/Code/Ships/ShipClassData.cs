using UnityEngine;

/// <summary>
/// Balance numbers for one ship class. Create assets via Assets > Create > Space > Ship Class, or
/// skip that entirely: FleetManager falls back to CreateDefault() (the GDD prototype table).
/// Speeds are GDD x 0.1 so they make sense in world units (system radius is a few hundred units).
/// </summary>
[CreateAssetMenu(menuName = "Space/Ship Class", fileName = "ShipClass")]
public class ShipClassData : ScriptableObject
{
    public ShipClass shipClass;

    [Header("Combat")]
    public float maxHealth = 100f;

    [Header("Movement (world units)")]
    public float maxSpeed = 22f;
    public float acceleration = 30f;
    [Tooltip("Degrees per second.")]
    public float turnRate = 180f;

    [Header("Fuel")]
    public float fuelCapacity = 60f;
    [Tooltip("Burned per second while moving.")]
    public float fuelBurnPerSec = 0.5f;

    [Header("Cargo and cost")]
    public float cargo = 20f;
    public int metalCost = 50;
    public int popCost = 1;

    [Header("Warp (galaxy-map units)")]
    public float jumpRange = 60f;
    public float warpFuelPerUnit = 1f;
    public float warpSpoolTime = 3f;
    [Tooltip("Galaxy-map units per second while in warp.")]
    public float warpSpeed = 20f;

    [Header("Visual")]
    [Tooltip("Length in world units.")]
    public float visualSize = 1.2f;
    [Tooltip("Optional. Leave empty to use the built-in triangle. Remove SpaceShip from it - ship state lives in ShipData now.")]
    public GameObject prefab;

    public static ShipClassData CreateDefault(ShipClass c)
    {
        var d = CreateInstance<ShipClassData>();
        d.name = c + " (default)";
        d.shipClass = c;
        switch (c)
        {
            case ShipClass.Corvette:
                d.maxHealth = 100; d.maxSpeed = 22f; d.acceleration = 30f; d.turnRate = 180f;
                d.fuelCapacity = 60; d.fuelBurnPerSec = 0.5f; d.cargo = 20; d.metalCost = 50; d.popCost = 1;
                d.jumpRange = 60; d.warpFuelPerUnit = 1f; d.warpSpoolTime = 3f; d.warpSpeed = 20f; d.visualSize = 1.2f;
                break;
            case ShipClass.Cruiser:
                d.maxHealth = 400; d.maxSpeed = 14f; d.acceleration = 18f; d.turnRate = 110f;
                d.fuelCapacity = 200; d.fuelBurnPerSec = 1.5f; d.cargo = 80; d.metalCost = 200; d.popCost = 3;
                d.jumpRange = 80; d.warpFuelPerUnit = 1.5f; d.warpSpoolTime = 4f; d.warpSpeed = 16f; d.visualSize = 2.2f;
                break;
            case ShipClass.Destroyer:
                d.maxHealth = 1200; d.maxSpeed = 9f; d.acceleration = 10f; d.turnRate = 60f;
                d.fuelCapacity = 600; d.fuelBurnPerSec = 4f; d.cargo = 250; d.metalCost = 700; d.popCost = 8;
                d.jumpRange = 100; d.warpFuelPerUnit = 3f; d.warpSpoolTime = 6f; d.warpSpeed = 12f; d.visualSize = 4f;
                break;
            default: // Dreadnought
                d.maxHealth = 5000; d.maxSpeed = 4.5f; d.acceleration = 4f; d.turnRate = 25f;
                d.fuelCapacity = 2000; d.fuelBurnPerSec = 12f; d.cargo = 1000; d.metalCost = 3000; d.popCost = 25;
                d.jumpRange = 150; d.warpFuelPerUnit = 6f; d.warpSpoolTime = 10f; d.warpSpeed = 8f; d.visualSize = 8f;
                break;
        }
        return d;
    }
}
