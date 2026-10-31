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

         

        // Space = ripple at the shield center
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("Test hit: center");
            target.Hit(target.transform.position, 1f);
        }

        // Left click = ripple at the mouse position
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector3 p = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            p.z = 0f;
            Debug.Log("Test hit: " + p);
            target.Hit(p, 1f);
        }
    }
}