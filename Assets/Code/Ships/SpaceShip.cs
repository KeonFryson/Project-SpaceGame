using UnityEngine;

public class SpaceShip : MonoBehaviour, IDamageable
{
    private Rigidbody2D rb;

    [SerializeField] private int health;
    private int maxHealth = 100;
    [SerializeField] private int shield;
    private int maxShield = 100;
    [SerializeField] private int fuel;
    private int maxFuel = 100;
    [SerializeField] private int ammo;
    private int maxAmmo = 100;

    private bool isAlive = true;
    public bool isShieldActive = false;
    private bool isShieldBreaking = false;

    [SerializeField] private ForceFieldController forceFieldController; // drag the shield's controller here
    [SerializeField] private GameObject shieldObject;

    public float speed = 5f;
    public float rotationSpeed = 200f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        isAlive = true;

        if (forceFieldController == null)
            forceFieldController = GetComponentInChildren<ForceFieldController>(true);
        if (shieldObject == null && forceFieldController != null)
            shieldObject = forceFieldController.gameObject;

        health = maxHealth;
        shield = maxShield;
        fuel = maxFuel;
        ammo = maxAmmo;

        isShieldActive = true;
    }

    void Update()
    {
        ShipShield();
    }

    private void ShipShield()
    {
        if (shieldObject == null) return;
        if (isShieldBreaking) return;   // let the break animation finish

        if (shield <= 0) isShieldActive = false;

        if (isShieldActive && !shieldObject.activeSelf)
        {
            shieldObject.SetActive(true);
            if (forceFieldController != null) forceFieldController.Restore();
        }
        else if (!isShieldActive && shieldObject.activeSelf)
        {
            shieldObject.SetActive(false);
        }
    }

    /// Called by projectiles so the shield can ripple at the impact point.
    public void OnProjectileHit(Vector2 worldPoint)
    {
        if (isShieldActive && forceFieldController != null)
            forceFieldController.Hit(worldPoint, 1f);
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
            // Handle spaceship destruction logic here
            Destroy(gameObject);
        }
    }

    private void BreakShield()
    {
        isShieldActive = false;

        if (forceFieldController == null || shieldObject == null)
        {
            if (shieldObject != null) shieldObject.SetActive(false);
            return;
        }

        isShieldBreaking = true;
        forceFieldController.Break(() =>
        {
            isShieldBreaking = false;
            shieldObject.SetActive(false);
        });
    }

    /// Bring the shield back (pickup, recharge, etc.)
    public void RestoreShield()
    {
        shield = maxShield;
        isShieldActive = true;
    }
}
