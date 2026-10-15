using H;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace AC_HWatchers;

// Two watchers who both like girls make love with each other once both are worked up, using the game's own
// Les animations (h/animator/female/000_01/les.unity3d: les_f1_XX and les_f2_XX share one origin). The H scene
// can't run a second Les scene, so the states are played directly.
internal static partial class Watchers
{
    internal static readonly (string set, string name)[] LoveSets = { ("00", "Scissoring"), ("01", "Standing") };
    // Caresses (h/animator/{male,female}/000_01/aib.unity3d): the girl whose menu it is plays the man's part
    // (aib_m_XX) on the other (aib_f_XX). The male clips drive the same bones, so they fit a girl. Only the ones that
    // need no furniture, toys or wall.
    internal static readonly (string set, string name)[] CaressSets =
    {
        ("aib00", "Kiss"), ("aib01", "Lying kiss"), ("aib12", "Standing kiss"), ("aib02", "Breasts"), ("aib17", "From behind"),
        ("aib25", "Standing caress"), ("aib03", "Fingers"), ("aib38", "Hug and fingers"), ("aib04", "Lick"), ("aib13", "Standing lick"),
        ("aib31", "Lick on all fours")
    };
    internal const string AibBundleF = "h/animator/female/000_01/aib.unity3d";
    internal const string AibBundleM = "h/animator/male/000_01/aib.unity3d";
    private static bool IsCaress(string set) => set != null && set.StartsWith("aib");
    private static readonly string[] LoveStates = { "WLoop1", "WLoop2", "SLoop1", "SLoop2", "OLoop", "Orgasm", "Orgasm_A" };
    private static readonly float[] LoveSpeeds = { 1f, 1.1f, 1.2f, 1.35f, 1.35f, 1f, 1f };
    private const int LoveOrgasm = 5;

    public static bool CanLove(Watcher a, Watcher b) =>
        Plugin.GirlPairs.Value && a != null && b != null && a != b && a.Human != null && b.Human != null &&
        !a.Locked && !b.Locked && !a.Tired && !b.Tired && a.LikesGirls && b.LikesGirls && a.Lover == null && b.Lover == null;

    // Girls she could make love with right now.
    public static List<Watcher> LoveChoices(Watcher w) =>
        !w.LikesGirls || w.Locked || w.Tired ? new List<Watcher>() : Present.Where(o => o != w && !o.Locked && !o.Tired && o.LikesGirls).ToList();

    public static void SwitchLove(Watcher w, Watcher other, string set)
    {
        if (w.Lover == other) { if (w.LoveSet == set) return; EndLove(w); }
        if (w.Lover != null) EndLove(w);
        if (other.Lover != null) EndLove(other);
        StartLove(w, other, set);
    }

    private static bool TryPairUp(Watcher w)
    {
        if (Random.value > 0.5f) return false;
        var other = Present.Where(o => CanLove(w, o) && o.Excitement >= 0.55f).OrderBy(_ => Random.value).FirstOrDefault();
        if (other == null) return false;
        StartLove(w, other, null);
        return w.Lover != null;
    }

    public static void StartLove(Watcher a, Watcher b, string set)
    {
        if (_scene == null) return;
        if (a.Lover != null || b.Lover != null || a.Locked || b.Locked) return;
        set ??= Random.value < 0.5f ? CaressSets[Random.Range(0, CaressSets.Length)].set : Random.value < 0.55f ? "00" : "01";
        var caress = IsCaress(set);
        // The taller girl always plays the man's part, whoever's menu it came from.
        if (caress && Taller(b, a)) (a, b) = (b, a);
        var ca = caress ? Controller(AibBundleM, $"aib_m_{set.Substring(3)}", out var ma) : Controller(LesBundle, $"les_f1_{set}", out ma);
        var cb = caress ? Controller(AibBundleF, $"aib_f_{set.Substring(3)}", out var mb) : Controller(LesBundle, $"les_f2_{set}", out mb);
        if (ca == null || cb == null) return;
        if (caress) LogClips(ca, cb);
        foreach (var w in new[] { a, b })
        {
            if (w.TokSet != null) { w.TokSet = null; RestoreClothes(w); }
            w.ReactAt = -1f;
            w.ReactionUntil = 0f;
            w.Undressed = new();
            w.Pose = null;
        }
        a.Lover = b; b.Lover = a;
        a.LoverLeads = true; b.LoverLeads = false;
        a.LoveSet = b.LoveSet = set;
        a.Human.body.animBody.runtimeAnimatorController = ca;
        b.Human.body.animBody.runtimeAnimatorController = cb;
        a.Mixed = ma; b.Mixed = mb;
        if (caress) EvenHeights(a, b);
        SetBlend(a, b);
        SetBlend(b, a);
        if (!PlaceCouple(_scene, a, set))
        {
            a.Lover = b.Lover = null;
            a.LoverLeads = false;
            BackToWatching(a, true);
            BackToWatching(b, true);
            return;
        }
        // Their necks follow the animation (a kiss or a lick places the head itself); only the eyes seek each other.
        Gaze(a, Head(b), true);
        Gaze(b, Head(a), true);
        LoveStage(a, 0);
        Plugin.Debug($"{a.Name} and {b.Name} make love ({(caress ? $"{a.Name} caresses {b.Name}, aib {set.Substring(3)}" : $"les {set}")}, sizes {Size(a)}/{Size(b)}).");
    }

    // 00 and 02 have one size letter (the leading girl's), 01 has two (the first girl's, then the second's).
    private static string LoveState(Watcher a, int stage)
    {
        var size = a.LoveSet == "01" ? Size(a) + Size(a.Lover) : Size(a);
        return $"Base Layer.{size}_{LoveStates[stage]}";
    }

    private static void LoveStage(Watcher a, int stage)
    {
        var b = a.Lover;
        a.LoveStage = stage;
        var size = a.LoveSet == "01" ? Size(a) + Size(a.Lover) : Size(a);
        foreach (var w in new[] { a, b })
        {
            var anim = w.Human.body?.animBody;
            if (anim == null) continue;
            var hash = StateHash(w, anim, LoveStates[stage], size);
            // Caresses may start from Idle and name their loops differently; try the usual alternatives.
            if (hash == 0 && IsCaress(a.LoveSet))
                foreach (var alt in CaressAlternatives(stage))
                    if ((hash = StateHash(w, anim, alt, size)) != 0) break;
            if (hash == 0) { Plugin.Log.LogWarning($"{w.Name}: no {LoveStates[stage]} state in {a.LoveSet} ({anim.runtimeAnimatorController?.name})."); continue; }
            anim.speed = LoveSpeeds[stage];
            if (stage == 0) anim.Play(hash, 0, 0f);
            else anim.CrossFadeInFixedTime(hash, 0.35f, 0, 0f);
            UndressFor(w, stage >= 2 ? 1 : 0);
            switch (stage)
            {
                case < 2: Faces.Set(w, Mood.Aroused); break;
                case < LoveOrgasm: Faces.Set(w, Mood.Hot); break;
                case LoveOrgasm: Faces.Set(w, Mood.Climax); break;
                default: Faces.Set(w, Mood.Afterglow); break;
            }
        }
        var now = Time.realtimeSinceStartup;
        if (KissSets.Contains(a.LoveSet)) { a.HeadCheckAt = now + 0.5f; a.MouthSum = Vector3.zero; a.MouthCount = 0; a.MouthPasses = 0; }
        a.LoveStageUntil = now + stage switch
        {
            0 => Random.Range(10f, 16f),
            1 => Random.Range(8f, 12f),
            2 => Random.Range(8f, 12f),
            3 => Random.Range(6f, 10f),
            4 => Random.Range(4f, 6f),
            5 => -1f,
            _ => Random.Range(5f, 8f)
        };
    }

    private static IEnumerable<string> CaressAlternatives(int stage) => stage switch
    {
        0 or 1 => new[] { "WLoop", "Loop", "Idle" },
        2 or 3 => new[] { "SLoop", "WLoop", "Loop" },
        4 => new[] { "OLoop", "SLoop" },
        5 => new[] { "Orgasm", "OrgasmF", "Orgasm_IN" },
        _ => new[] { "Orgasm_A", "Orgasm_B", "Idle" }
    };

    private static readonly HashSet<string> _clipsLogged = new();

    // Detailed log: the clips a caress animator has.
    private static void LogClips(params RuntimeAnimatorController[] cs)
    {
        foreach (var c in cs)
            try
            {
                if (c == null || !_clipsLogged.Add(c.name)) continue;
                var names = c.animationClips.Where(x => x != null).Select(x => x.name).Distinct().ToList();
                Plugin.Debug($"Caress animator {c.name}: {names.Count} clips: {string.Join(", ", names.Take(40))}");
            }
            catch { }
    }

    private static void LoveTick(Watcher a, float now)
    {
        var b = a.Lover;
        if (b?.Human == null || a.Human.body?.animBody?.runtimeAnimatorController == null ||
            b.Human.body?.animBody?.runtimeAnimatorController == null)
        {
            EndLove(a);
            return;
        }
        b.ReactAt = -1f;
        if (a.HeadCheckAt > 0f && now >= a.HeadCheckAt) MindHeads(a, now);
        if (a.ReactAt >= 0 && now >= a.ReactAt)
        {
            a.ReactAt = -1f;
            if (a.LoveStage is >= 2 and < LoveOrgasm) { LoveStage(a, LoveOrgasm); return; }
        }
        bool done;
        if (a.LoveStage == LoveOrgasm)
        {
            var anim = a.Human.body.animBody;
            var info = anim.GetCurrentAnimatorStateInfo(0);
            done = !anim.IsInTransition(0) && info.normalizedTime >= 0.98f;
        }
        else done = now >= a.LoveStageUntil;
        if (!done) return;
        if (a.LoveStage + 1 < LoveStates.Length) LoveStage(a, a.LoveStage + 1);
        else EndLove(a);
    }

    public static void EndLove(Watcher w)
    {
        var other = w.Lover;
        if (other == null) return;
        w.Lover = null; other.Lover = null;
        w.LoverLeads = false; other.LoverLeads = false;
        w.Excitement = Mathf.Min(w.Excitement, 0.3f);
        other.Excitement = Mathf.Min(other.Excitement, 0.3f);
        BackToWatching(w, true);
        BackToWatching(other, true);
        Plugin.Debug($"{w.Name} and {other.Name} go back to watching.");
    }

    // Both girls stand on one spot (their animations hold them in place around it), between their two places
    // in the ring, where there is room. Against a wall, the spot is by the nearest wall instead.
    private static bool PlaceCouple(HScene scene, Watcher a, string set = null)
    {
        var b = a.Lover;
        set ??= a.LoveSet;
        a.Pinned = b.Pinned = false;
        var center = SceneCenter(scene);
        AimRing(scene, center);
        var k = Scale(scene);
        center.y = SceneFloor(scene, center, k);
        if (!_anchored) { _anchor = center; _anchored = true; }
        var floor = RoomFloor(center, k);
        if (floor < center.y) center.y = floor;
        var bodies = ActorBones(scene);
        var baseAngle = (SlotAngles[a.Slot % SlotAngles.Length] + SlotAngles[b.Slot % SlotAngles.Length]) * 0.5f;
        Vector3? first = null, chosen = null, cramped = null;
        foreach (var dist in new[] { 1.2f, 1.5f, 1.85f, 2.3f })
        {
            foreach (var off in AngleTries)
            {
                var dir = Quaternion.Euler(0, baseAngle + off, 0) * _ringDir;
                if (!Usable(center, center + dir * Plugin.Distance.Value * dist * k, k, out var pos, out var wet) || (wet && set == "00")) continue;
                first ??= pos;
                if (!IsClear(a, pos, bodies, k) || BlocksView(pos, k)) continue;
                var room = Roomy(pos, center.y, k);
                if (room < 2) { if (room == 1) cramped ??= pos; continue; }
                if (set == "00" && !HasRoom(pos, k)) { cramped ??= pos; continue; }
                chosen = pos;
                break;
            }
            if (chosen != null) break;
        }
        if (chosen == null && first == null)
        {
            Plugin.Debug($"No spot on the couple's level for {a.Name} and {b.Name}.");
            return false;
        }
        var spot = chosen ?? cramped ?? first.Value;
        var face = center - spot; face.y = 0;
        var rot = Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face : Vector3.forward);
        a.Human.transform.SetPositionAndRotation(spot, rot);
        b.Human.transform.SetPositionAndRotation(spot, rot);
        a.Wet = b.Wet = false;
        return true;
    }

    private static readonly HashSet<string> KissSets = new() { "aib00", "aib01", "aib12" };

    // Kisses: the man's clips put her mouth where a man's would be. The two mouths are watched for a moment, then
    // the leading girl steps toward or away from the other until they meet, and her height is nudged (a few
    // percent, feet staying on the floor) so they meet level.
    private static Vector3 Mouth(Watcher w)
    {
        var l = FindBone(w.Human, "cf_J_Mouth_L");
        var r = FindBone(w.Human, "cf_J_Mouth_R");
        if (l != null && r != null) return (l.position + r.position) * 0.5f;
        var lo = FindBone(w.Human, "cf_J_MouthLow");
        return lo != null ? lo.position : Head(w).position;
    }

    private static void MindHeads(Watcher a, float now)
    {
        var b = a.Lover;
        if (b?.Human == null || !KissSets.Contains(a.LoveSet)) { a.HeadCheckAt = 0f; return; }
        a.MouthSum += Mouth(b) - Mouth(a);
        a.MouthCount++;
        if (now < a.HeadCheckAt + 1.2f) return;
        a.HeadCheckAt = 0f;
        var d = a.MouthSum / a.MouthCount;
        a.MouthSum = Vector3.zero; a.MouthCount = 0;
        var k = _scene != null ? Scale(_scene) : 1f;
        var t = a.Human.transform;
        const float touch = 0.025f;
        a.MouthPasses++;
        if (a.LoveSet == "aib01")
        {
            // Lying: she is over the other girl, so she moves the whole way, up and down included.
            var gap = d.magnitude;
            if (gap < 0.001f) return;
            var move = d / gap * (gap - touch * k);
            move = new Vector3(Mathf.Clamp(move.x, -0.1f * k, 0.1f * k), Mathf.Clamp(move.y, -0.06f * k, 0.06f * k), Mathf.Clamp(move.z, -0.1f * k, 0.1f * k));
            t.position += move;
            Plugin.Debug($"Kiss (lying): mouths {gap:0.000} m apart ({d.y:+0.000;-0.000} up/down); {a.Name} moves {move.magnitude:0.000} m.");
        }
        else
        {
            var flat = d; flat.y = 0;
            var across = flat.magnitude;
            var step = 0f;
            if (across > 0.001f)
            {
                step = Mathf.Clamp(across - touch * k, -0.1f * k, 0.12f * k);
                t.position += flat / across * step;
            }
            // Level: she grows and the other girl shrinks by the same share of the gap, each scaled from her feet.
            float la = 1f, lb = 1f;
            float upA = Mouth(a).y - t.position.y, upB = Mouth(b).y - b.Human.transform.position.y;
            if (upA > 0.3f * k && upB > 0.3f * k && Mathf.Abs(d.y) > 0.005f * k)
            {
                la = Grow(a, d.y * 0.5f / upA);
                lb = Grow(b, -d.y * 0.5f / upB);
            }
            Plugin.Debug($"Kiss: mouths {across:0.000} m apart across, {d.y:+0.000;-0.000} m up/down; {a.Name} moves {step:+0.000;-0.000} m, heights x{la:0.000}/x{lb:0.000}.");
            if (Mathf.Abs(d.y) <= 0.012f * k) return;
        }
        // Once more, to check what's left.
        if (a.MouthPasses < 3) a.HeadCheckAt = now + 0.2f;
    }

    // Scales her by 1 + share from her feet, staying within 90-110% of her own size; returns the factor applied.
    private static float Grow(Watcher w, float share)
    {
        var t = w.Human.transform;
        w.BaseScale ??= t.localScale;
        var base0 = w.BaseScale.Value.y;
        var now0 = t.localScale.y;
        var want = Mathf.Clamp(now0 * (1f + share), base0 * 0.9f, base0 * 1.1f);
        var f = want / now0;
        t.localScale *= f;
        return f;
    }

    private static float RoomFloor(Vector3 center, float k)
    {
        try
        {
            // The floor the player walks on, when the couple is above it (a bench, a bed, a raised slab).
            if (NavMesh.SamplePosition(center, out var walk, 1.5f * k, PlayerAreas()) && center.y - walk.position.y > 0.08f * k)
                return walk.position.y;
            if (NavMesh.SamplePosition(center, out var hit, 1.5f * k, NavMesh.AllAreas) && center.y - hit.position.y > 0.3f * k)
                return hit.position.y;
            var lowest = center.y;
            var hits = Physics.RaycastAll(center + Vector3.up * 0.2f * k, Vector3.down, 1.1f * k, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || IsCharacter(h.collider.transform) || IsWater(h.collider) || NotFloor(h.collider) || h.normal.y < 0.6f) continue;
                if (h.point.y < lowest) lowest = h.point.y;
            }
            if (center.y - lowest > 0.25f * k) return lowest;
        }
        catch { }
        return center.y;
    }

    private static bool HasRoom(Vector3 pos, float k)
    {
        try
        {
            var cols = Physics.OverlapBox(pos + Vector3.up * 0.45f * k, new Vector3(0.8f, 0.3f, 0.8f) * k, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
                if (c != null && !IsCharacter(c.transform) && !IsSound(c)) return false;
            for (var a = 0; a < 360; a += 45)
            {
                var at = pos + Quaternion.Euler(0, a, 0) * Vector3.forward * 0.75f * k;
                if (!Ground(at, k, out var g) || Mathf.Abs(g.y - pos.y) > 0.15f * k) return false;
            }
        }
        catch { }
        return true;
    }

    private static bool BlocksView(Vector3 pos, float k)
    {
        try
        {
            var cam = _scene != null ? Camera(_scene) : null;
            if (cam == null) return false;
            var c = cam.transform.position;
            var t = SceneCenter(_scene) + Vector3.up * 0.6f * k;
            var p = pos + Vector3.up * 0.4f * k;
            var flat = p - c; flat.y = 0;
            if (flat.magnitude < 1.3f * k) return true;
            var d = t - c;
            var len2 = d.sqrMagnitude;
            if (len2 < 1e-4f) return false;
            var u = Vector3.Dot(p - c, d) / len2;
            if (u <= 0f || u >= 1f) return false;
            return (c + d * u - p).magnitude < 0.9f * k;
        }
        catch { return false; }
    }

    // Worn out from H: she lies on the floor (the faint idle of a caress where the girl lies on her back) and
    // does nothing else.
    private static RuntimeAnimatorController _lieCtrl;
    private static bool _lieMixed;

    internal static void LieDown(Watcher w)
    {
        if (w.TokSet != null) { w.TokSet = null; RestoreClothes(w); }
        w.Pose = "tired";
        w.ReactAt = -1f;
        var anim = w.Human?.body?.animBody;
        if (anim == null) return;
        // A controller from an earlier scene may be gone (its bundle unloaded): Unity's null check, not ??=.
        if (_lieCtrl == null) _lieCtrl = Controller(AibBundleF, "aib_f_04", out _lieMixed);
        if (_lieCtrl == null) return;
        anim.runtimeAnimatorController = _lieCtrl;
        w.Mixed = _lieMixed;
        SetBlend(w);
        anim.speed = 1f;
        var hash = 0;
        foreach (var st in new[] { "D_Idle", "D_WLoop", "Idle" })
            if ((hash = StateHash(w, anim, st, Size(w))) != 0) { Plugin.Debug($"{w.Name} lies down worn out ({st})."); break; }
        if (hash != 0) anim.Play(hash, 0, Random.value);
        Faces.Set(w, Mood.Tired);
        if (_scene != null && !w.Pinned) Place(_scene, w);
    }

    private static void TiredTick(Watcher w, float now)
    {
        w.ReactAt = -1f;
        var anim = w.Human.body?.animBody;
        if (w.Pose != "tired" || anim == null || anim.runtimeAnimatorController == null) LieDown(w);
    }

    internal static Transform Head(Watcher w) => w.Human.face?.objHeadBone?.transform ?? w.Human.transform;
}
