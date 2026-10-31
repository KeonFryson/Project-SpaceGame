using UnityEngine;
using UnityEngine.InputSystem;

public class ForceFieldTester : MonoBehaviour
{
    [SerializeField] private ForceFieldController target;
    [SerializeField] private SpaceShip ship;
    [SerializeField] private Camera cam;

    void Awake()
    {
        if (target == null) target = GetComponentInChildren<ForceFieldController>(true);
        if (ship == null) ship = GetComponentInParent<SpaceShip>();
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (target == null) { Debug.LogWarning("Tester: no ForceFieldController found"); return; }

        var dbg = GameInput.Actions.DebugTools;

        if (dbg.TestHitCenter.WasPressedThisFrame())
        {
            Debug.Log("Test hit: center");
            target.Hit(target.transform.position, 1f);
        }

        if (dbg.TestHitPointer.WasPressedThisFrame())
        {
            Vector3 p = cam.ScreenToWorldPoint(GameInput.Actions.RTS.Point.ReadValue<Vector2>());
            p.z = 0f;
            Debug.Log("Test hit: " + p);
            target.Hit(p, 1f);
        }
    }
}