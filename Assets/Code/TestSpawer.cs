using UnityEngine;

public class TestSpawer : MonoBehaviour
{
    [SerializeField] private GameObject objectToSpawn;
    [SerializeField] private float spawnInterval = 10f;
    [SerializeField] private float timer = 0f;

    void Update()
    {
        if (timer <= 0f)
        {
            // Spawn facing the same way as the spawner and fire along its "up"
            GameObject obj = Instantiate(objectToSpawn, transform.position, transform.rotation);
            Projectile proj = obj.GetComponent<Projectile>();
            if (proj != null) proj.Launch(transform.up, GetComponent<Collider2D>());

            timer = spawnInterval;
        }
        else
        {
            timer -= Time.deltaTime;
        }
    }
}
