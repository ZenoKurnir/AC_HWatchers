using H;
using UnityEngine;

namespace AC_HWatchers;

// The game never plays the H animators as they are: each position's animator (tok_f_03, les_f1_00, ...) is mixed
// onto a base animator (tok_f_base_00, les_f1_base_00, ...) whose states blend the S, M and L clips by the girl's
// height ("height", 0 to 1 = her height slider). Playing one size's clips straight left girls between sizes floating
// or sunk into the floor, so watchers get the same mix and their own height.
internal static partial class Watchers
{
    private static readonly Dictionary<string, RuntimeAnimatorController> _mixed = new();

    private static string BaseOf(string set)
    {
        if (set.StartsWith("tok_f_")) return "tok_f_base_00";
        if (set.StartsWith("les_f1_") || set.StartsWith("les_f2_")) return set.Substring(0, 7) + (set.EndsWith("_01") ? "base_01" : "base_00");
        return null;
    }

    // The caress base isn't named in the position list; the likely names are tried once each.
    private static readonly Dictionary<string, string> _aibBase = new();

    private static RuntimeAnimatorController AibBase(string bundle, string set)
    {
        var side = set.StartsWith("aib_m_") ? "m" : "f";
        if (_aibBase.TryGetValue(side, out var known)) return known == null ? null : LoadController(bundle, known);
        foreach (var name in new[] { $"aib_{side}_base_00", $"aib_{side}_base", $"aib_{side}_base1", $"aib_{side}_base_01" })
        {
            var c = LoadController(bundle, name, true);
            if (c == null) continue;
            _aibBase[side] = name;
            Plugin.Debug($"Caress base animator: {name}.");
            return c;
        }
        _aibBase[side] = null;
        Plugin.Log.LogInfo($"No caress base animator found for aib_{side}; the caress animators play as they are.");
        return null;
    }

    // The animator for a set, mixed onto its base when that works; mixed says which state names to use.
    internal static RuntimeAnimatorController Controller(string bundle, string set, out bool mixed)
    {
        mixed = false;
        var over = LoadController(bundle, set);
        if (over == null) return null;
        if (_mixed.TryGetValue(set, out var m) && m != null)
        {
            // Kept from an earlier scene: usable only while its base animator is still loaded.
            var aoc = m.TryCast<AnimatorOverrideController>();
            if (aoc == null || aoc.runtimeAnimatorController != null) { mixed = true; return m; }
            _mixed.Remove(set);
        }
        var baseName = BaseOf(set);
        RuntimeAnimatorController b;
        if (set.StartsWith("aib_"))
        {
            b = AibBase(bundle, set);
            if (b == null) return over;
            baseName = b.name;
        }
        else
        {
            if (baseName == null) return over;
            b = LoadController(set.StartsWith("les") ? LesBundle : TokBundle, baseName);
        }
        if (b == null) return over;
        // HActor.MixRuntimeControler hands back nothing outside the scene's own actors, so the mix is built here:
        // the base animator with each of its clips replaced by the position's clip of the same name.
        try
        {
            var byName = new Dictionary<string, AnimationClip>();
            foreach (var c in over.animationClips) if (c != null) byName[c.name] = c;
            var aoc = new AnimatorOverrideController(b);
            var n = 0;
            foreach (var c in b.animationClips)
                if (c != null && byName.TryGetValue(c.name, out var rep)) { aoc[c] = rep; n++; }
            if (n > 0)
            {
                aoc.name = set + " on " + baseName;
                _mixed[set] = aoc; mixed = true;
                Plugin.Debug($"Mixed {set} onto {baseName} ({n} clips).");
                return aoc;
            }
            Plugin.Log.LogWarning($"{set} shares no clip names with {baseName}; playing it as it is.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Could not mix {set} onto {baseName}: {e.Message}"); }
        return over;
    }

    internal static float HeightOf(Watcher w)
    {
        if (w.BlendHeight != null) return w.BlendHeight.Value;
        try { return Mathf.Clamp01(w.Human.fileBody.shapeValueBody[0]); } catch { return 0.5f; }
    }

    // The base animators' parameters: her height (and her partner's), bust, and the loop speed multiplier.
    internal static void SetBlend(Watcher w, Watcher partner = null)
    {
        var anim = w.Human?.body?.animBody;
        if (anim == null) return;
        try
        {
            float bust = 0.5f;
            try { bust = Mathf.Clamp01(w.Human.fileBody.shapeValueBody[3]); } catch { }
            foreach (var p in anim.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float) continue;
                switch (p.name.ToLowerInvariant())
                {
                    case "height": anim.SetFloat(p.nameHash, HeightOf(w)); break;
                    case "height1": anim.SetFloat(p.nameHash, partner != null ? HeightOf(partner) : HeightOf(w)); break;
                    case "breast": anim.SetFloat(p.nameHash, bust); break;
                    case "speed" when w.Mixed: anim.SetFloat(p.nameHash, 1f); break;
                }
            }
        }
        catch { }
    }

    private static readonly HashSet<string> _paramsLogged = new();

    internal static void LogParams(Animator anim, string what)
    {
        try
        {
            var name = anim.runtimeAnimatorController?.name ?? "?";
            if (!_paramsLogged.Add(name)) return;
            var ps = new List<string>();
            foreach (var p in anim.parameters) ps.Add($"{p.name} ({p.type}) = {(p.type == AnimatorControllerParameterType.Float ? anim.GetFloat(p.nameHash).ToString("0.00") : "-")}");
            Plugin.Debug($"The {what} {name} has parameters: {string.Join(", ", ps)}");
        }
        catch { }
    }

    // "Base Layer.WLoop" on a mixed animator, "Base Layer.<size>_WLoop" on a plain one.
    internal static int StateHash(Watcher w, Animator anim, string state, string size)
    {
        if (w.Mixed)
        {
            var h = Animator.StringToHash($"Base Layer.{state}");
            if (anim.HasState(0, h)) return h;
            // The base has one WLoop/SLoop whose "motion" (0 or 1) picks the position's WLoop1 or WLoop2.
            var last = state[^1];
            if (last is '1' or '2')
            {
                h = Animator.StringToHash($"Base Layer.{state[..^1]}");
                if (anim.HasState(0, h))
                {
                    try { anim.SetFloat("motion", last == '2' ? 1f : 0f); } catch { }
                    return h;
                }
            }
        }
        var hash = Animator.StringToHash($"Base Layer.{size}_{state}");
        if (!anim.HasState(0, hash)) hash = Animator.StringToHash($"Base Layer.{(size.Length == 2 ? "MM" : "M")}_{state}");
        return anim.HasState(0, hash) ? hash : 0;
    }
}
