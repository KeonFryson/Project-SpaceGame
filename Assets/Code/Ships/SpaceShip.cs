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
    private bool isAnimationPlaying = false;

    [SerializeField] private ForceFieldController forceFieldController; // drag the shield's controller here
    [SerializeField] private GameObject shieldObject;

    public float speed = 5f;
    public float rotationSpeed = 200f;

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
    }

    void Update()
    {
        
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
        Debug.Log("Breaking shield. Current shield: " + shield);
        isAnimationPlaying = true;
        forceFieldController.PlayAnimation(() =>
        {
            Debug.Log("Shield break animation completed.");
            isAnimationPlaying = false;
            Debug.Log("isAnimationPlaying" + isAnimationPlaying);
            shieldObject.SetActive(false);
        });
    }

    /// Bring the shield back (pickup, recharge, etc.)
    public void RestoreShield()
    {
        shield = maxShield;
        isAnimationPlaying = true;
        forceFieldController.PlayAnimation(() =>
        {
            forceFieldController.Restore();
            isAnimationPlaying = false;
            shieldObject.SetActive(true);
        });
    }

    [ContextMenu("Toggle Shield")]
    public void ToggleShield()
    {
        Debug.Log("ToggleShield called. isShieldActive: " + isShieldActive + ", shield: " + shield + ", isAnimationPlaying: " + isAnimationPlaying);
        if (isAnimationPlaying) { Debug.Log("Animation is playing, cannot toggle shield."); return; }   // let the break animation finish

        isShieldActive = !isShieldActive;
        Debug.Log("Shield toggled: " + isShieldActive);

        if (isShieldActive && shield > 0)
        {
            shieldObject.SetActive(isShieldActive);
            RestoreShield();
            Debug.Log("Shield restored and activated.");
        }
        else
        {

            BreakShield();
            shieldObject.SetActive(isShieldActive);
            Debug.Log("Shield deactivated.");
        }
    }
}
