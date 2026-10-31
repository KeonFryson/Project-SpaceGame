using UnityEngine;

/// <summary>The eight empire resources. Energy and ResearchData are NOT physical (they can't sit in a stockpile or ship).</summary>
public enum ResourceType
{
    Minerals,
    Energy,
    Gas,
    RareMetals,
    ExoticMatter,
    Fuel,
    Food,
    ResearchData
}

public static class ResourceInfo
{
    public const int Count = 8;

    /// <summary>Resources that live in stockpiles and are hauled by ships.</summary>
    public static readonly ResourceType[] Physical =
    {
        ResourceType.Minerals, ResourceType.Gas, ResourceType.RareMetals,
        ResourceType.ExoticMatter, ResourceType.Fuel, ResourceType.Food
    };

    /// <summary>Resources that exist as finite deposits in the ground (order matches GameConfig.RichnessTable).</summary>
    public static readonly ResourceType[] Deposited =
    {
        ResourceType.Minerals, ResourceType.Gas, ResourceType.RareMetals, ResourceType.ExoticMatter
    };

    public static bool IsPhysical(ResourceType t) => t != ResourceType.Energy && t != ResourceType.ResearchData;

    public static string Name(ResourceType t)
    {
        switch (t)
        {
            case ResourceType.RareMetals: return "Rare Metals";
            case ResourceType.ExoticMatter: return "Exotic Matter";
            case ResourceType.ResearchData: return "Research Data";
            default: return t.ToString();
        }
    }

    public static Color Tint(ResourceType t)
    {
        switch (t)
        {
            case ResourceType.Minerals: return new Color(0.80f, 0.68f, 0.50f);
            case ResourceType.Energy: return new Color(1.00f, 0.90f, 0.35f);
            case ResourceType.Gas: return new Color(0.55f, 0.90f, 0.75f);
            case ResourceType.RareMetals: return new Color(0.75f, 0.60f, 1.00f);
            case ResourceType.ExoticMatter: return new Color(1.00f, 0.45f, 0.85f);
            case ResourceType.Fuel: return new Color(1.00f, 0.60f, 0.25f);
            case ResourceType.Food: return new Color(0.55f, 0.85f, 0.40f);
            default: return new Color(0.45f, 0.80f, 1.00f);
        }
    }

    public static string Hex(ResourceType t) => ColorUtility.ToHtmlStringRGB(Tint(t));

    /// <summary>"Rare Metals" in its own colour, as a rich-text string.</summary>
    public static string Coloured(ResourceType t) => "<color=#" + Hex(t) + ">" + Name(t) + "</color>";
}

/// <summary>
/// A location's storage for the six physical resources. Each resource has the same capacity.
/// Planets and (later) stations own one; ships will get a similar one for cargo.
/// </summary>
public class ResourceStock
{
    readonly float[] amounts = new float[ResourceInfo.Count];
    public float capacity = GameConfig.DefaultStockCapacity;

    public float Get(ResourceType t) => amounts[(int)t];

    public void Set(ResourceType t, float amount)
    {
        if (!ResourceInfo.IsPhysical(t)) return;
        amounts[(int)t] = Mathf.Clamp(amount, 0f, capacity);
    }

    /// <summary>Adds up to amount, limited by free space. Returns how much was actually stored.</summary>
    public float Add(ResourceType t, float amount)
    {
        if (!ResourceInfo.IsPhysical(t) || amount <= 0f) return 0f;
        float stored = Mathf.Min(amount, Mathf.Max(0f, capacity - amounts[(int)t]));
        amounts[(int)t] += stored;
        return stored;
    }

    public bool Has(ResourceType t, float amount) => amounts[(int)t] >= amount;

    /// <summary>Removes exactly amount or nothing.</summary>
    public bool TryRemove(ResourceType t, float amount)
    {
        if (amount < 0f || amounts[(int)t] < amount) return false;
        amounts[(int)t] -= amount;
        return true;
    }

    /// <summary>Removes up to amount. Returns how much was removed.</summary>
    public float Remove(ResourceType t, float amount)
    {
        float taken = Mathf.Clamp(amount, 0f, amounts[(int)t]);
        amounts[(int)t] -= taken;
        return taken;
    }
}
