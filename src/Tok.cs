using Character;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AC_HWatchers;

// Aicomi has its own masturbation animations (h/animator/female/000_01/tok.unity3d), with plain states per body
// size: <S|M|L>_<Idle|WLoop|MLoop|SLoop|OLoop|Orgasm|Orgasm_A>. A watcher plays them straight on her animator.
internal static partial class Watchers
{
    // The ones that need nothing but the floor. tok_f_32 (showing off) is in the 020_01 bundle.
    internal static readonly (string set, string name)[] TokSets =
    {
        ("tok_f_03", "standing"), ("tok_f_01", "crouching"), ("tok_f_00", "lying"), ("tok_f_32", "showing off")
    };

    private static readonly string[] TokStates = { "WLoop", "MLoop", "SLoop", "OLoop", "Orgasm", "Orgasm_A" };
    private const int TokOrgasm = 4;

    internal static string TokBundleOf(string set) => set == "tok_f_32" ? TokBundle2 : TokBundle;

    public static bool CanTouch(Watcher w) =>
        Plugin.Masturbate.Value && !w.Locked && w.Human != null && w.Lover == null;

    public static void StartTouching(Watcher w, string set)
    {
        if (!CanTouch(w) || w.TokSet != null) return;
        if (set == null)
        {
            var r = Random.value;
            set = r < 0.4f ? "tok_f_03" : r < 0.65f ? "tok_f_01" : r < 0.85f ? "tok_f_32" : "tok_f_00";
        }
        if (w.Wet) set = "tok_f_03";
        var rac = Controller(TokBundleOf(set), set, out var mixed);
        var anim = w.Human.body?.animBody;
        if (rac == null || anim == null) return;
        anim.runtimeAnimatorController = rac;
        w.Mixed = mixed;
        SetBlend(w);
        w.TokSet = set;
        w.Undressed = new();
        w.Pose = null;
        TokStage(w, 0);
        Plugin.Debug($"{w.Name} started touching herself ({set}).");
    }

    public static void StopTouching(Watcher w)
    {
        if (w.TokSet == null) return;
        w.TokSet = null;
        RestoreClothes(w);
        w.Excitement = Mathf.Min(w.Excitement, 0.35f);
        BackToWatching(w, false);
    }

    private static void TokStage(Watcher w, int stage)
    {
        var anim = w.Human.body?.animBody;
        if (anim == null) { StopTouching(w); return; }
        w.TokStage = stage;
        var hash = StateHash(w, anim, TokStates[stage], Size(w));
        if (hash == 0) { Plugin.Log.LogWarning($"{w.Name}: {w.TokSet} has no {TokStates[stage]} state."); StopTouching(w); return; }
        anim.speed = 1f;
        if (stage == 0) anim.Play(hash, 0, 0f);
        else anim.CrossFadeInFixedTime(hash, 0.3f, 0, 0f);
        var now = Time.realtimeSinceStartup;
        w.TokStageUntil = now + stage switch
        {
            0 => Random.Range(12f, 20f),
            1 => Random.Range(10f, 16f),
            2 => Random.Range(8f, 14f),
            3 => Random.Range(5f, 8f),
            4 => -1f,
            _ => Random.Range(4f, 7f)
        };
        UndressFor(w, stage);
        switch (stage)
        {
            case < 2: Faces.Set(w, Mood.Aroused); break;
            case < 4: Faces.Set(w, Mood.Hot); break;
            case 4: Faces.Set(w, Mood.Climax); break;
            default: Faces.Set(w, Mood.Afterglow); break;
        }
    }

    private static void TokTick(Watcher w, float now)
    {
        // Someone in the scene climaxes: if she is close, she finishes with them.
        if (w.ReactAt >= 0 && now >= w.ReactAt)
        {
            w.ReactAt = -1f;
            if (w.TokStage >= 2 && w.TokStage < TokOrgasm) { TokStage(w, TokOrgasm); return; }
        }
        bool done;
        if (w.TokStage == TokOrgasm)
        {
            // The climax plays once; she moves on when it has finished.
            var anim = w.Human.body?.animBody;
            var info = anim != null ? anim.GetCurrentAnimatorStateInfo(0) : default;
            done = anim == null || (!anim.IsInTransition(0) && info.normalizedTime >= 0.98f);
        }
        else done = now >= w.TokStageUntil;
        if (!done) return;
        if (w.TokStage + 1 < TokStates.Length) TokStage(w, w.TokStage + 1);
        else StopTouching(w);
    }

    // She moves her clothes aside as she goes: panties, skirt and pantyhose first, then her top and bra.
    private static readonly ChaFileDefine.ClothesKind[] LowerClothes =
        { ChaFileDefine.ClothesKind.shorts, ChaFileDefine.ClothesKind.bot, ChaFileDefine.ClothesKind.panst };
    private static readonly ChaFileDefine.ClothesKind[] UpperClothes =
        { ChaFileDefine.ClothesKind.top, ChaFileDefine.ClothesKind.bra };

    internal static void UndressFor(Watcher w, int stage)
    {
        HalfUndress(w, LowerClothes);
        if (stage >= 1) HalfUndress(w, UpperClothes);
    }

    private static void HalfUndress(Watcher w, ChaFileDefine.ClothesKind[] kinds)
    {
        var cloth = w.Human?.cloth;
        if (cloth == null) return;
        w.Undressed ??= new();
        foreach (var kind in kinds)
            try
            {
                if (w.Undressed.ContainsKey(kind) || !cloth.IsExist(kind)) continue;
                var state = cloth.GetClothesStateType(kind);
                if (state != ChaFileDefine.ClothesState.Clothing) continue;
                var to = cloth.IsClothesStateType(kind, ChaFileDefine.ClothesState.HalfUndress) ? ChaFileDefine.ClothesState.HalfUndress : ChaFileDefine.ClothesState.Naked;
                if (!cloth.IsClothesStateType(kind, to)) continue;
                cloth.SetClothesState(kind, to, true);
                w.Undressed[kind] = (state, to);
            }
            catch { }
    }

    // When she stops she puts back what she moved, unless someone changed it in the meantime.
    internal static void RestoreClothes(Watcher w)
    {
        var cloth = w.Human?.cloth;
        if (cloth == null || w.Undressed == null) return;
        foreach (var (kind, (was, set)) in w.Undressed)
            try { if (cloth.GetClothesStateType(kind) == set) cloth.SetClothesState(kind, was, true); }
            catch { }
        w.Undressed = null;
    }

    // ---------- watching ----------

    private static RuntimeAnimatorController _standCtrl;
    private static RuntimeAnimatorController _mapCtrl;
    private static bool _standMixed;

    // Standing and watching: the pose she had on the map (her own explore animator), or failing that the idle
    // of the standing masturbation, which is a plain standing pose.
    internal static void BackToWatching(Watcher w, bool place)
    {
        if (w.Human == null) return;
        RestoreHeight(w);
        if (w.Tired) { LieDown(w); return; }
        w.HeadCheckAt = 0f;
        var anim = w.Human.body?.animBody;
        if (anim != null)
        {
            RuntimeAnimatorController map = null;
            try { map = w.Npc?.Params?.Chara?.body?.animBody?.runtimeAnimatorController; } catch { }
            // A girl who was in the scene (swapped out) has her map body playing H; any girl's map animator does.
            if (map != null && map.name.StartsWith("f_")) _mapCtrl = map;
            else map = _mapCtrl;
            if (map != null)
            {
                anim.runtimeAnimatorController = map;
                w.Mixed = false;
                w.Pose = "map";
                SetBlend(w);
                LogParams(anim, "map animator");
            }
            else
            {
                if (_standCtrl == null) _standCtrl = Controller(TokBundle, "tok_f_03", out _standMixed);
                if (_standCtrl != null)
                {
                    anim.runtimeAnimatorController = _standCtrl;
                    w.Mixed = _standMixed;
                    SetBlend(w);
                    var hash = StateHash(w, anim, "Idle", Size(w));
                    if (hash != 0) anim.Play(hash, 0, Random.value);
                }
                w.Pose = "stand";
            }
            anim.speed = 1f;
        }
        RestoreClothes(w);
        w.NextPoseAt = Time.realtimeSinceStartup + Random.Range(5f, 12f);
        w.ReactAt = -1f;
        Faces.Set(w, w.Excitement < 0.35f ? Mood.Shy : Mood.Curious);
        if (!place || _scene == null) return;
        if (!w.Pinned) Place(_scene, w);
        LookAtScene(_scene, w);
    }
}
