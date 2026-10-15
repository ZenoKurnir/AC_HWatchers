using AC.User;
using H;
using HarmonyLib;
using UnityEngine;

namespace AC_HWatchers;

// H experience for every girl who took part. The game credits only the girl it started H with: her experience,
// her parts (mouth, breast, nipple, groin, anal, hip), her H count. Here every girl who played (a watcher swapped
// in, the 3P girl, the 5P girls) gets the same gains the game gave the main girl, once H is over.
// The 3P button (and the 5P) restarts H as a new scene, which would lose what the first scene earned; that is
// carried into the new scene.
internal static class Experience
{
    private const int Max = 100;
    private static readonly Dictionary<IntPtr, ActorData> _played = new();
    private static readonly Dictionary<IntPtr, Snap> _start = new();
    private static float _noSceneSince = -1f;
    private static float _nextTick;
    private static IntPtr _scenePtr;
    private static bool _restarting;

    // What a scene that was restarted had earned so far.
    private static float[] _carryParts;
    private static int[] _carryResult;
    private static float _carryPoint;

    private sealed class Snap
    {
        public int Sexp, HCount, Lewd;
        public int[] Parts;
    }

    private static Snap Take(ActorData d)
    {
        var s = new Snap { Sexp = d.Sexperience, HCount = d.HCount, Lewd = d.Lewdness, Parts = new int[6] };
        var r = d._resistFlags;
        for (var i = 0; r != null && i < r.Length && i < 6; i++) s.Parts[i] = r[i];
        return s;
    }

    private static string Text(Snap s) => $"experience {s.Sexp}, H count {s.HCount}, lewdness {s.Lewd}, parts [{string.Join(",", s.Parts)}]";

    private static string NameOf(ActorData d)
    {
        try { return Report.Name(d.HumanData); } catch { return "?"; }
    }

    private static string Earned(HScene scene)
    {
        try
        {
            var f = scene.ResistUpValues;
            var r = scene._resultResistUpValues;
            var p = scene._resultPoint;
            return $"parts earned [{(f == null ? "-" : string.Join(",", f.Select(x => x.ToString("0.#"))))}], " +
                   $"result parts [{(r == null ? "-" : string.Join(",", r))}], points {(p == null ? "-" : $"{p._addValue:0.#}/{p._maxValue:0.#}")}";
        }
        catch (Exception e) { return $"earned ? ({e.Message})"; }
    }

    // Every half second while a scene runs: the girls playing in it now.
    public static void Tick(HScene scene, float now)
    {
        _noSceneSince = -1f;
        if (_scenePtr != scene.Pointer) { _scenePtr = scene.Pointer; _restarting = false; GiveCarry(scene); }
        if (now < _nextTick) return;
        _nextTick = now + 0.5f;
        foreach (var a in Watchers.ActiveActors(scene))
        {
            try
            {
                if (a.Sex != 1 || a.ActorData == null) continue;
                var key = Swaps.KeyOf(a);
                if (key == IntPtr.Zero || _played.ContainsKey(key)) continue;
                var d = key == a.ActorData.Pointer ? a.ActorData : Swaps.NpcByKey(key)?.BaseData;
                if (d == null || Residents.Girl(d) == null) continue;
                _played[key] = d;
                if (!_start.ContainsKey(key)) _start[key] = Take(d);
                Plugin.Debug($"Experience: {NameOf(d)} takes part in this H ({Text(_start[key])}).");
            }
            catch { }
        }
    }

    // A scene ends because the 3P button (or a 5P) starts a new one: keep what it earned.
    internal static void SceneDisposing(HScene scene)
    {
        if (scene == null) return;
        try
        {
            if (scene.IsEnded) return;
            _restarting = true;
            Plugin.Debug($"Experience: the scene restarts; carrying {Earned(scene)}.");
            var f = scene.ResistUpValues;
            var r = scene._resultResistUpValues;
            _carryParts = f?.ToArray();
            _carryResult = r?.ToArray();
            _carryPoint = scene._resultPoint?._addValue ?? 0f;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Experience: keeping the scene's gains failed: {e.Message}"); }
    }

    private static void GiveCarry(HScene scene)
    {
        if (_carryParts == null && _carryResult == null && _carryPoint == 0f) return;
        try
        {
            var f = scene.ResistUpValues;
            for (var i = 0; f != null && _carryParts != null && i < f.Length && i < _carryParts.Length; i++) f[i] += _carryParts[i];
            var r = scene._resultResistUpValues;
            for (var i = 0; r != null && _carryResult != null && i < r.Length && i < _carryResult.Length; i++) r[i] += _carryResult[i];
            if (scene._resultPoint != null) scene._resultPoint._addValue += _carryPoint;
            Plugin.Debug($"Experience: the new scene starts with what the last one earned: {Earned(scene)}.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Experience: carrying the gains failed: {e.Message}"); }
        _carryParts = null; _carryResult = null; _carryPoint = 0f;
    }

    public static void NoScene()
    {
        if (_played.Count == 0 && _carryParts == null) return;
        if (_noSceneSince < 0f) { _noSceneSince = Time.realtimeSinceStartup; return; }
        var since = Time.realtimeSinceStartup - _noSceneSince;
        // The 3P button and the 5P end the scene and start a new one: keep everything until it starts.
        if (since < (_restarting ? 30f : 8f)) return;
        Forget();
    }

    private static void Forget()
    {
        _played.Clear();
        _start.Clear();
        _before.Clear();
        _carryParts = null; _carryResult = null; _carryPoint = 0f;
        _noSceneSince = -1f;
        _restarting = false;
    }

    private static readonly Dictionary<IntPtr, Snap> _before = new();

    // The game writes the H results (once, as H really ends): note everyone before, and share after.
    internal static void AtResult(HScene scene, HResult.InputParameter p, int sexperience, bool after)
    {
        var parts = new List<string> { $"points {sexperience}" };
        try { parts.Add($"parameter experience {p.Sexperience}, parameter parts [{(p.ResistUpValues == null ? "-" : string.Join(",", p.ResistUpValues))}]"); } catch { }
        if (scene != null) parts.Add(Earned(scene));
        Plugin.Debug($"Experience: {(after ? "after" : "before")} the game's results: {string.Join("; ", parts)}.");
        if (!after)
        {
            try { if (scene != null) { _nextTick = 0f; Tick(scene, float.MaxValue); _nextTick = 0f; } } catch { }
            _before.Clear();
            foreach (var kv in _played) try { _before[kv.Key] = Take(kv.Value); } catch { }
            return;
        }
        Share(p, sexperience);
    }

    // The gains the game gave the main girl go to every other girl who took part in this H.
    private static void Share(HResult.InputParameter p, int points)
    {
        try
        {
            var gain = new Snap { Sexp = Mathf.Max(0, points), Parts = new int[6] };
            try
            {
                var r = p.ResistUpValues;
                for (var i = 0; r != null && i < r.Length && i < 6; i++) gain.Parts[i] = Mathf.Max(0, r[i]);
            }
            catch { }
            var credited = new HashSet<IntPtr>();
            foreach (var kv in _played)
            {
                if (!_before.TryGetValue(kv.Key, out var was)) continue;
                var now = Take(kv.Value);
                var changed = now.Sexp != was.Sexp;
                for (var i = 0; i < 6; i++) changed |= now.Parts[i] != was.Parts[i];
                if (!changed) continue;
                credited.Add(kv.Key);
                gain.Sexp = Mathf.Max(gain.Sexp, now.Sexp - was.Sexp);
                for (var i = 0; i < 6; i++) gain.Parts[i] = Mathf.Max(gain.Parts[i], now.Parts[i] - was.Parts[i]);
                Plugin.Log.LogInfo($"Experience: the game gave {NameOf(kv.Value)} her results: {Text(_start.TryGetValue(kv.Key, out var s0) ? s0 : was)} -> {Text(now)}.");
            }
            Plugin.Log.LogInfo($"Experience: this H earned experience +{gain.Sexp}, parts [{string.Join(",", gain.Parts)}] " +
                               "(mouth, breast, nipple, groin, anal, hip).");
            if (!Plugin.SharedExperience.Value) { Plugin.Debug("Experience: sharing is off in the settings."); return; }
            foreach (var kv in _played)
            {
                if (credited.Contains(kv.Key)) continue;
                var d = kv.Value;
                var was = Take(d);
                _start.TryGetValue(kv.Key, out var first);
                d.Sexperience = Mathf.Clamp(was.Sexp + gain.Sexp, 0, Mathf.Max(Max, was.Sexp));
                // The game counts an H for each girl a scene starts with; the others get theirs here.
                if (first == null || was.HCount <= first.HCount) d.HCount = was.HCount + 1;
                var r = d._resistFlags;
                for (var i = 0; r != null && i < r.Length && i < 6; i++)
                    r[i] = Mathf.Clamp(r[i] + gain.Parts[i], 0, Mathf.Max(Max, r[i]));
                Plugin.Log.LogInfo($"Experience: {NameOf(d)} took part too: {Text(first ?? was)} -> {Text(Take(d))}.");
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Experience: {e.Message}"); }
        finally { Forget(); }
    }
}

[HarmonyPatch]
internal static class ExperienceHooks
{
    [HarmonyPrefix, HarmonyPatch(typeof(HResult), nameof(HResult.ActorResult))]
    private static void ActorResultPrefix(HScene hScene, HResult.InputParameter parameter, int sexperience)
    {
        try { Experience.AtResult(hScene, parameter, sexperience, false); } catch (Exception e) { Plugin.Log.LogWarning($"Experience before: {e.Message}"); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(HResult), nameof(HResult.ActorResult))]
    private static void ActorResultPostfix(HScene hScene, HResult.InputParameter parameter, int sexperience)
    {
        try { Experience.AtResult(hScene, parameter, sexperience, true); } catch (Exception e) { Plugin.Log.LogWarning($"Experience after: {e.Message}"); }
    }
}
