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
        // start in a state consistent with the flag
        if (shieldObject != null) shieldObject.SetActive(isShieldActive);
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
            Destroy(gameObject);
        }
    }

    private void BreakShield()
    {
        isShieldActive = false;       // damage goes to health from now on
        isAnimationPlaying = true;
        forceFieldController.PlayAnimation(() =>
        {
            isAnimationPlaying = false;
            shieldObject.SetActive(false);
        });
    }

    /// Shows the shield object, then plays the "appear" animation in reverse.
    private void ActivateShield()
    {
        isShieldActive = true;
        isAnimationPlaying = true;
        shieldObject.SetActive(true);        // must be active before starting the coroutine
        forceFieldController.Restore();      // clear stale ripples / progress
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
        if (isAnimationPlaying) return;   // let the current animation finish

        if (isShieldActive)
        {
            BreakShield();
        }
        else if (shield > 0)
        {
            ActivateShield();
        }
        else
        {
            Debug.Log("Can't activate shield: it has no charge. Call RestoreShield() first.");
        }
    }
}
