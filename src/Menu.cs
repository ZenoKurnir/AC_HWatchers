using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace AC_HWatchers;

// Ctrl + right-click (without dragging) on a watcher opens her menu. A virgin only watches: her menu shows,
// but every option is locked.
public class WatcherMenu : MonoBehaviour
{
    public WatcherMenu(IntPtr pointer) : base(pointer) { }

    private static readonly GUILayoutOption[] None = Array.Empty<GUILayoutOption>();
    private static readonly string[] PartNames = { "Top", "Bottom", "Bra", "Panties", "Gloves", "Pantyhose", "Socks", "Shoes" };
    private static readonly string[] StateNames = { "on", "half", "off" };

    private static int _skip;
    private static readonly List<Action> _rows = new();
    private static Rect _rect;
    private static Rect _fiveRect;       // the 5P toggle next to the game's 3P button; empty when not shown
    private static float _nextButtonCheck;
    private static UnityEngine.EventSystems.EventSystem _blocked;
    private Vector3 _downAt;
    private bool _down;

    public static void Create()
    {
        ClassInjector.RegisterTypeInIl2Cpp<WatcherMenu>();
        var go = new GameObject("AC_HWatchers");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<WatcherMenu>();
    }

    private void Update()
    {
        try { Watchers.Update(); }
        catch (Exception e) { Plugin.Log.LogError($"Watcher update failed: {e}"); }
        try { BlockGameUi(); } catch { }
        try
        {
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetMouseButtonDown(1) && ctrl) { _downAt = Input.mousePosition; _down = true; }
            if (Input.GetMouseButtonUp(1) && !ctrl) _down = false;
            if (Input.GetMouseButtonUp(1) && _down && ctrl)
            {
                _down = false;
                if ((Input.mousePosition - _downAt).magnitude < 6f) Watchers.PickAt(Input.mousePosition);
            }
        }
        catch { }
    }

    private void OnGUI()
    {
        try { DrawFiveToggle(); } catch { }
        var w = Watchers.Selected;
        if (w?.Human == null) return;
        try { Draw(w); }
        catch (Exception e) { Plugin.Log.LogWarning($"Menu failed: {e.Message}"); Watchers.Selected = null; }
    }

    // The game's buttons under the menu (the motion list opens on the right) would take the same clicks, so the
    // game's UI stops listening while the mouse is over the menu.
    private static void BlockGameUi()
    {
        PlaceFiveToggle();
        var m = Input.mousePosition;
        var mouse = new Vector2(m.x, Screen.height - m.y);
        var over = (Watchers.Selected?.Human != null && _rect.Contains(mouse)) || (_fiveRect.width > 0 && _fiveRect.Contains(mouse));
        if (over && _blocked == null)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.enabled) { es.enabled = false; _blocked = es; }
        }
        else if (!over && _blocked != null)
        {
            try { _blocked.enabled = true; } catch { }
            _blocked = null;
        }
    }

    // The 5P toggle sits right of the game's 3P button while that button shows and enough watchers can join.
    private static void PlaceFiveToggle()
    {
        if (Time.realtimeSinceStartup < _nextButtonCheck) return;
        _nextButtonCheck = Time.realtimeSinceStartup + 0.25f;
        _fiveRect = default;
        try
        {
            var scene = Watchers.Scene;
            if (scene == null || !FiveP.Offered) return;
            // The 3P invite shows as the "Feeling watched.." bubble (ExternalParticipation/btnGaze).
            UnityEngine.UI.Selectable btn = scene._sprite?._btnGaze;
            if (btn == null || !btn.gameObject.activeInHierarchy) btn = scene._sprite?._btnParticipation;
            if (btn == null || !btn.gameObject.activeInHierarchy) return;
            var rt = btn.GetComponent<RectTransform>();
            if (rt == null) return;
            var r = Report.ScreenRect(rt, scene._sprite.Canvas);
            var h = Mathf.Max(28f, r.height * 0.6f);
            _fiveRect = new Rect(r.xMax + 10f, r.center.y - h * 0.5f, 70f, h);
        }
        catch { _fiveRect = default; }
    }

    private static void DrawFiveToggle()
    {
        if (_fiveRect.width <= 0 || !FiveP.Offered) return;
        if (GUI.Button(_fiveRect, FiveP.Wanted ? "<b>5P ✓</b>" : "5P")) FiveP.Toggle();
    }

    // No GUI scroll view (the game ships it stripped on some installs): the rows are listed, and the menu shows
    // as many as fit, moved with the arrows or the mouse wheel.
    private static void Draw(Watcher w)
    {
        const float width = 280f;
        const float rowHeight = 25f;
        // Right side, between the motion list (top right) and the gauges (bottom right); the game's own H buttons
        // sit in the left column, the clothes panel next to it, and the 3P button at the bottom centre.
        var top = Screen.height * 0.18f;
        var height = Screen.height * 0.85f - top;
        _rect = new Rect(Screen.width - width - 30f, top, width, height);
        _rows.Clear();
        BuildRows(w);

        var fit = Mathf.Max(3, (int)((height - 3 * rowHeight - 16f) / rowHeight));
        try
        {
            var e = Event.current;
            if (e != null && e.type == EventType.ScrollWheel && _rect.Contains(e.mousePosition))
            {
                _skip += e.delta.y > 0 ? 2 : -2;
                e.Use();
            }
        }
        catch { }
        _skip = Mathf.Clamp(_skip, 0, Mathf.Max(0, _rows.Count - fit));

        GUILayout.BeginArea(_rect, GUI.skin.box);
        GUI.enabled = _skip > 0;
        if (_rows.Count > fit && GUILayout.Button("▲", None)) _skip = Mathf.Max(0, _skip - 3);
        GUI.enabled = true;
        for (var i = _skip; i < _rows.Count && i < _skip + fit; i++)
        {
            GUI.enabled = true;
            try { _rows[i](); } catch (Exception ex) { Plugin.Debug($"Menu row failed: {ex.Message}"); }
        }
        GUI.enabled = _skip + fit < _rows.Count;
        if (_rows.Count > fit && GUILayout.Button("▼", None)) _skip += 3;
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", None)) Watchers.Selected = null;
        GUILayout.EndArea();
    }

    private static void Row(Action a) => _rows.Add(a);

    // The pose list that is open, if any ("les" or "caress"), whose menu it is and with whom.
    private static string _page;
    private static Watcher _pageFor, _pageWith;

    private static void Open(string page, Watcher w, Watcher with)
    {
        _page = page; _pageFor = w; _pageWith = with; _skip = 0;
    }

    private static void BuildRows(Watcher w)
    {
        var locked = w.Locked;
        Row(() => GUILayout.Label($"<b>{w.Name}</b>", None));
        Row(() => GUILayout.Label($"Experience {w.Sexperience}/100{(w.LikesGirls ? ", likes girls" : "")}", None));
        if (locked) Row(() => GUILayout.Label("She's a virgin: she only watches.", None));
        if (w.Tired) Row(() => GUILayout.Label("She's worn out from H and stays lying down.", None));
        Row(() => GUILayout.Label(Watchers.Scene != null && Moving.PanelOpen(Watchers.Scene)
            ? "<i>The move panel now moves her.</i>" : "<i>Open the move panel (left) to move her.</i>", None));

        var busy = w.Lover != null;
        var outfit = Watchers.Outfit(w);
        if (outfit >= 0 && !busy)
            Row(() => { GUI.enabled = !locked; if (GUILayout.Button($"Outfit: {(ChaFileDefine.CoordinateType)outfit}  (next)", None)) Watchers.SetOutfit(w, (outfit + 1) % 4); });

        Row(() =>
        {
            GUI.enabled = !locked;
            GUILayout.BeginHorizontal(None);
            if (GUILayout.Button("Dressed", None)) Watchers.Redress(w, ChaFileDefine.ClothesState.Clothing);
            if (GUILayout.Button("Half", None)) Watchers.Redress(w, ChaFileDefine.ClothesState.HalfUndress);
            if (GUILayout.Button("Naked", None)) Watchers.Redress(w, ChaFileDefine.ClothesState.Naked);
            GUILayout.EndHorizontal();
        });

        var cloth = w.Human.cloth;
        if (cloth != null)
            foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
            {
                bool exists;
                try { exists = cloth.IsClothesStateKind(kind) && cloth.IsExist(kind); } catch { exists = false; }
                if (!exists) continue;
                var k = kind;
                Row(() =>
                {
                    GUI.enabled = !locked;
                    var state = (int)cloth.GetClothesStateType(k);
                    if (GUILayout.Button($"{PartNames[(int)k % PartNames.Length]}: {StateNames[state % 3]}", None)) Watchers.CyclePart(w, k);
                });
            }

        if (Plugin.GirlPairs.Value)
        {
            if (w.Lover != null) Row(() => { GUI.enabled = !locked; if (GUILayout.Button($"Stop making love with {w.Lover?.Name}", None)) Watchers.EndLove(w); });
            var choices = Watchers.LoveChoices(w);
            if (_page != null && (_pageFor != w || !choices.Contains(_pageWith))) _page = null;
            if (_page != null)
            {
                // One list open: every pose of that kind with that girl, then back to the menu.
                var o = _pageWith;
                var caress = _page == "caress";
                Row(() => GUILayout.Label($"<b>{(caress ? "Caress poses" : "Lesbian poses")} with {o.Name}</b>", None));
                foreach (var (set, name) in caress ? Watchers.CaressSets : Watchers.LoveSets)
                {
                    var st = set; var nm = name;
                    Row(() =>
                    {
                        GUI.enabled = !locked;
                        if (GUILayout.Button(w.Lover == o && w.LoveSet == st ? $"<b>{nm} ✓</b>" : nm, None)) { Watchers.SwitchLove(w, o, st); _page = null; _skip = 0; }
                    });
                }
                Row(() => { if (GUILayout.Button("Back", None)) { _page = null; _skip = 0; } });
            }
            else
            {
                if (choices.Count > 0) Row(() => GUILayout.Label(w.Lover != null ? "Switch to:" : "Make love with:", None));
                foreach (var other in choices)
                {
                    var o = other;
                    Row(() => GUILayout.Label(o.Name, None));
                    Row(() =>
                    {
                        GUI.enabled = !locked;
                        GUILayout.BeginHorizontal(None);
                        if (GUILayout.Button("Lesbian poses", None)) Open("les", w, o);
                        if (GUILayout.Button("Caress poses", None)) Open("caress", w, o);
                        GUILayout.EndHorizontal();
                    });
                }
            }
            if (!w.LikesGirls && !locked) Row(() => GUILayout.Label("<i>She doesn't like girls.</i>", None));
        }

        if (Plugin.Masturbate.Value && !busy && !w.Tired)
        {
            if (w.TokSet == null)
            {
                Row(() => GUILayout.Label("Touch herself:", None));
                Row(() =>
                {
                    GUI.enabled = !locked;
                    GUILayout.BeginHorizontal(None);
                    foreach (var (set, name) in Watchers.TokSets)
                        if (GUILayout.Button(name, None)) Watchers.StartTouching(w, set);
                    GUILayout.EndHorizontal();
                });
            }
            else Row(() => { GUI.enabled = !locked; if (GUILayout.Button("Stop touching herself", None)) Watchers.StopTouching(w); });
        }

        var scene = Watchers.Scene;
        var targets = Swaps.Targets(scene);
        if (targets.Count > 0)
        {
            if (Swaps.CanSwap(w))
                foreach (var (actor, name) in targets)
                {
                    var a = actor; var n = name;
                    Row(() => { if (GUILayout.Button($"Take {n}'s place", None)) { Watchers.Selected = null; Swaps.SwapIn(scene, w, a); } });
                }
            else if (!locked) Row(() => GUILayout.Label("Not experienced enough to join yet.", None));
        }
        // With one girl in the scene, the 3P button can still bring another.
        if (targets.Count >= 2) { }
        else if (Swaps.PendingJoin == w.Key)
        {
            Row(() => GUILayout.Label("The 3P button will bring her.", None));
            Row(() => { if (GUILayout.Button("Let the game pick who joins", None)) Swaps.ChooseJoiner(w); });
        }
        else if (Invite.IsFirst(w) && Swaps.PendingJoin == IntPtr.Zero) Row(() => GUILayout.Label("The 3P button will bring her.", None));
        else if (Invite.CanJoin(w) || (Swaps.Enabled && Swaps.CanSwap(w) && Invite.Any))
        {
            Row(() => GUILayout.Label("Experienced enough for 3P.", None));
            if (Swaps.Enabled) Row(() => { if (GUILayout.Button("Join with the 3P button", None)) Swaps.ChooseJoiner(w); });
        }
        else if (!locked && !Invite.Eligible(w.Data) && targets.Count == 0) Row(() => GUILayout.Label("Not experienced enough for 3P yet.", None));
        Row(() => { if (GUILayout.Button("Send her back", None)) Watchers.SendBack(w); });
    }
}
