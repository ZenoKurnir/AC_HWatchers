using H;
using UnityEngine;

namespace AC_HWatchers;

// Tiredness ("faintness"). In Aicomi the scene keeps one count (FlagControl.Counter.Faintness): once it is reached,
// every girl in the scene is worn out and the tired version of each position plays. Here it is kept per girl:
// everyone in the scene when it happens stays tired for the rest of H, as a watcher too (lying down), even if a
// fresh girl swapped in makes the scene fresh again. A fresh girl swapped in starts fresh; a 3P or 5P keeps the
// girls all tired or all fresh where the watchers allow it, and otherwise follows the main girl.
internal static class Tired
{
    private static readonly HashSet<IntPtr> _tired = new();
    private static float _noSceneSince = -1f;
    private static bool? _plan;            // the state the next scene starts in, once its girls are swapped
    private static float _sceneSince = -1f;
    private static IntPtr _scenePtr;
    private static float _nextTick;
    private static string _lastProbe;
    private static bool _wasFaint;
    // Collapses taken off the scene's count to make it fresh, given back as the scene ends (for its results).
    private static int _faintDebt;
    // The scene's collapsed state (HScene.Status.FaintnessState), which is what picks the tired positions. Cleared to
    // make the scene fresh; what it was is kept to put back when the scene turns tired again or ends.
    private static (bool on, bool voice, int type)? _faintState;
    // The position playing keeps its tired or fresh motions until it is started again, so after the scene's state
    // changes the same position is picked again, as if from the position list (direct restart if that does nothing).
    private static float _restartAt = -1f;
    private static int _restartStep;
    private static bool _sawChange;

    // Last resort, after the position was started again: anyone still in a tired ("D_") motion while the scene is
    // fresh is moved to the same motion's fresh state, at the same point of it.
    private static void FixMotions(HScene scene, bool tired)
    {
        var report = new List<string>();
        foreach (var a in Watchers.ActiveActors(scene))
        {
            try
            {
                var anim = a.Animator;
                if (anim == null || anim.runtimeAnimatorController == null) continue;
                var names = new Dictionary<int, string>();
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                {
                    if (clip == null) continue;
                    var n = clip.name;
                    names[Animator.StringToHash(n)] = n;
                    var d = n.IndexOf("D_", StringComparison.Ordinal);
                    if (d > 0) names[Animator.StringToHash(n.Substring(d))] = n.Substring(d);
                    var u = n.LastIndexOf('_');
                    if (u > 0 && u < n.Length - 1) names[Animator.StringToHash(n.Substring(u + 1))] = n.Substring(u + 1);
                }
                var st = anim.GetCurrentAnimatorStateInfo(0);
                names.TryGetValue(st.shortNameHash, out var cur);
                var who = Report.Name(a.Human?.data);
                var dPos = cur?.IndexOf("D_", StringComparison.Ordinal) ?? -1;
                if (tired || dPos < 0) { report.Add($"{who} {cur ?? st.shortNameHash.ToString()}"); continue; }
                var fresh = cur.Remove(dPos, 2);
                var hash = Animator.StringToHash(fresh);
                if (!anim.HasState(0, hash)) { report.Add($"{who} {cur} (no fresh {fresh})"); continue; }
                anim.Play(hash, 0, st.normalizedTime % 1f);
                report.Add($"{who} {cur} -> {fresh}");
            }
            catch (Exception e) { report.Add($"? ({e.Message})"); }
        }
        Plugin.Debug($"Tiredness: motions after the restart ({(tired ? "tired" : "fresh")} scene): {string.Join(", ", report)}.");
    }

    private static void Restart(HScene scene, float now)
    {
        try
        {
            if (scene.IsNowChangeAnim()) { _sawChange = true; return; }
            if (now < _restartAt) return;
            var info = scene.NowAnimationInfo;
            if (info == null) { _restartAt = -1f; return; }
            if (_restartStep == 0)
            {
                _sawChange = false;
                scene.SetSelectAnimationListInfo(info);
                Plugin.Debug($"Tiredness: playing {info.NameAnimation} again so its motions match the scene.");
                _restartStep = 1; _restartAt = now + 1f;
                return;
            }
            if (_restartStep == 1)
            {
                if (!_sawChange)
                {
                    scene.ChangeAnimation(info, false, false, true, new Il2CppSystem.Threading.CancellationToken());
                    Plugin.Debug($"Tiredness: picking the position did nothing; started {info.NameAnimation} again directly.");
                }
                _restartStep = 2; _restartAt = now + 1.5f;
                return;
            }
            FixMotions(scene, SceneFaint(scene));
            _restartAt = -1f;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Tiredness: playing the position again failed: {e.Message}"); _restartAt = -1f; }
    }

    private static string StateText(HScene scene)
    {
        try { var f = scene.Status.FaintnessState; return f == null ? "none" : $"{(f.IsOn ? "on" : "off")} type {f.Type} voice {f.IsVoice}"; }
        catch (Exception e) { return $"? ({e.Message})"; }
    }

    // Clears (fresh) or brings back (tired) the collapsed state; true if it changed.
    // Every status object the scene's position handlers read (the scene's own, and any other a handler holds).
    private static List<HScene.HStatus> Statuses(HScene scene)
    {
        var list = new List<HScene.HStatus>();
        var seen = new HashSet<IntPtr>();
        void Add(HScene.HStatus s) { if (s != null && seen.Add(s.Pointer)) list.Add(s); }
        try { Add(scene.Status); } catch { }
        try
        {
            var procs = scene._procPostures;
            for (var i = 0; procs != null && i < procs.Length; i++)
                try { Add(procs[i]?._status); } catch { }
        }
        catch { }
        return list;
    }

    private static bool SetState(HScene scene, bool tired)
    {
        var all = Statuses(scene);
        var changed = false;
        for (var i = 1; i < all.Count; i++)
        {
            var g = all[i].FaintnessState;
            if (g == null) continue;
            if (!tired && g.IsOn) { g.Reset(); if (g.IsOn) g.IsOn = false; changed = true; Plugin.Debug($"Tiredness: a position handler had its own collapsed state; cleared it."); }
        }
        var f = all.Count > 0 ? all[0].FaintnessState : null;
        if (f == null) return changed;
        if (!tired)
        {
            if (!f.IsOn) return changed;
            _faintState = (f.IsOn, f.IsVoice, f.Type);
            f.Reset();
            if (f.IsOn) f.IsOn = false;
            return true;
        }
        if (f.IsOn || _faintState == null) return changed;
        var s = _faintState.Value;
        f.Type = s.type; f.IsVoice = s.voice; f.IsOn = true;
        return true;
    }

    public static void AtEnd(HScene scene)
    {
        if (_faintDebt == 0 && _faintState == null) return;
        try
        {
            if (!scene.IsEnded) return;
            var c = scene.CtrlFlag.Counter;
            c.Faintness = Mathf.Max(0, c.Faintness + _faintDebt);
            if (_faintState != null) SetState(scene, true);
            Plugin.Debug($"Tiredness: H ends; the scene's faintness count gets back {_faintDebt} (now {c.Faintness}), collapsed state {StateText(scene)}.");
        }
        catch { }
        _faintDebt = 0;
        _faintState = null;
    }
    // Who watched as the last scene ended, for picking tired or fresh girls for the next one.
    private static readonly List<(NPC npc, IntPtr key)> _pool = new();

    public static bool Is(IntPtr key) => _tired.Contains(key);

    private static bool Collapsed(HScene scene)
    {
        try { return scene.Status?.FaintnessState?.IsOn == true; } catch { return false; }
    }

    internal static bool SceneFaint(HScene scene)
    {
        try { return scene.CtrlFlag.Counter.IsFaintnessCount(); } catch { return false; }
    }

    private static string Probe(HScene scene)
    {
        var parts = new List<string>();
        try
        {
            var c = scene.CtrlFlag.Counter;
            parts.Add($"faint {c.IsFaintnessCount()} (faintness {c.Faintness}/{FlagControl.CountData.GO_TO_FAINTNESS_COUNT}, orgasm {c.Orgasm}, " +
                      $"second {c.OrgasmSecond}, total {c.OrgasmTotal}, same {c.SameOrgasm}, F2 {c.OrgasmF2}, female total {c.GetFemaleOrgasmTotal()})");
        }
        catch (Exception e) { parts.Add($"count ? ({e.Message})"); }
        parts.Add($"collapsed {StateText(scene)}");
        foreach (var a in Watchers.ActiveActors(scene))
            if (a.Sex == 1)
                try { parts.Add($"{Report.Name(a.Human?.data)} weak {a.IsWeakness()}"); } catch { }
        try { parts.Add($"position {scene.NowAnimationInfo?.NameAnimation}"); } catch { }
        return string.Join(", ", parts);
    }

    // Every half second while the scene runs.
    public static void Tick(HScene scene, float now)
    {
        _noSceneSince = -1f;
        if (_scenePtr != scene.Pointer) { _scenePtr = scene.Pointer; _sceneSince = now; _faintDebt = 0; _faintState = null; _restartAt = -1f; }
        if (_restartAt >= 0f) Restart(scene, now);
        if (now < _nextTick) return;
        _nextTick = now + 0.5f;
        var probe = Probe(scene);
        if (probe != _lastProbe) { _lastProbe = probe; Plugin.Debug($"Tiredness: {probe}."); }
        if (now - _sceneSince < 1.5f) return;
        if (_plan != null) { Set(scene, _plan.Value, "the girls of the new scene"); _plan = null; return; }
        // Only the moment the scene turns tired marks its girls: a scene the mod made fresh may still read tired
        // until the game's own count drops, and the girl who just came in must not be marked for it.
        var faint = SceneFaint(scene);
        var rising = faint && !_wasFaint;
        _wasFaint = faint;
        if (!rising) return;
        foreach (var a in Watchers.ActiveActors(scene))
        {
            if (a.Sex != 1 || a.ActorData == null) continue;
            var key = Swaps.KeyOf(a);
            if (_tired.Add(key)) Plugin.Log.LogInfo($"{Report.Name(Swaps.NpcByKey(key)?.BaseData?.HumanData)} is worn out; she stays tired for the rest of H.");
        }
    }

    // The scene's state. The game's tired check (CountData.IsFaintnessCount) reads its orgasm counts, not the
    // faintness count, so the counts that may feed it are lowered (fresh) or raised (tired) one at a time until the
    // check agrees. The scene's total, used for its results, is never touched.
    private static readonly string[] Counts = { "SameOrgasm", "Orgasm", "OrgasmSecond", "Faintness" };

    internal static void Set(HScene scene, bool tired, string why)
    {
        try
        {
            var c = scene.CtrlFlag.Counter;
            var before = Probe(scene);
            var changed = new List<string>();
            var goal = FlagControl.CountData.GO_TO_FAINTNESS_COUNT;
            foreach (var name in Counts)
            {
                // The faintness count (how often she collapsed) is what picks the tired positions, so it always
                // follows; the orgasm counts only until the tired check agrees.
                if (name != "Faintness" && c.IsFaintnessCount() == tired) continue;
                var now = Get(c, name);
                var want = name == "Faintness" ? (tired ? Mathf.Max(now, 1) : 0) : tired ? Mathf.Max(now, goal) : 0;
                if (now == want) continue;
                Put(c, name, want);
                if (name == "Faintness") _faintDebt += now - want;
                changed.Add($"{name} {now} -> {want}");
            }
            try { if (SetState(scene, tired)) changed.Add($"collapsed state {(tired ? "on" : "off")}"); }
            catch (Exception e) { changed.Add($"collapsed state ? ({e.Message})"); }
            _wasFaint = SceneFaint(scene);
            if (changed.Count > 0) { _restartAt = Time.realtimeSinceStartup + 0.5f; _restartStep = 0; }
            Plugin.Debug($"Tiredness: the scene should be {(tired ? "tired" : "fresh")} for {why}; changed {(changed.Count > 0 ? string.Join(", ", changed) : "nothing")}; " +
                         $"now {(c.IsFaintnessCount() == tired ? "it is" : "it is NOT")} (before: {before}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Tiredness: setting the scene failed: {e.Message}"); }
    }

    private static int Get(FlagControl.CountData c, string name) => name switch
    {
        "SameOrgasm" => c.SameOrgasm, "Orgasm" => c.Orgasm, "OrgasmSecond" => c.OrgasmSecond, _ => c.Faintness
    };

    private static void Put(FlagControl.CountData c, string name, int v)
    {
        switch (name)
        {
            case "SameOrgasm": c.SameOrgasm = v; break;
            case "Orgasm": c.Orgasm = v; break;
            case "OrgasmSecond": c.OrgasmSecond = v; break;
            default: c.Faintness = v; break;
        }
    }

    // A girl swapped into the scene: a fresh girl makes it fresh; a tired one makes it tired only if every girl in it
    // is tired now.
    // Climaxes so far, per girl, in a scene with one girl: the scene keeps one count, so a girl swapped in would
    // otherwise start from where the girl before her stopped (and wear out after one climax).
    private static readonly Dictionary<IntPtr, (int same, int orgasm, int second)> _counts = new();

    public static void AfterSwap(HScene scene, IntPtr inKey, IntPtr outKey)
    {
        try
        {
            var girls = Watchers.ActiveActors(scene).Count(a => a.Sex == 1 && a.ActorData != null);
            if (girls == 1)
            {
                var c = scene.CtrlFlag.Counter;
                if (outKey != IntPtr.Zero) _counts[outKey] = (c.SameOrgasm, c.Orgasm, c.OrgasmSecond);
                _counts.TryGetValue(inKey, out var mine);
                Plugin.Debug($"Tiredness: climaxes {c.Orgasm} (same {c.SameOrgasm}, second {c.OrgasmSecond}) were the girl going out's; " +
                             $"the girl coming in has had {mine.orgasm} (same {mine.same}, second {mine.second}).");
                c.SameOrgasm = mine.same; c.Orgasm = mine.orgasm; c.OrgasmSecond = mine.second;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Tiredness: per-girl climaxes: {e.Message}"); }
        if (!Is(inKey))
        {
            if (SceneFaint(scene) || Collapsed(scene)) Set(scene, false, "the fresh girl who came in");
            return;
        }
        var all = Watchers.ActiveActors(scene).Where(a => a.Sex == 1 && a.ActorData != null).All(a => Is(Swaps.KeyOf(a)) || Swaps.KeyOf(a) == inKey);
        if (all && !SceneFaint(scene)) Set(scene, true, "the tired girl who came in");
    }

    // As a scene ends: the watchers who could take a place in the next one.
    public static void RememberPool()
    {
        var pool = Watchers.Present.Where(w => w.Npc != null && Invite.Eligible(w.Data)).OrderBy(w => w.StartDistance).Select(w => (w.Npc, w.Key)).ToList();
        if (pool.Count == 0) return;
        _pool.Clear();
        _pool.AddRange(pool);
    }

    // A new scene with its girls (the 3P button's restart, a 5P): all tired with the main girl, else all fresh, by
    // swapping girls of the other state for watchers; if neither can be done, the scene follows the main girl.
    public static void AtSceneStart(HScene.InputParameter p)
    {
        try
        {
            var r = p.Receivers;
            if (r == null || r.Length < 2 || _tired.Count == 0) { _pool.Clear(); return; }
            var girls = new List<(IntPtr from, IntPtr shown, bool pick)>();
            for (var i = 0; i < r.Length; i++)
            {
                var d = r[i]?.ActorData;
                if (d == null || Residents.Girl(d) == null) continue;
                var shown = Swaps.CarriedTo(d.Pointer);
                var pick = i > 0 && Swaps.PendingJoin != IntPtr.Zero && Invite.WasFirst(d.Pointer);
                if (pick) shown = Swaps.PendingJoin;
                girls.Add((d.Pointer, shown, pick));
            }
            if (girls.Count < 2) return;
            var mainTired = Is(girls[0].shown);
            var inUse = new HashSet<IntPtr>(girls.Select(g => g.shown).Concat(girls.Select(g => g.from)).Concat(Swaps.CarryKeys()));
            foreach (var want in new[] { mainTired, !mainTired })
            {
                var odd = girls.Where(g => Is(g.shown) != want).ToList();
                if (odd.Count == 0) { Plan(want, "everyone already is"); return; }
                if (odd.Any(g => g.pick)) continue;
                var spare = _pool.Where(x => Is(x.key) == want && !inUse.Contains(x.key)).ToList();
                if (spare.Count < odd.Count) continue;
                for (var i = 0; i < odd.Count; i++)
                {
                    Swaps.AddCarry(odd[i].from, spare[i].key);
                    Plugin.Log.LogInfo($"Tiredness: {Report.Name(spare[i].npc.BaseData.HumanData)} ({(want ? "tired" : "fresh")}) takes " +
                                       $"{Report.Name(Swaps.NpcByKey(odd[i].shown)?.BaseData?.HumanData)}'s place in the new scene.");
                }
                Plan(want, $"{odd.Count} girl(s) swapped");
                return;
            }
            Plan(mainTired, "not enough watchers to match; following the main girl");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Tiredness at the scene start: {e.Message}"); }
        finally { _pool.Clear(); }
    }

    private static void Plan(bool tired, string why)
    {
        _plan = tired;
        Plugin.Log.LogInfo($"Tiredness: the new scene starts {(tired ? "tired" : "fresh")} ({why}).");
    }

    public static void NoScene()
    {
        if (_tired.Count == 0 && _plan == null) return;
        if (_noSceneSince < 0f) { _noSceneSince = Time.realtimeSinceStartup; return; }
        if (Time.realtimeSinceStartup - _noSceneSince < 8f) return;
        _tired.Clear();
        _counts.Clear();
        _plan = null;
        _noSceneSince = -1f;
        Plugin.Debug("Tiredness: H is over; everyone is fresh again.");
    }
}
