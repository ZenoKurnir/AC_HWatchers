using AC.Scene;
using AC.Scene.Explore;
using AC.User;
using Character;
using H;
using UnityEngine;
using UnityEngine.UI;

namespace AC_HWatchers;

// With the detailed log on: what the mod sees of Aicomi's H, written to the log a few seconds after H starts.
internal static class Report
{
    internal static float Scheduled = -1f;
    private static bool _wasParticipation;

    internal static void Tick(HScene scene, float now)
    {
        if (Scheduled > 0 && now >= Scheduled && Plugin.DebugLog.Value)
        {
            Scheduled = -1f;
            try { Write(scene); } catch (Exception e) { Plugin.Log.LogWarning($"Report failed: {e}"); }
        }
        // The game's own 3P invite button: when it shows up, and what it offers.
        try
        {
            var btn = scene._sprite?._btnParticipation;
            var on = btn != null && btn.gameObject.activeInHierarchy && btn.interactable;
            if (on != _wasParticipation) Plugin.Debug($"3P invite button {(on ? "available" : "not available")}.");
            _wasParticipation = on;
        }
        catch { }
    }

    internal static void LogInput(HScene.InputParameter p)
    {
        var lines = new List<string> { "H starts with:" };
        lines.Add($"  external participation {SafeStr(() => p.ExternalParticipation.ToString())}, 5P {SafeStr(() => p.Is5P.ToString())}, " +
                  $"M3P {SafeStr(() => p.IsM3P.ToString())}, free H {SafeStr(() => p.IsFreeH.ToString())}, map {SafeStr(() => p.MapID.Value.ToString())}, " +
                  $"place {SafeStr(() => p.PlaceID.Value.ToString())}, posture {SafeStr(() => p.PostureH.ToString())}, wet {SafeStr(() => p.IsWet.ToString())}, " +
                  $"start coordinate {SafeStr(() => p.StartCoordinateType.Value.ToString())}");
        void Actors(string what, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<HActor> list)
        {
            for (var i = 0; list != null && i < list.Length; i++)
            {
                var a = list[i];
                if (a == null) { lines.Add($"  {what} {i}: none"); continue; }
                lines.Add($"  {what} {i}: {Name(a.ActorData?.HumanData)} sex {a.Sex}, {DataLine(a.ActorData)}");
            }
        }
        try { Actors("receiver", p.Receivers); } catch (Exception e) { lines.Add($"  receivers: {e.Message}"); }
        try { Actors("attacker", p.Attackers); } catch (Exception e) { lines.Add($"  attackers: {e.Message}"); }
        Plugin.Debug(string.Join("\n", lines));
    }

    internal static void LogIntrusions(Il2CppSystem.Collections.Generic.List<Actor> candidates, Actor target)
    {
        var names = new List<string>();
        for (var i = 0; candidates != null && i < candidates.Count; i++)
        {
            var c = candidates[i];
            names.Add($"{Name(c?.BaseData?.HumanData)} ({DataLine(c?.BaseData)}, {Dist(c)})");
        }
        Plugin.Debug($"3P candidates the game found for {Name(target?.BaseData?.HumanData)}: {names.Count} candidates: {string.Join("; ", names)}");
    }

    private static string Dist(Actor a)
    {
        try
        {
            var s = Watchers.Scene;
            if (s == null) return "-";
            return $"{(a.Transform.position - Watchers.SceneCenter(s)).magnitude / Watchers.Scale(s):0.0} m";
        }
        catch { return "-"; }
    }

    internal static string DataLine(ActorData d)
    {
        if (d == null) return "no save data";
        try
        {
            var state = HScene.HStatus.GetState(d.HCount, d.Sexperience);
            return $"virgin {d.IsVirgin}, experience {d.Sexperience}, H count {d.HCount}, lewdness {d.Lewdness}, favor {d.Favor}, " +
                   $"state {state}, habit {HScene.HStatus.IsHabitH(d.HCount, d.Sexperience)}, likes girls {Traits.LikesGirls(d.HumanData)}, traits [{Traits.List(d.HumanData)}]";
        }
        catch (Exception e) { return $"data error {e.Message}"; }
    }

    internal static string Name(HumanData h) => Watchers.SafeName(() => h.Parameter.fullname) ?? "?";

    private static void Write(HScene scene)
    {
        var lines = new List<string> { $"Report (AC_HWatchers {Plugin.Version})" };
        var k = Watchers.Scale(scene);
        var center = Watchers.SceneCenter(scene);
        lines.Add($"  map {SafeInt(() => scene.GetMapID())}, scene center {center}, scale {k:0.00}/m, 5P {SafeBool(() => scene._inputParameter.Is5P)}, " +
                  $"participation {SafeStr(() => scene._inputParameter.ExternalParticipation.ToString())}, borders [{SafeStr(() => string.Join(",", HScene.HStatus.SEXPERIENCE_BORDERS))}]");
        try
        {
            var all = scene._hActorAll;
            for (var i = 0; all != null && i < all.Length; i++)
            {
                var a = all[i];
                if (a == null) continue;
                bool vis; try { vis = a.IsVisible(); } catch { vis = false; }
                lines.Add($"  actor {i}: {Name(a.ActorData?.HumanData)} sex {a.Sex}, visible {vis}, active {a.Human?.gameObject?.activeInHierarchy}, virgin {a.IsVirgin}, " +
                          $"experience {a.Sexperience}, lewd {SafeBool(() => a.IsLewd(false))}, habit {SafeBool(() => a.IsHabitH(false))}, size {Watchers.Size(a.Human)}, " +
                          $"animator {SafeStr(() => a.Human.body.animBody.runtimeAnimatorController.name)}");
            }
        }
        catch (Exception e) { lines.Add($"  actors: {e.Message}"); }

        var explore = ExploreScene.Instance;
        lines.Add($"  explore scene {(explore != null ? "loaded" : "missing")}, player at {SafeStr(() => explore.Player.Transform.position.ToString())}");
        foreach (var (npc, dist) in Watchers.Nearby(scene))
        {
            try
            {
                var h = npc.Params?.Chara;
                var anim = h?.body?.animBody;
                var clips = new List<string>();
                try { foreach (var c in anim.runtimeAnimatorController.animationClips) { if (clips.Count >= 12) break; clips.Add(c.name); } } catch { }
                var shown = 0; var total = 0;
                try { foreach (var r in h.gameObject.GetComponentsInChildren<Renderer>(false)) { total++; if (r.enabled && r.isVisible) shown++; } } catch { }
                lines.Add($"  girl {Name(npc.BaseData?.HumanData)}: {dist:0.0} m, area {SafeInt(() => npc.AreaID)}, {DataLine(npc.BaseData)}");
                lines.Add($"     body active {SafeBool(() => h.gameObject.activeInHierarchy)}, visibleAll {SafeBool(() => h.visibleAll)}, renderers drawn {shown}/{total}, " +
                          $"state {SafeInt(() => npc.CurrentStateID)}, animator {SafeStr(() => anim.runtimeAnimatorController.name)} " +
                          $"(state hash {SafeInt(() => anim.GetCurrentAnimatorStateInfo(0).fullPathHash)}), clips {string.Join(",", clips)}");
            }
            catch (Exception e) { lines.Add($"  girl: {e.Message}"); }
        }

        // Where the H screen's buttons are, so the watcher menu can go where nothing else is.
        try
        {
            var sprite = scene._sprite;
            if (sprite != null)
            {
                var canvas = sprite.Canvas;
                lines.Add($"  screen {Screen.width}x{Screen.height}, canvas {canvas?.name} mode {canvas?.renderMode}");
                foreach (var sel in sprite.GetComponentsInChildren<Selectable>(false))
                {
                    if (sel == null || !sel.gameObject.activeInHierarchy) continue;
                    var rt = sel.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    var r = ScreenRect(rt, canvas);
                    lines.Add($"     ui {Path(sel.transform, sprite.transform)}: x {r.xMin:0}-{r.xMax:0}, y {r.yMin:0}-{r.yMax:0} (from top)");
                }
            }
        }
        catch (Exception e) { lines.Add($"  ui: {e.Message}"); }
        lines.Add($"  watchers: {string.Join(", ", Watchers.Present.Select(w => $"{w.Name} ({w.Pose ?? w.TokSet ?? "-"})"))}");
        Plugin.Log.LogInfo(string.Join("\n", lines));
    }

    // Screen rectangle with y measured from the top, like IMGUI.
    internal static Rect ScreenRect(RectTransform rt, Canvas canvas)
    {
        var corners = new Vector3[4];
        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(arr);
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var p = RectTransformUtility.WorldToScreenPoint(cam, arr[i]);
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, Screen.height - y1, x1, Screen.height - y0);
    }

    private static string Path(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var i = 0; t != null && t != root && i < 6; i++, t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    private static string SafeStr(Func<string> f) { try { return f(); } catch { return "?"; } }
    private static string SafeInt(Func<int> f) { try { return f().ToString(); } catch { return "?"; } }
    private static string SafeBool(Func<bool> f) { try { return f().ToString(); } catch { return "?"; } }
}
