using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Title screen, main menu, pause menu and save/load screen, all built from code and styled by UITheme.
/// Created by RTSBootstrap. Change GameTitle to your game's name.
/// </summary>
public class MenuManager : MonoBehaviour
{
    public const string GameTitle = "STAR FRONTIER";
    const float AutosaveInterval = 180f;   // real seconds

    sealed class SlotRow
    {
        public int slot;
        public GameObject root;
        public TMP_Text info;
        public Button action, delete;
    }

    public static MenuManager Instance { get; private set; }

    GalaxyManager galaxy;
    Transform hudCanvas;
    GameObject titlePanel, mainPanel, pausePanel, slotsPanel, hudButtons;
    TMP_Text pressKey, slotsHeading, slotsStatus;
    Button continueBtn;
    readonly List<SlotRow> rows = new List<SlotRow>();

    bool slotsSaving;
    GameObject slotsReturn;
    string pendingConfirm = "";
    bool hadSelection;
    float autosaveTimer;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        Instance = this;
        galaxy = FindFirstObjectByType<GalaxyManager>();
        EnsureEventSystem();
        BuildCanvas();
        SetMode(GameMode.Title);
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        GameFlow.Mode = GameMode.Playing;
    }

    Transform HudCanvas
    {
        get
        {
            if (hudCanvas == null) hudCanvas = transform.Find("RTSCanvas");
            return hudCanvas;
        }
    }

    void SetMode(GameMode m)
    {
        GameFlow.Mode = m;
        Time.timeScale = m == GameMode.Paused ? 0f : 1f;
        if (galaxy != null)
        {
            if (m == GameMode.Title || m == GameMode.MainMenu) galaxy.EnterOverview();
            else if (m == GameMode.Playing) galaxy.ExitOverview();
        }
        titlePanel.SetActive(m == GameMode.Title);
        mainPanel.SetActive(m == GameMode.MainMenu);
        pausePanel.SetActive(m == GameMode.Paused);
        slotsPanel.SetActive(false);
        hudButtons.SetActive(m == GameMode.Playing);

        var hud = HudCanvas;
        if (hud != null) hud.gameObject.SetActive(m == GameMode.Playing || m == GameMode.Paused);

        // planets orbit behind the menus; in-game orbits stay frozen (GameClock can drive them if enabled)
        CelestialBody.OrbitTimeScale = (m == GameMode.Title || m == GameMode.MainMenu) ? 1f : 0f;

        if (m == GameMode.MainMenu) RefreshContinue();
        if (m == GameMode.Playing) autosaveTimer = 0f;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;

        switch (GameFlow.Mode)
        {
            case GameMode.Title:
                pressKey.alpha = 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2f));
                if ((kb != null && kb.anyKey.wasPressedThisFrame) ||
                    (mouse != null && mouse.leftButton.wasPressedThisFrame))
                    SetMode(GameMode.MainMenu);
                break;

            case GameMode.MainMenu:
                if (esc && slotsPanel.activeSelf) CloseSlots();
                break;

            case GameMode.Playing:
                // first Esc deselects (SelectionManager), Esc with nothing selected opens the pause menu
                if (esc && !hadSelection) SetMode(GameMode.Paused);
                autosaveTimer += Time.unscaledDeltaTime;
                if (autosaveTimer >= AutosaveInterval) { autosaveTimer = 0f; AutoSave(); }
                break;

            case GameMode.Paused:
                if (esc)
                {
                    if (slotsPanel.activeSelf) CloseSlots();
                    else SetMode(GameMode.Playing);
                }
                break;
        }
    }

    void LateUpdate()
    {
        var sel = SelectionManager.Instance;
        hadSelection = sel != null && sel.Selected != null;
    }

    // ------------------------------------------------------------------ actions

    void OnContinue()
    {
        int s = SaveSystem.MostRecentSlot();
        if (s >= 0) DoLoad(s);
    }

    void OnNewGame()
    {
        if (galaxy == null) return;
        galaxy.StartNewGame();
        SetMode(GameMode.Playing);
        if (SelectionManager.Instance != null) SelectionManager.Instance.FocusHome();
    }

    void OnLoadGame() => OpenSlots(false, mainPanel);
    void OnSaveGame() => OpenSlots(true, pausePanel);
    void OnLoadFromPause() => OpenSlots(false, pausePanel);
    void OnResume() => SetMode(GameMode.Playing);
    void OnPauseClicked() => SetMode(GameMode.Paused);

    void OnMainMenu()
    {
        AutoSave();
        SetMode(GameMode.MainMenu);
    }

    void OnQuit()
    {
        AutoSave();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnHomeClicked()
    {
        if (SelectionManager.Instance != null) SelectionManager.Instance.FocusHome();
    }

    // ------------------------------------------------------------------ saving / loading

    void AutoSave()
    {
        if (GameFlow.Mode == GameMode.Playing || GameFlow.Mode == GameMode.Paused) SaveGame(0);
    }

    bool SaveGame(int slot)
    {
        if (galaxy == null || galaxy.Galaxy == null) return false;
        return SaveSystem.Write(slot, galaxy.CaptureSave());
    }

    void DoLoad(int slot)
    {
        var d = SaveSystem.Read(slot);
        if (d == null) { SetStatus("Could not read that save."); return; }
        if (galaxy == null || !galaxy.LoadGame(d)) { SetStatus("Failed to load that save."); return; }
        SetMode(GameMode.Playing);
    }

    void RefreshContinue()
    {
        bool has = SaveSystem.MostRecentSlot() >= 0;
        SetButton(continueBtn, "Continue", has);
    }

    // ------------------------------------------------------------------ slot screen

    void OpenSlots(bool saving, GameObject returnTo)
    {
        slotsSaving = saving;
        slotsReturn = returnTo;
        pendingConfirm = "";
        returnTo.SetActive(false);
        slotsPanel.SetActive(true);
        SetStatus("");
        RefreshSlots();
    }

    void CloseSlots()
    {
        slotsPanel.SetActive(false);
        if (slotsReturn != null) slotsReturn.SetActive(true);
        if (GameFlow.Mode == GameMode.MainMenu) RefreshContinue();
    }

    void SetStatus(string s)
    {
        if (slotsStatus != null) slotsStatus.text = s;
    }

    static string SlotName(int slot) => slot == 0 ? "Autosave" : "Slot " + slot;

    static string Describe(SaveData d)
    {
        int t = (int)d.gameTime;
        return d.currentSystemName + "  -  T+" + (t / 60).ToString("00") + ":" + (t % 60).ToString("00") +
               "  -  " + d.timestamp;
    }

    void RefreshSlots()
    {
        slotsHeading.text = slotsSaving ? "SAVE GAME" : "LOAD GAME";
        foreach (var r in rows)
        {
            r.root.SetActive(!(slotsSaving && r.slot == 0));   // can't manually save over the autosave
            var d = SaveSystem.Read(r.slot);
            r.info.text = "<b>" + SlotName(r.slot) + "</b>\n<size=75%>" +
                          (d != null ? Describe(d) : UITheme.Dim("Empty")) + "</size>";

            string actionText = slotsSaving
                ? (pendingConfirm == "save" + r.slot ? "Overwrite?" : "Save")
                : "Load";
            SetButton(r.action, actionText, slotsSaving || d != null);
            SetButton(r.delete, pendingConfirm == "del" + r.slot ? "Sure?" : "Delete", d != null);
        }
    }

    void OnRowAction(int slot)
    {
        if (!slotsSaving) { DoLoad(slot); return; }

        string key = "save" + slot;
        if (SaveSystem.Exists(slot) && pendingConfirm != key)
        {
            pendingConfirm = key;
            SetStatus("Click again to overwrite " + SlotName(slot));
        }
        else
        {
            pendingConfirm = "";
            SetStatus(SaveGame(slot) ? "Saved to " + SlotName(slot) : "Save failed - see console");
        }
        RefreshSlots();
    }

    void OnRowDelete(int slot)
    {
        string key = "del" + slot;
        if (pendingConfirm != key)
        {
            pendingConfirm = key;
            SetStatus("Click again to delete " + SlotName(slot));
        }
        else
        {
            pendingConfirm = "";
            SaveSystem.Delete(slot);
            SetStatus("Deleted " + SlotName(slot));
        }
        RefreshSlots();
    }

    // ------------------------------------------------------------------ building the UI

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    void BuildCanvas()
    {
        var cgo = new GameObject("MenuCanvas");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // above the HUD (100)
        cgo.AddComponent<CanvasScaler>();
        cgo.AddComponent<UIFit>();            // fits the 1920x1080 layout to any aspect ratio
        cgo.AddComponent<GraphicRaycaster>();

        // HUD buttons respect notches / rounded corners; full-screen panels intentionally don't
        var safe = UITheme.Rect("SafeArea", cgo.transform);
        Fill(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        BuildHudButtons(safe);
        BuildTitle(cgo.transform);
        BuildMain(cgo.transform);
        BuildPause(cgo.transform);
        BuildSlots(cgo.transform);
    }

    void BuildHudButtons(Transform parent)
    {
        var rt = UITheme.Rect("HudButtons", parent);
        Place(rt, new Vector2(1f, 1f), new Vector2(-12f, -54f), new Vector2(250f, 38f));
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleRight;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        UITheme.MakeButton(rt, "Home (H)", 120f, 38f, OnHomeClicked, 16f);
        UITheme.MakeButton(rt, "Menu (Esc)", 122f, 38f, OnPauseClicked, 16f);
        hudButtons = rt.gameObject;
    }

    void BuildTitle(Transform parent)
    {
        var p = FullPanel("TitleScreen", parent, new Color(0f, 0.01f, 0.03f, 0.35f));
        titlePanel = p.gameObject;

        var t = UITheme.MakeText(p, "GameTitle", 120, TextAlignmentOptions.Midline, FontStyles.Bold);
        t.text = GameTitle;
        t.color = UITheme.TextMain;
        t.characterSpacing = 12f;
        t.enableAutoSizing = true; t.fontSizeMin = 36f; t.fontSizeMax = 120f;   // long names still fit
        Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1700f, 180f));

        Line(p, new Vector2(0f, 0f), 900f);

        var s = UITheme.MakeText(p, "Subtitle", 28, TextAlignmentOptions.Midline);
        s.text = "A galaxy of procedural worlds awaits";
        s.color = UITheme.Accent;
        s.characterSpacing = 4f;
        Place(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1000f, 50f));

        pressKey = UITheme.MakeText(p, "PressKey", 30, TextAlignmentOptions.Midline, FontStyles.Bold);
        pressKey.text = "PRESS ANY KEY TO START";
        pressKey.characterSpacing = 6f;
        Place(pressKey.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(1000f, 50f));
    }

    void BuildMain(Transform parent)
    {
        var p = FullPanel("MainMenu", parent, new Color(0f, 0.01f, 0.03f, 0.6f));
        mainPanel = p.gameObject;

        var t = UITheme.MakeText(p, "GameTitle", 84, TextAlignmentOptions.Midline, FontStyles.Bold);
        t.text = GameTitle;
        t.color = UITheme.TextMain;
        t.characterSpacing = 10f;
        t.enableAutoSizing = true; t.fontSizeMin = 30f; t.fontSizeMax = 84f;
        Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 280f), new Vector2(1400f, 130f));

        Line(p, new Vector2(0f, 205f), 700f);

        var list = ButtonColumn(p, 380f, -10f,
            new[] { "Continue", "New Game", "Load Game", "Quit" },
            new UnityAction[] { OnContinue, OnNewGame, OnLoadGame, OnQuit });
        continueBtn = list[0];
    }

    void BuildPause(Transform parent)
    {
        var p = FullPanel("PauseMenu", parent, new Color(0f, 0.01f, 0.03f, 0.65f));
        pausePanel = p.gameObject;

        var card = Card("Card", p, new Vector2(460f, 490f));
        var h = UITheme.MakeText(card, "Heading", 40, TextAlignmentOptions.Midline, FontStyles.Bold);
        h.text = "PAUSED";
        h.color = UITheme.Accent;
        h.characterSpacing = 8f;
        Place(h.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(400f, 60f));

        ButtonColumn(card, 380f, -40f,
            new[] { "Resume", "Save Game", "Load Game", "Main Menu", "Quit to Desktop" },
            new UnityAction[] { OnResume, OnSaveGame, OnLoadFromPause, OnMainMenu, OnQuit });
    }

    void BuildSlots(Transform parent)
    {
        var p = FullPanel("SlotsScreen", parent, new Color(0f, 0.01f, 0.03f, 0.75f));
        slotsPanel = p.gameObject;

        var card = Card("Card", p, new Vector2(820f, 600f));

        slotsHeading = UITheme.MakeText(card, "Heading", 38, TextAlignmentOptions.Midline, FontStyles.Bold);
        slotsHeading.color = UITheme.Accent;
        slotsHeading.characterSpacing = 8f;
        Place(slotsHeading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(700f, 60f));

        var list = UITheme.Rect("Rows", card);
        list.anchorMin = new Vector2(0f, 1f);
        list.anchorMax = new Vector2(1f, 1f);
        list.pivot = new Vector2(0.5f, 1f);
        list.anchoredPosition = new Vector2(0f, -100f);
        list.sizeDelta = new Vector2(-48f, (SaveSystem.SlotCount + 1) * 84f + SaveSystem.SlotCount * 10f);
        var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        for (int s = 0; s <= SaveSystem.SlotCount; s++) rows.Add(BuildRow(list, s));

        slotsStatus = UITheme.MakeText(card, "Status", 18, TextAlignmentOptions.Midline);
        slotsStatus.color = UITheme.Accent;
        Place(slotsStatus.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(760f, 30f));

        var back = UITheme.MakeButton(card, "Back", 200f, 52f, CloseSlots, 22f);
        Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(200f, 52f));
    }

    SlotRow BuildRow(Transform parent, int slot)
    {
        var rt = UITheme.Rect("Slot" + slot, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.85f, 1f, 0.05f);
        UITheme.AddFrame(rt, UITheme.AccentDim);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 84f;

        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(16, 14, 8, 8);
        h.spacing = 12f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var row = new SlotRow { slot = slot, root = rt.gameObject };

        row.info = UITheme.MakeText(rt, "Info", 22, TextAlignmentOptions.MidlineLeft);
        var il = row.info.gameObject.AddComponent<LayoutElement>();
        il.minWidth = 100f;
        il.flexibleWidth = 1f;

        int captured = slot;
        row.action = UITheme.MakeButton(rt, "Load", 140f, 52f, () => OnRowAction(captured), 20f);
        row.delete = UITheme.MakeButton(rt, "Delete", 110f, 52f, () => OnRowDelete(captured), 20f);
        return row;
    }

    // ------------------------------------------------------------------ UI helpers

    List<Button> ButtonColumn(Transform parent, float width, float centerY, string[] labels, UnityAction[] actions)
    {
        const float H = 58f, S = 14f;
        var col = UITheme.Rect("Buttons", parent);
        Place(col, new Vector2(0.5f, 0.5f), new Vector2(0f, centerY),
              new Vector2(width, labels.Length * H + (labels.Length - 1) * S));
        var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = S;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = true;

        var list = new List<Button>();
        for (int i = 0; i < labels.Length; i++) list.Add(UITheme.MakeButton(col, labels[i], width, H, actions[i], 24f));
        return list;
    }

    static void SetButton(Button b, string text, bool enabled)
    {
        b.interactable = enabled;
        var t = b.GetComponentInChildren<TMP_Text>();
        if (t == null) return;
        t.text = text;
        t.color = enabled ? UITheme.TextMain : new Color(1f, 1f, 1f, 0.35f);
    }

    /// <summary>Thin glowing divider line.</summary>
    static void Line(Transform parent, Vector2 pos, float width)
    {
        var rt = UITheme.Rect("Line", parent);
        Place(rt, new Vector2(0.5f, 0.5f), pos, new Vector2(width, 2f));
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(UITheme.Accent.r, UITheme.Accent.g, UITheme.Accent.b, 0.6f);
        img.raycastTarget = false;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static RectTransform FullPanel(string name, Transform parent, Color color)
    {
        var rt = UITheme.Rect(name, parent);
        Fill(rt);
        rt.gameObject.AddComponent<Image>().color = color;   // raycast target: blocks clicks to the game below
        return rt;
    }

    static RectTransform Card(string name, Transform parent, Vector2 size)
    {
        var rt = UITheme.Panel(parent, name, true);
        Place(rt, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        return rt;
    }
}
