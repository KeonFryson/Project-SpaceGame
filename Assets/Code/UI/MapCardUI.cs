using TMPro;
using UnityEngine;

/// <summary>
/// Info card for the system selected on the galaxy map, with a "Go to system" button.
/// Lives under RTSUI's canvas (bottom-left, same spot as the selection info panel).
/// Created by RTSBootstrap after RTSUI.
/// </summary>
public class MapCardUI : MonoBehaviour
{
    GalaxyManager galaxy;
    GameObject panel;
    TMP_Text title, body, goLabel;
    int shownId = -2;
    float timer;

    void Awake()
    {
        galaxy = FindFirstObjectByType<GalaxyManager>();
        var safe = transform.Find("RTSCanvas/SafeArea");
        if (safe == null)
        {
            Debug.LogError("MapCardUI: RTSUI canvas not found. Add MapCardUI AFTER RTSUI.", this);
            enabled = false;
            return;
        }
        Build(safe);
    }

    void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.1f;
        Refresh();
    }

    void Refresh()
    {
        bool show = galaxy != null && galaxy.MapOpen && galaxy.MapSelectedId >= 0 &&
                    galaxy.Galaxy != null && GameFlow.Mode == GameMode.Playing;
        if (panel.activeSelf != show) panel.SetActive(show);
        if (!show) { shownId = -2; return; }

        if (galaxy.MapSelectedId != shownId)
        {
            shownId = galaxy.MapSelectedId;
            Fill(shownId);
        }
    }

    void Fill(int id)
    {
        var g = galaxy.Galaxy;
        if (id < 0 || id >= g.systems.Count) return;

        var info = g.systems[id];
        var sd = SolarSystemGenerator.Generate(info.seed, info.id, info.position);

        int planets = 0, moons = 0, belts = 0;
        foreach (var b in sd.bodies)
        {
            if (b.parentIndex < 0) continue;      // the star
            if (b.isBelt) belts++;
            else if (b.parentIndex == 0) planets++;
            else moons++;
        }

        bool here = galaxy.CurrentSystem != null && galaxy.CurrentSystem.id == id;
        var gs = GameState.Instance;
        bool home = gs != null && gs.HomeSystemId == id;

        title.text = sd.name;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=#" + ClassHex(sd.starClass) + ">" + sd.starClass + " star</color>");
        if (home) sb.AppendLine(UITheme.Tint("Your home system", UITheme.Good));
        if (here) sb.AppendLine(UITheme.Tint("You are here", UITheme.Good));
        sb.AppendLine();
        sb.AppendLine("Planets: " + planets);
        sb.AppendLine("Moons: " + moons);
        sb.AppendLine("Asteroid belt: " + (belts > 0 ? "yes" : "no"));
        if (galaxy.CurrentSystem != null && !here)
            sb.AppendLine("Distance: " +
                          Vector2.Distance(info.position, galaxy.CurrentSystem.galaxyPosition).ToString("0") + " ly");
        sb.AppendLine(UITheme.Dim("Deposits and anomalies are unknown until you visit."));
        body.text = sb.ToString();

        goLabel.text = here ? "Return to system" : "Go to system";
    }

    static string ClassHex(StarClass c)
    {
        switch (c)
        {
            case StarClass.RedDwarf: return "ff5a41";
            case StarClass.Orange: return "ffa54d";
            case StarClass.Yellow: return "fff28c";
            case StarClass.White: return "e6f2ff";
            default: return "8cb4ff";
        }
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = UITheme.Panel(parent, "MapCard");
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(12f, 12f);
        rt.sizeDelta = new Vector2(380f, 330f);
        panel = rt.gameObject;

        title = UITheme.MakeText(rt, "Title", 24, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 14f);
        title.color = UITheme.Accent;
        var tr = title.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(-24f, 36f);
        tr.anchoredPosition = new Vector2(0f, -10f);

        body = UITheme.MakeText(rt, "Body", 15, TextAlignmentOptions.TopLeft, FontStyles.Normal, 10f);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Truncate;
        UITheme.Stretch(body.rectTransform, 12f, 70f, 12f, 52f);

        var btn = UITheme.MakeButton(rt, "Go to system", 340f, 48f, () =>
        {
            if (galaxy != null) galaxy.GoToSelectedSystem();
        }, 22f, out goLabel);
        var b = (RectTransform)btn.transform;
        b.anchorMin = b.anchorMax = b.pivot = new Vector2(0.5f, 0f);
        b.anchoredPosition = new Vector2(0f, 14f);
        b.sizeDelta = new Vector2(340f, 48f);

        panel.SetActive(false);
    }
}
