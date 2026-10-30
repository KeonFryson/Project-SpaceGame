using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
#endif

/// <summary>
/// Holds the original UniPixelPlanet .mat files. They are used as TEMPLATES: every planet layer
/// gets its own Material instance copied from one of these, so all the hand-tuned shader values
/// (_Size, _OCTAVES, light borders, cutoffs, ...) are preserved and the originals are never modified.
///
/// Create via: Assets > Create > Space > Planet Material Library.
/// Fields are auto-filled by name when the asset is created (or via the context menu on the asset).
/// </summary>
[CreateAssetMenu(menuName = "Space/Planet Material Library", fileName = "PlanetMaterialLibrary")]
public class PlanetMaterialLibrary : ScriptableObject
{
    [Header("Terran (land masses)")]
    public Material planetUnder, planetLandMass, clouds;

    [Header("River world")]
    public Material landRivers, cloudsRiver;

    [Header("Desert")]
    public Material dryTerran;

    [Header("Ice")]
    public Material icePlanetUnder, lakes, iceClouds;

    [Header("Lava")]
    public Material lavaPlanetUnder, lavaCraters, lavaRivers;

    [Header("Gas giant")]
    public Material gasPlanet, gasPlanet2;

    [Header("Ringed gas giant")]
    public Material gasLayers, ring;

    [Header("Barren / moon")]
    public Material noAtmosphere, craters;

    [Header("Asteroid")]
    public Material asteroid;

    [Header("Star")]
    public Material star, starBlobs, starFlares;

#if UNITY_EDITOR
    void Reset() => AutoFill();

    [ContextMenu("Auto-Fill From Project")]
    void AutoFill()
    {
        foreach (var f in typeof(PlanetMaterialLibrary).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType != typeof(Material)) continue;
            foreach (var guid in AssetDatabase.FindAssets(f.Name + " t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), f.Name,
                                  System.StringComparison.OrdinalIgnoreCase))
                {
                    f.SetValue(this, AssetDatabase.LoadAssetAtPath<Material>(path));
                    break;
                }
            }
        }
        EditorUtility.SetDirty(this);
    }
#endif
}
