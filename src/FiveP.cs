using AC.Scene.Explore;
using Character;
using H;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace AC_HWatchers;

// 5P from watchers (experimental). The game only starts a 5P from its own events: an H scene with four girls and
// Is5P set. Here the 3P button is turned into a 5P: when the game restarts H with the girl it brings, two more
// experienced watchers are added to the scene's receivers as the scene is set up (HScene.InitializeParameter), and
// Is5P is set. The game's map side only knows the first two girls; the scene itself restores every actor it had.
internal static class FiveP
{
    internal static bool Wanted;
    private static readonly List<NPC> _remembered = new();
    // The added girls and where they stood on the map, to check (and fix) after H that they're back there.
    private static readonly List<Saved> _added = new();

    // How an added girl's map body was before H: the scene poses that body, and the map side doesn't put it back.
    private sealed class Saved
    {
        public NPC Npc;
        public Vector3 Pos;
        public Quaternion Rot;
        public Human Body;
        public RuntimeAnimatorController Controller;
        public Transform Parent;
        public Vector3 LocalPos;
        public Quaternion LocalRot;
        public Vector3 LocalScale;
        public bool RootMotion;
        public readonly List<(Behaviour b, bool on)> Rig = new();
    }
    private static float _checkAt = -1f;

    internal static bool Enabled => Plugin.FiveP.Value;

    public static int EligibleWatchers() => Watchers.Present.Count(w => w.Npc != null && Invite.Eligible(w.Data));

    public static bool Offered => Enabled && Invite.Any && EligibleWatchers() >= 3;

    public static void Toggle()
    {
        Wanted = !Wanted;
        Plugin.Log.LogInfo(Wanted ? "The 3P button will start a 5P with two more watchers." : "The 3P button brings one girl again.");
    }

    // As the scene ends (the 3P button ends it first): the experienced watchers, nearest first.
    public static void Remember()
    {
        if (!Wanted) { _remembered.Clear(); return; }
        if (!Watchers.Present.Any()) return;
        _remembered.Clear();
        foreach (var w in Watchers.Present.Where(w => w.Npc != null && Invite.Eligible(w.Data)).OrderBy(w => w.StartDistance))
            _remembered.Add(w.Npc);
    }

    public static void AtSceneStart(HScene.InputParameter p)
    {
        if (!Wanted) return;
        Wanted = false;
        try
        {
            var r = p.Receivers;
            if (!Enabled || p.Is5P || r == null || r.Length != 2 || r[1]?.ActorData == null || !Invite.WasFirst(r[1].ActorData.Pointer))
            {
                Plugin.Debug($"5P: not the 3P button's scene ({r?.Length ?? 0} girls); nothing added.");
                return;
            }
            var taken = new HashSet<IntPtr>();
            for (var i = 0; i < r.Length; i++) if (r[i]?.ActorData != null) taken.Add(r[i].ActorData.Pointer);
            if (Swaps.PendingJoin != IntPtr.Zero) taken.Add(Swaps.PendingJoin);
            foreach (var key in Swaps.CarryKeys()) taken.Add(key);
            var extras = new List<NPC>();
            foreach (var n in _remembered)
            {
                if (extras.Count == 2) break;
                try { if (n?.BaseData != null && !taken.Contains(n.BaseData.Pointer)) extras.Add(n); } catch { }
            }
            if (extras.Count < 2) { Plugin.Log.LogInfo("5P: fewer than two more experienced watchers; it stays a 3P."); return; }
            var arr = new Il2CppReferenceArray<HActor>(4);
            arr[0] = r[0];
            arr[1] = r[1];
            _added.Clear();
            for (var i = 0; i < 2; i++)
            {
                arr[2 + i] = new HActor(extras[i]);
                _added.Add(Save(extras[i]));
            }
            p.Receivers = arr;
            p.Is5P = true;
            Plugin.Log.LogInfo($"5P: {string.Join(", ", extras.Select(n => Report.Name(n.BaseData.HumanData)))} join the scene " +
                               $"(5P place check {SafeIs5P(arr.Length, p.Attackers?.Length ?? 0)}).");
        }
        catch (Exception e) { Plugin.Log.LogError($"5P: setting up the scene failed: {e}"); }
        finally { _remembered.Clear(); }
    }

    public static void SceneEnded() { if (_added.Count > 0 && _checkAt < 0f) _checkAt = Time.realtimeSinceStartup + 1f; }

    private static Saved Save(NPC npc)
    {
        var sv = new Saved { Npc = npc, Pos = npc.Transform.position, Rot = npc.Transform.rotation };
        try
        {
            var h = npc.Params?.Chara;
            if (h == null) return sv;
            sv.Body = h;
            var t = h.transform;
            sv.Parent = t.parent;
            sv.LocalPos = t.localPosition;
            sv.LocalRot = t.localRotation;
            sv.LocalScale = t.localScale;
            var anim = h.body?.animBody;
            if (anim != null) { sv.Controller = anim.runtimeAnimatorController; sv.RootMotion = anim.applyRootMotion; }
            void Keep<T>() where T : Behaviour
            {
                foreach (var c in h.gameObject.GetComponentsInChildren<T>(true)) sv.Rig.Add((c, c.enabled));
            }
            Keep<RootMotion.FinalIK.FullBodyBipedIK>();
            Keep<ILLGAMES.Unity.Correct.BaseCorrect>();
            Keep<ILLGAMES.Unity.Correct.Process.BaseProcess>();
            Plugin.Debug($"5P: saved {Report.Name(npc.BaseData.HumanData)}'s map body (animator {sv.Controller?.name}, parent {sv.Parent?.name}, {sv.Rig.Count} rig parts).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"5P: saving a girl's map body failed: {e.Message}"); }
        return sv;
    }

    // Shortly after H: the added girls get their map body back as it was (place, animator, rig).
    public static void AfterScene()
    {
        if (_checkAt < 0f || Time.realtimeSinceStartup < _checkAt) return;
        _checkAt = -1f;
        foreach (var sv in _added)
        {
            var npc = sv.Npc;
            try
            {
                var name = Report.Name(npc.BaseData.HumanData);
                var now = npc.Transform.position;
                var moved = (now - sv.Pos).magnitude;
                if (moved > 0.5f)
                {
                    try { npc._agent?.Warp(sv.Pos); } catch { }
                    npc.Transform.SetPositionAndRotation(sv.Pos, sv.Rot);
                }
                var h = sv.Body;
                var parts = new List<string>();
                if (h?.gameObject != null)
                {
                    var t = h.transform;
                    if (sv.Parent != null && (t.parent == null || t.parent.Pointer != sv.Parent.Pointer)) { t.SetParent(sv.Parent, false); parts.Add("parent"); }
                    t.localPosition = sv.LocalPos;
                    t.localRotation = sv.LocalRot;
                    t.localScale = sv.LocalScale;
                    var anim = h.body?.animBody;
                    if (anim != null && sv.Controller != null)
                    {
                        if (anim.runtimeAnimatorController == null || anim.runtimeAnimatorController.Pointer != sv.Controller.Pointer)
                            parts.Add($"animator {anim.runtimeAnimatorController?.name} -> {sv.Controller.name}");
                        anim.runtimeAnimatorController = sv.Controller;
                        anim.applyRootMotion = sv.RootMotion;
                        anim.speed = 1f;
                        anim.Rebind();
                    }
                    var rig = 0;
                    foreach (var (b, on) in sv.Rig)
                        try { if (b != null && b.enabled != on) { b.enabled = on; rig++; } } catch { }
                    if (rig > 0) parts.Add($"{rig} rig parts");
                }
                Plugin.Debug($"5P: after H {name} was {moved:0.00} from her map place; put back: {(parts.Count > 0 ? string.Join(", ", parts) : "nothing else")}.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"5P: putting a girl back after H failed: {e.Message}"); }
        }
        _added.Clear();
    }

    private static bool SafeB(Func<bool> f) { try { return f(); } catch { return false; } }

    private static string SafeIs5P(int r, int a)
    {
        try { return Define.Place.Is5P(r, a).ToString(); } catch (Exception e) { return e.Message; }
    }
}
