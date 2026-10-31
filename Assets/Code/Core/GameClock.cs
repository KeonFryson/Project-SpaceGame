using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Game time with pause / 1x / 2x / 4x. Production and build timers should use GameClock.Instance.Dt
/// instead of Time.deltaTime. Hotkeys: P = pause, 1 / 2 / 3 = 1x / 2x / 4x.
/// </summary>
public class GameClock : MonoBehaviour
{
    public static GameClock Instance { get; private set; }

    static readonly float[] Scales = { 0f, 1f, 2f, 4f };

    [Tooltip("If on, orbits follow the game speed. Off by default because GalaxyManager freezes orbits at start.")]
    [SerializeField] bool driveOrbits = false;

    public int SpeedIndex { get; private set; } = 1;
    public float Scale => Scales[SpeedIndex];
    public bool Paused => SpeedIndex == 0;
    public float GameTime { get; private set; }

    /// <summary>Game-time delta for this frame.</summary>
    public float Dt => Time.deltaTime * Scale;

    int lastRunning = 1;

    void Awake() => Instance = this;

    public void SetSpeed(int index)
    {
        SpeedIndex = Mathf.Clamp(index, 0, Scales.Length - 1);
        if (SpeedIndex > 0) lastRunning = SpeedIndex;
    }

    public void TogglePause() => SetSpeed(Paused ? lastRunning : 0);

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.pKey.wasPressedThisFrame) TogglePause();
            if (kb.digit1Key.wasPressedThisFrame) SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) SetSpeed(3);
        }

        GameTime += Dt;
        if (driveOrbits) CelestialBody.OrbitTimeScale = Scale;
    }
}
