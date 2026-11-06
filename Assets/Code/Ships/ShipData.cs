using System;
using System.Collections.Generic;
using UnityEngine;

public enum ShipClass { Corvette, Cruiser, Destroyer, Dreadnought }
public enum ShipState { Normal, Spooling, InTransit }
public enum OrderType { Move, Warp }

[Serializable]
public struct ShipOrder
{
    public OrderType type;
    public Vector2 position;   // Move: system-local target
    public int systemId;       // Warp: destination system

    public static ShipOrder Move(Vector2 p) => new ShipOrder { type = OrderType.Move, position = p };
    public static ShipOrder Warp(int id) => new ShipOrder { type = OrderType.Warp, systemId = id };
}

/// <summary>
/// One ship. Pure data - no GameObjects. FleetManager simulates it every frame whether or not its
/// system is on screen; a ShipView mirrors it only while its system is the one being viewed.
/// Positions are LOCAL to the system centre (the star is at 0,0). Rotation 0 = facing up.
/// </summary>
[Serializable]
public class ShipData
{
    public int id;
    public string name;
    public ShipClass shipClass;
    public int owner;                 // 0 = player

    public int systemId;              // -1 while in warp
    public Vector2 position;
    public float rotation;            // degrees
    public float speed;               // current speed, world units / sec

    public float hp;
    public float fuel;
    public bool lowFuelAlerted, emptyAlerted;

    public List<ShipOrder> orders = new List<ShipOrder>();

    // warp
    public ShipState state;
    public int warpFrom, warpDest;
    public float warpTimer, warpDuration;
    public bool followCamera;         // load the destination system when this ship arrives
}

/// <summary>Placeholder for depots/stations (step 3): ships within range refuel here.</summary>
[Serializable]
public class RefuelPoint
{
    public int systemId;
    public Vector2 position;
}
