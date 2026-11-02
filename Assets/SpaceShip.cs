using UnityEngine;
using UnityEngine.InputSystem;

public class SpaceShip : MonoBehaviour, IDamageable
{
    const float ProjectileRippleStrength = 1f;

    private Rigidbody2D rb;

    [Header("Stats")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int maxShield = 100;
    [SerializeField] private int maxFuel = 100;
    [SerializeField] private int maxAmmo = 100;

    [Header("Current (runtime)")]
    [SerializeField] private int health;
    [SerializeField] private int shield;
    [SerializeField] private int fuel;
    [SerializeField] private int ammo;

    [Header("Shield")]
    [SerializeField] private ForceFieldController forceFieldController;
    [SerializeField] private GameObject shieldObject;
    [Tooltip("How strongly projectile hits ripple the shield.")]
    [SerializeField] private float projectileRippleStrength = ProjectileRippleStrength;

    [Header("Movement")]
    public float speed = 5f;
    public float rotationSpeed = 200f;

    private bool isAlive = true;
    public bool isShieldActive = false;
    private bool isAnimationPlaying = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (forceFieldController == null)
            forceFieldController = GetComponentInChildren<ForceFieldController>(true);
        if (shieldObject == null && forceFieldController != null)
            shieldObject = forceFieldController.gameObject;

        health = maxHealth;
        shield = maxShield;
        fuel = maxFuel;
        ammo = maxAmmo;
    }

    void Start()
    {
        isAlive = true;
        if (shieldObject != null) shieldObject.SetActive(isShieldActive);
    }

    private void Update()
    {
        if (!isAlive) return;
        if (GameInput.Actions.Ship.ToggleShield.WasPressedThisFrame()) ToggleShield();
    }

    /// Called by projectiles so the shield can ripple at the impact point.
    public void OnProjectileHit(Vector2 worldPoint)
    {
        if (isShieldActive && forceFieldController != null)
            forceFieldController.Hit(worldPoint, projectileRippleStrength);
    }

    public void TakeDamage(int damage)
    {
        if (isShieldActive)
        {
            shield -= damage;
            if (shield <= 0)
            {
                health += shield;   // leftover damage carries over to health
                shield = 0;
                BreakShield();
            }
        }
        else
        {
            health -= damage;
        }

        if (health <= 0)
        {
            isAlive = false;
            Destroy(gameObject);
        }
    }

    private void BreakShield()
    {
        isShieldActive = false;
        isAnimationPlaying = true;
        forceFieldController.PlayAnimation(() =>
        {
            isAnimationPlaying = false;
            shieldObject.SetActive(false);
        });
    }

    private void ActivateShield()
    {
        isShieldActive = true;
        isAnimationPlaying = true;
        shieldObject.SetActive(true);
        forceFieldController.Restore();
        forceFieldController.PlayAnimation(() =>
        {
            forceFieldController.Restore();
            isAnimationPlaying = false;
        }, reverse: true);
    }

    public void RestoreShield()
    {
        shield = maxShield;
        if (!isShieldActive && !isAnimationPlaying) ActivateShield();
    }

    [ContextMenu("Toggle Shield")]
    public void ToggleShield()
    {
        if (isAnimationPlaying) return;

        if (isShieldActive) BreakShield();
        else if (shield > 0) ActivateShield();
        else Debug.Log("Can't activate shield: it has no charge. Call RestoreShield() first.");
    }
}