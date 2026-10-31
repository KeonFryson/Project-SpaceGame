using UnityEngine;

/// <summary>
/// Creates the RTS managers automatically when the scene loads, so nothing has to be added by hand.
/// Only runs in scenes that contain a GalaxyManager. Components are added in dependency order and
/// each subscribes to events in Awake, which all runs before GalaxyManager.Start.
/// </summary>
public static class RTSBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Object.FindFirstObjectByType<GalaxyManager>() == null) return;
        if (Object.FindFirstObjectByType<GameState>() != null) return;

        var go = new GameObject("RTS");
        go.AddComponent<GameState>();
        go.AddComponent<GameClock>();
        go.AddComponent<SelectionManager>();
        go.AddComponent<RTSUI>();
    }
}
