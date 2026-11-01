using UnityEngine;

/// <summary>Single shared instance of the generated InputSystem_Actions wrapper.</summary>
public static class GameInput
{
    static InputSystem_Actions actions;

    public static InputSystem_Actions Actions
    {
        get
        {
            if (actions == null)
            {
                actions = new InputSystem_Actions();
                actions.RTS.Enable();
                actions.Ship.Enable();
                actions.DebugTools.Enable();
            }
            return actions;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        if (actions == null) return;
        actions.Disable();
        actions.Dispose();
        actions = null;
    }
}