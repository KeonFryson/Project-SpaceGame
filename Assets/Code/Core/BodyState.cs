/// <summary>
/// Everything that must survive a solar system being regenerated / destroyed: remaining deposits,
/// the stockpile, ownership. Keyed by (systemId, bodyIndex). Pure data, no GameObjects.
/// Buildings, power grid and population get added here in phase 3.
/// </summary>
public class BodyState
{
    public readonly int systemId;
    public readonly int bodyIndex;

    /// <summary>Indexed by (int)ResourceType; only the Deposited types are used.</summary>
    public readonly float[] deposits = new float[ResourceInfo.Count];
    public readonly float[] depositsMax = new float[ResourceInfo.Count];

    public float fertility;            // 0..1
    public string anomalyName;         // null = none
    public bool owned;
    public bool isHome;
    public readonly ResourceStock stock = new ResourceStock();

    public BodyState(int systemId, int bodyIndex)
    {
        this.systemId = systemId;
        this.bodyIndex = bodyIndex;
    }

    public static long Key(int systemId, int bodyIndex) => ((long)systemId << 32) | (uint)bodyIndex;

    public float Deposit(ResourceType t) => deposits[(int)t];
    public float DepositMax(ResourceType t) => depositsMax[(int)t];

    public bool HasAnyDeposit
    {
        get
        {
            foreach (var t in ResourceInfo.Deposited)
                if (depositsMax[(int)t] > 0f) return true;
            return false;
        }
    }

    /// <summary>Takes up to amount out of the ground (used by mines in phase 3). Returns what was taken.</summary>
    public float Extract(ResourceType t, float amount)
    {
        int i = (int)t;
        float taken = amount < 0f ? 0f : (amount > deposits[i] ? deposits[i] : amount);
        deposits[i] -= taken;
        return taken;
    }
}
