using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private float projectileSpeed = 20f;
    [SerializeField] private float lifetime = 5f;

    private Rigidbody2D rb;
    private bool launched;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        // Fallback: if nobody called Launch(), fly along the object's own "up".
        if (!launched) Launch(transform.up);
    }

    /// Call right after Instantiate. direction is world-space.
    public void Launch(Vector2 direction, Collider2D shooter = null)
    {
        launched = true;
        direction.Normalize();

        // Rotate the sprite so its "up" points along the direction of travel
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (rb != null) rb.linearVelocity = direction * projectileSpeed;

        // Don't hit the ship that fired it
        if (shooter != null)
        {
            var myCol = GetComponent<Collider2D>();
            if (myCol != null) Physics2D.IgnoreCollision(myCol, shooter);
        }

        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        IDamageable damageable = collision.GetComponentInParent<IDamageable>();
        if (damageable == null) return;

        if (damageable is ShipView ship)
            ship.OnProjectileHit(collision.ClosestPoint(transform.position));

        damageable.TakeDamage(damageAmount);
        Destroy(gameObject);
    }
}
