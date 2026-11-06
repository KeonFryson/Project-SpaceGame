using UnityEngine;

/// <summary>
/// Sits on every spawned star / planet / moon / asteroid. Holds its BodyData (so your RTS code can
/// read what was clicked) and moves it around its parent on a circular orbit.
/// </summary>
public class CelestialBody : MonoBehaviour
{
    /// <summary>Global orbit speed. 0 = everything frozen (handy for pausing / building), 1 = normal.</summary>
    public static float OrbitTimeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => OrbitTimeScale = 1f;

    public BodyData data;
    public int index;          // index into SolarSystemData.bodies

    float angle;
    float angularSpeed;
    float orbitRadius;

    public void Init(BodyData d, int bodyIndex)
    {
        data = d;
        index = bodyIndex;
        orbitRadius = d.orbitRadius;
        angle = d.startAngle;
        angularSpeed = d.orbitPeriod > 0f ? Mathf.PI * 2f / d.orbitPeriod : 0f;
        Apply();
    }

    void Update()
    {
        if (orbitRadius <= 0f || angularSpeed == 0f || OrbitTimeScale == 0f) return;
        angle += angularSpeed * OrbitTimeScale * Time.deltaTime;
        Apply();
    }

    void Apply()
    {
        transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * orbitRadius;
    }
}
