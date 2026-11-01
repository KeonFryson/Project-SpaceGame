using UnityEngine;

public enum GameMode { Title, MainMenu, Playing, Paused }

/// <summary>Global game state. Gameplay input scripts check InputBlocked.</summary>
public static class GameFlow
{
    public static GameMode Mode = GameMode.Playing;   // Playing by default so scenes without menus still work
    public static bool InputBlocked => Mode != GameMode.Playing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Mode = GameMode.Playing;
}
