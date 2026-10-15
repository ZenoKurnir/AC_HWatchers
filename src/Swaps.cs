using AC.Scene;
using AC.Scene.Explore;
using AC.User;
using Character;
using H;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AC_HWatchers;

// Swapping a watcher into the 3P. The scene can't take a new actor once it has started, so the actor stays and her
// body is rebuilt from the watcher's card (Human.data + Reload), keeping her pose, outfit type, clothes state and
// marks. Her voice follows the new girl. The game's own bookkeeping (H counts, experience, results) still goes to the
// girl the scene started with. The girl who was in the scene takes the watcher's spot. Before H ends every actor gets
// her own body back, so the map and the save see nothing of it.
internal sealed class SwapSlot
{
    public HActor Actor;
    public Human Human;
    public HumanData Original;        // the body's own data object; never written to, put back when H ends
    public IntPtr OriginalKey;        // the actor's save data (ActorData) the scene started with
    public IntPtr ShownKey;           // whose save data the body shows now
    public HumanData ActorData;       // HActor.Data, fileParam, fileStatus, fileGameParam as the scene set them
    public HumanDataParameter Param;
    public HumanDataStatus Status;
    public HumanDataGameParameter_AC GameParam;
    public int Personality;
    public float Pitch;
    public int Coord = -1;            // the outfit type the scene started with
}

internal static class Swaps
{
    private static readonly List<SwapSlot> _slots = new();
    private static bool _ending;
    // The girl picked in the menu to join with the 3P button: the game brings its own first pick, and she is swapped
    // in for her as soon as the 3P starts.
    internal static IntPtr PendingJoin;
    private static float _pendingSince = -1f;

    internal static bool Enabled => Plugin.Swaps.Value;

    // ---------- who the menu offers ----------

    // The girls in the scene the menu can swap a watcher with (any H: one girl, 3P, Les, 5P).
    public static List<(HActor actor, string name)> Targets(HScene scene)
    {
        var result = new List<(HActor, string)>();
        if (scene == null || !Enabled) return result;
        try
        {
            foreach (var a in Watchers.ActiveActors(scene))
            {
                if (a.Sex != 1 || Residents.Girl(a.ActorData) == null) continue;
                var npc = NpcByKey(KeyOf(a));
                var name = Watchers.SafeName(() => npc.BaseData.HumanData.Parameter.fullname) ?? Report.Name(a.ActorData?.HumanData);
                result.Add((a, name));
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Listing the scene's girls failed: {e.Message}"); }
        return result;
    }

    public static bool CanSwap(Watcher w) =>
        Enabled && w?.Human != null && w.Npc != null && !w.Locked && Invite.Eligible(w.Data);

    // Whose save data an actor shows: her own, or the girl swapped into her body.
    internal static IntPtr KeyOf(HActor a)
    {
        foreach (var s in _slots) if (s.Actor != null && s.Actor.Pointer == a.Pointer) return s.ShownKey;
        return a.ActorData?.Pointer ?? IntPtr.Zero;
    }

    internal static NPC NpcByKey(IntPtr key)
    {
        var explore = ExploreScene.Instance;
        if (explore == null || key == IntPtr.Zero) return null;
        foreach (var n in Residents.All(explore))
        {
            try { if (n?.BaseData != null && n.BaseData.Pointer == key) return n; } catch { }
        }
        return null;
    }

    // ---------- swapping ----------

    public static void SwapIn(HScene scene, Watcher w, HActor actor)
    {
        if (!CanSwap(w) || scene == null || actor?.Human == null) return;
        var outKey = KeyOf(actor);
        var outNpc = NpcByKey(outKey);
        if (outNpc == null) { Plugin.Log.LogWarning("Swap: the girl in the scene isn't on the map; nothing changed."); return; }
        var inNpc = w.Npc;
        var inName = w.Name;
        var outName = Watchers.SafeName(() => outNpc.BaseData.HumanData.Parameter.fullname) ?? "her";

        // Each keeps what she has on: the watcher's goes into the scene, the girl leaving keeps hers.
        Wardrobe.Remember(w.Key, w.Human);
        Wardrobe.Remember(outKey, actor.Human);
        var inCoord = Watchers.Outfit(w);
        try { scene.FadeStart(0.6f); } catch { }

        Watchers.Dismiss(w);
        if (!Show(scene, actor, inNpc, inCoord)) return;

        // The girl going out watches from where the one coming in stood, in the clothes she had on in the scene.
        Tired.AfterSwap(scene, inNpc.BaseData.Pointer, outKey);
        var back = Watchers.SpawnNow(scene, outNpc, 0f);
        if (back != null) back.Excitement = Mathf.Max(back.Excitement, 0.5f);
        Plugin.Log.LogInfo($"{inName} swapped in for {outName}.");
    }

    // The menu's pick to join with the 3P button.
    public static void ChooseJoiner(Watcher w)
    {
        PendingJoin = PendingJoin == w.Key ? IntPtr.Zero : w.Key;
        Plugin.Debug(PendingJoin == IntPtr.Zero ? "No watcher picked to join; the 3P button brings the first one." : $"{w.Name} will join with the 3P button.");
    }

    // A new 3P from the game's button: the second girl is the game's pick; the menu's pick takes her place.
    public static void AtSceneStart(HScene.InputParameter p)
    {
        if (PendingJoin == IntPtr.Zero) return;
        var keep = false;
        try
        {
            var r = p.Receivers;
            if (r != null && r.Length >= 2)
                for (var i = 1; i < r.Length; i++)
                    if (r[i]?.ActorData != null && Invite.WasFirst(r[i].ActorData.Pointer)) keep = r[i].ActorData.Pointer != PendingJoin;
        }
        catch { }
        if (!keep) PendingJoin = IntPtr.Zero;
        _pendingSince = -1f;
        Plugin.Debug(keep ? "3P from the button: the picked watcher will take the second girl's place." : "No pending 3P pick for this scene.");
    }

    public static void EndCheck(HScene scene, bool now = false)
    {
        if (_slots.Count > 0 && !_ending && _slots.Any(x => x.ShownKey != x.OriginalKey) && (now || scene.IsEnded)) RestoreAll(scene, true);
    }

    // ---------- carrying a swap into the next scene ----------

    // When H restarts (the 3P button ends the scene and starts a new one with the same girl), the swapped-in girl
    // stays in her place: the bodies are given back as the old scene ends, and she is swapped in again as the new
    // one starts. A normal end leaves nothing to carry: no new scene starts with that girl in time.
    private static readonly List<(IntPtr from, IntPtr to)> _carry = new();
    private static float _carryAt = -1f, _carrySeen = -1f;

    // The girls the scene shows in place of its own (the 3P button must not bring one of them).
    internal static HashSet<IntPtr> ShownKeys() => _slots.Where(x => x.ShownKey != x.OriginalKey).Select(x => x.ShownKey).ToHashSet();

    internal static IEnumerable<IntPtr> CarryKeys() => _carry.Select(c => c.to);

    // Whose body the actor with this save data will show once the carried swaps are done.
    internal static IntPtr CarriedTo(IntPtr from)
    {
        foreach (var c in _carry) if (c.from == from) return c.to;
        return from;
    }

    // A swap to make as soon as that girl shows in the scene (a tired or fresh girl taking her place).
    internal static void AddCarry(IntPtr from, IntPtr to)
    {
        _carry.RemoveAll(c => c.from == from);
        if (from != to) _carry.Add((from, to));
        _carrySeen = -1f;
    }

    private static float _noSceneSince = -1f;

    // Back on the map for a while: nothing is carried any more.
    public static void NoScene()
    {
        if (_carry.Count == 0) { _noSceneSince = -1f; return; }
        if (_noSceneSince < 0f) { _noSceneSince = Time.realtimeSinceStartup; return; }
        if (Time.realtimeSinceStartup - _noSceneSince < 8f) return;
        _carry.Clear();
        _noSceneSince = -1f;
        Plugin.Debug("Swap: no new scene with the swapped girls; nothing carried over.");
    }

    private static void Remember()
    {
        var swapped = _slots.Where(x => x.ShownKey != x.OriginalKey).ToList();
        if (swapped.Count == 0) return;
        _carry.Clear();
        foreach (var x in swapped) _carry.Add((x.OriginalKey, x.ShownKey));
        _carryAt = Time.realtimeSinceStartup;
        _carrySeen = -1f;
    }

    private static void CarryTick(HScene scene, float now)
    {
        if (_carry.Count == 0) return;
        _noSceneSince = -1f;
        if (_carrySeen < 0f) { _carrySeen = now; return; }
        if (now - _carrySeen < 1f) return;
        var inScene = new HashSet<IntPtr>();
        foreach (var a in Watchers.ActiveActors(scene)) if (a.ActorData != null) inScene.Add(KeyOf(a));
        foreach (var (from, to) in _carry.ToList())
        {
            if (inScene.Contains(to)) { _carry.Remove((from, to)); continue; }
            // A girl waiting hidden (the 3P girl in a position for two) is swapped once she shows.
            var actor = Watchers.ActiveActors(scene).FirstOrDefault(a => a.Sex == 1 && a.ActorData != null && KeyOf(a) == from);
            if (actor?.Human == null) continue;
            _carry.Remove((from, to));
            var inNpc = NpcByKey(to);
            var outNpc = NpcByKey(from);
            if (inNpc == null || outNpc == null) continue;
            var w = Watchers.Present.FirstOrDefault(x => x.Key == to);
            if (w != null) { Wardrobe.Remember(w.Key, w.Human); Watchers.Dismiss(w); }
            Wardrobe.Remember(from, actor.Human);
            Wardrobe.TryGet(to, out _, out var coord);
            try { scene.FadeStart(0.6f); } catch { }
            if (!Show(scene, actor, inNpc, coord)) continue;
            Watchers.SpawnNow(scene, outNpc, 0f);
            Tired.AfterSwap(scene, to, from);
            Plugin.Log.LogInfo($"Swap carried into the new scene: {Report.Name(inNpc.BaseData.HumanData)} stays in place of {Report.Name(outNpc.BaseData.HumanData)}.");
        }
    }

    // Every frame while the scene runs: the pending pick, and giving bodies back as H ends.
    public static void Tick(HScene scene, float now)
    {
        CarryTick(scene, now);
        if (PendingJoin == IntPtr.Zero) return;
        if (_pendingSince < 0f) _pendingSince = now;
        if (now - _pendingSince < 1f) return;
        HActor second = null;
        foreach (var a in Watchers.ActiveActors(scene))
            if (a.Sex == 1 && a.ActorData != null && Invite.WasFirst(KeyOf(a))) { second = a; break; }
        if (second == null)
        {
            if (now - _pendingSince > 15f) { Plugin.Debug("The 3P girl never showed up; the pick is dropped."); PendingJoin = IntPtr.Zero; }
            return;
        }
        var w = Watchers.Present.FirstOrDefault(x => x.Key == PendingJoin);
        if (w == null)
        {
            if (now - _pendingSince > 4f && !Watchers.IsQueued(PendingJoin))
            {
                var npc = NpcByKey(PendingJoin);
                if (npc != null) Watchers.SpawnNow(scene, npc, 0f);
                else { PendingJoin = IntPtr.Zero; return; }
            }
            return;
        }
        PendingJoin = IntPtr.Zero;
        SwapIn(scene, w, second);
    }

    // Rebuilds the actor's body as the girl walking the map as npc.
    private static bool Show(HScene scene, HActor actor, NPC npc, int coord)
    {
        var h = actor.Human;
        var slot = _slots.FirstOrDefault(s => s.Actor.Pointer == actor.Pointer);
        if (slot == null)
        {
            slot = new SwapSlot
            {
                Actor = actor, Human = h, Original = h.data, OriginalKey = actor.ActorData.Pointer, ShownKey = actor.ActorData.Pointer,
                ActorData = actor.Data, Param = actor.fileParam, Status = actor.fileStatus, GameParam = actor.fileGameParam,
                Personality = actor.Personality, Pitch = actor.VoicePitch
            };
            try { slot.Coord = h.fileStatus.coordinateType; } catch { }
            _slots.Add(slot);
            Plugin.Debug($"Swap slot for {Report.Name(actor.ActorData?.HumanData)}: body data is the save's own {h.data?.Pointer == actor.ActorData.HumanData?.Pointer}, " +
                         $"actor data is body data {actor.Data?.Pointer == h.data?.Pointer}, the map body {MapBody(actor)}, " +
                         $"personality {actor.Personality}, pitch {actor.VoicePitch:0.00}.");
        }
        return Apply(scene, slot, npc.BaseData.Pointer, npc.BaseData.HumanData, coord);
    }

    private static string MapBody(HActor actor)
    {
        try
        {
            var n = NpcByKey(actor.ActorData.Pointer);
            var c = n?.Params?.Chara;
            return c == null ? "unknown" : c.Pointer == actor.Human.Pointer ? "is the same" : "is another";
        }
        catch { return "unknown"; }
    }

    private static bool Apply(HScene scene, SwapSlot slot, IntPtr key, HumanData source, int coord)
    {
        var actor = slot.Actor;
        var h = slot.Human;
        var t0 = Time.realtimeSinceStartup;
        var own = key == slot.OriginalKey;

        HumanData data;
        if (own) data = slot.Original;
        else
        {
            data = new HumanData();
            data.CopyWithoutPngData(source);
        }
        if (coord < 0) coord = slot.Coord;

        // What the scene has done to her so far, to carry over to the new body.
        var clothes = Clothes(h);
        var siru = new Dictionary<ChaFileDefine.SiruParts, Human.SiruLv>();
        try
        {
            foreach (ChaFileDefine.SiruParts part in Enum.GetValues(typeof(ChaFileDefine.SiruParts)))
                siru[part] = h.GetSiruFlags(part);
        }
        catch { }
        var anim = h.body?.animBody;
        RuntimeAnimatorController controller = null;
        int state = 0;
        float time = 0f, speed = 1f;
        var floats = new Dictionary<int, float>();
        if (anim != null)
        {
            controller = anim.runtimeAnimatorController;
            speed = anim.speed;
            try { var info = anim.GetCurrentAnimatorStateInfo(0); state = info.fullPathHash; time = info.normalizedTime; } catch { }
            try { foreach (var p in anim.parameters) if (p.type == AnimatorControllerParameterType.Float) floats[p.nameHash] = anim.GetFloat(p.nameHash); } catch { }
        }
        var layer = -1;
        try { var r = h.body?.rendBody; if (r != null) layer = r.gameObject.layer; } catch { }
        var pos = h.transform.position;
        var rot = h.transform.rotation;
        var voice = VoiceOf(scene, h, out var voicePath);
        var oldCloth = h.cloth;

        try { Rebuild(h, data, coord); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Swap: rebuilding the girl failed: {e}");
            try { Rebuild(h, slot.Original, slot.Coord); } catch { }
            return false;
        }

        if (_ending)
        {
            foreach (var k in clothes.Keys.ToList()) clothes[k] = ChaFileDefine.ClothesState.Clothing;
            foreach (var k in siru.Keys.ToList()) siru[k] = Human.SiruLv.None;
        }
        // She comes in wearing what she had on (watching, or when she was last in the scene), not what the girl
        // before her had on.
        else if (Wardrobe.TryGet(key, out var worn, out _)) clothes = worn;
        foreach (var kv in clothes)
        {
            try
            {
                if (!h.cloth.IsExist(kv.Key)) continue;
                var st = kv.Value;
                // Not every piece has a half-off look; one without it comes off instead.
                if (st != ChaFileDefine.ClothesState.Clothing && !h.cloth.IsClothesStateType(kv.Key, st))
                    st = h.cloth.IsClothesStateType(kv.Key, ChaFileDefine.ClothesState.Naked) ? ChaFileDefine.ClothesState.Naked : ChaFileDefine.ClothesState.Clothing;
                h.cloth.SetClothesState(kv.Key, st, true);
            }
            catch { }
        }
        try
        {
            h.cloth.SetUpdateClothesStateAll();
            h.cloth.UpdateVisible();
            h.UpdateVisible();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: clothes update: {e.Message}"); }
        try
        {
            foreach (var kv in siru) h.SetSiruFlags(kv.Key, kv.Value);
            h.body?.UpdateSiru();
            h.face?.UpdateSiru();
        }
        catch { }
        h.transform.SetPositionAndRotation(pos, rot);
        var newAnim = h.body?.animBody;
        if (newAnim != null)
        {
            if (controller != null && newAnim.runtimeAnimatorController != controller) newAnim.runtimeAnimatorController = controller;
            newAnim.speed = speed;
            foreach (var kv in floats) try { newAnim.SetFloat(kv.Key, kv.Value); } catch { }
            if (state != 0) newAnim.Play(state, 0, time);
            if (actor.Animator == null || actor.Animator.Pointer != newAnim.Pointer)
            {
                Plugin.Debug("Swap: the animator changed with the new body.");
                actor.Animator = newAnim;
            }
            // Her own bust size for the motions that blend on it.
            try { SetFloat(newAnim, "breast", Mathf.Clamp01(h.fileBody.shapeValueBody[3])); } catch { }
        }
        if (layer >= 0)
            foreach (var r in h.gameObject.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer != layer) r.gameObject.layer = layer;

        if (own)
        {
            actor.Data = slot.ActorData;
            actor.fileParam = slot.Param;
            actor.fileStatus = slot.Status;
            actor.fileGameParam = slot.GameParam;
            actor.Personality = slot.Personality;
            actor.VoicePitch = slot.Pitch;
        }
        else
        {
            actor.Data = data;
            actor.fileParam = data.Parameter;
            actor.fileStatus = data.Status;
            actor.fileGameParam = data.GameParameter;
            try { actor.Personality = data.Parameter.personality; } catch { }
            try { actor.VoicePitch = data.Parameter.voicePitch; } catch { }
        }
        // The clothes keep their states in the status they were built with; the actor reads the same one.
        try { var st = h.cloth?._fileStatus; if (st != null && (actor.fileStatus == null || actor.fileStatus.Pointer != st.Pointer)) actor.fileStatus = st; } catch { }
        slot.ShownKey = key;
        Plugin.Debug($"Swap: clothes object {(oldCloth == null || h.cloth == null ? "missing" : oldCloth.Pointer == h.cloth.Pointer ? "the same" : "replaced")}.");
        Revoice(scene, actor, h, voice, voicePath);
        Plugin.Debug($"Swap: {Report.Name(actor.ActorData?.HumanData)}'s body now shows {Report.Name(data)} (personality {actor.Personality}, pitch {actor.VoicePitch:0.00}, " +
                     $"outfit {coord}, {(Time.realtimeSinceStartup - t0) * 1000:0} ms).");
        return true;
    }

    private static Dictionary<ChaFileDefine.ClothesKind, ChaFileDefine.ClothesState> Clothes(Human h)
    {
        var clothes = new Dictionary<ChaFileDefine.ClothesKind, ChaFileDefine.ClothesState>();
        try
        {
            foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                if (h.cloth.IsClothesStateKind(kind)) clothes[kind] = h.cloth.GetClothesStateType(kind);
        }
        catch { }
        return clothes;
    }

    private static void SetFloat(Animator a, string name, float value)
    {
        foreach (var p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name == name) { a.SetFloat(p.nameHash, value); return; }
    }

    // Data first, then the outfit she wears, then everything is rebuilt: Reload() alone keeps the old hair and clothes.
    private static void Rebuild(Human h, HumanData data, int coord)
    {
        h.data = data;
        if (coord >= 0)
        {
            // The outfit she wears is a separate copy (coorde.Now), filled from a throwaway copy of the card so
            // nothing the scene does to the clothes can reach the girl's own save data.
            try
            {
                data.Status.coordinateType = coord;
                var copy = new HumanData();
                copy.CopyWithoutPngData(data);
                h.coorde.SetNowCoordinate(copy.Coordinates[coord]);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Swap: outfit: {e.Message}"); }
        }
        var all = Human.ReloadParameter.All;
        h.Reload(ref all);
    }

    // ---------- voice ----------

    // The scene's voice player keeps one entry per girl, tied to a bone of her body.
    private static H.Sound.Voice.HVoiceCtrl.Voice VoiceOf(HScene scene, Human h, out string path)
    {
        path = null;
        try
        {
            var voices = scene.CtrlVoice?.NowVoices;
            if (voices == null) return null;
            for (var i = 0; i < voices.Length; i++)
            {
                var t = voices[i]?.Transform;
                if (t == null || !t.IsChildOf(h.transform)) continue;
                path = PathTo(h.transform, t);
                return voices[i];
            }
        }
        catch { }
        return null;
    }

    private static string PathTo(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x.Pointer != root.Pointer; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }

    private static void Revoice(HScene scene, HActor actor, Human h, H.Sound.Voice.HVoiceCtrl.Voice voice, string path)
    {
        if (voice == null) { Plugin.Debug("Swap: no voice entry for this girl."); return; }
        try
        {
            if (path != null && (voice.Transform == null || !voice.Transform.IsChildOf(h.transform)))
            {
                var t = h.transform.Find(path);
                if (t != null) voice.Transform = t;
                Plugin.Debug($"Swap: voice bone {path} {(t != null ? "found again" : "not found")} on the new body.");
            }
            var was = voice.Personality;
            voice.Personality = actor.Personality;
            voice.VoicePitch = actor.VoicePitch;
            var fix = voice.FixOrPersonality;
            if (fix == was) voice.FixOrPersonality = actor.Personality;
            if (was == actor.Personality) { Plugin.Debug($"Swap: same voice personality {was}."); return; }
            var vc = scene.CtrlVoice;
            var parts = new List<string>();
            try { vc.LoadVoiceList(); parts.Add("voice"); } catch (Exception e) { parts.Add($"voice failed: {e.Message}"); }
            try { vc.LoadBreathList(); parts.Add("breath"); } catch (Exception e) { parts.Add($"breath failed: {e.Message}"); }
            try { vc.LoadShortBreathList(); parts.Add("short"); } catch (Exception e) { parts.Add($"short failed: {e.Message}"); }
            try { var info = scene.NowAnimationInfo; if (info != null) { vc.SetVoiceList(info); parts.Add("position"); } } catch (Exception e) { parts.Add($"position failed: {e.Message}"); }
            Plugin.Debug($"Swap: voice personality {was} -> {voice.Personality} (fixed {fix} -> {voice.FixOrPersonality}; lists reloaded: {string.Join(", ", parts)}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: voice: {e.Message}"); }
    }

    // ---------- the end ----------

    // The game reads the girls for what follows H while it fades out, so everyone gets her own body back as soon as
    // the scene starts ending; when it is disposed the actors get their own data back too.
    public static void RestoreAll(HScene scene, bool rebuild)
    {
        if (_slots.Count == 0) { if (!rebuild) PendingJoinKeepOnly(); return; }
        if (rebuild) Remember();
        _ending = true;
        try
        {
            if (rebuild) Plugin.Debug("H is ending: the girls in the scene get their own bodies back.");
            foreach (var slot in _slots.ToList())
            {
                if (rebuild && slot.ShownKey != slot.OriginalKey && slot.Human?.gameObject != null)
                    try { Apply(scene, slot, slot.OriginalKey, slot.Original, slot.Coord); }
                    catch (Exception e) { Plugin.Log.LogWarning($"Swap: giving {Report.Name(slot.Actor?.ActorData?.HumanData)} her body back failed: {e.Message}"); }
                if (!rebuild)
                {
                    try { if (slot.Human != null && slot.Human.data?.Pointer != slot.Original?.Pointer) slot.Human.data = slot.Original; } catch { }
                    var a = slot.Actor;
                    if (a != null)
                    {
                        a.Data = slot.ActorData;
                        a.fileParam = slot.Param;
                        a.fileStatus = slot.Status;
                        a.fileGameParam = slot.GameParam;
                        a.Personality = slot.Personality;
                        a.VoicePitch = slot.Pitch;
                    }
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"Swap: restoring the scene's girls failed: {e}"); }
        finally { _ending = false; }
        if (!rebuild) { _slots.Clear(); PendingJoinKeepOnly(); }
    }

    // A pick made for the 3P button survives the end of the scene (the 3P is a new scene); nothing else does.
    private static void PendingJoinKeepOnly() => _pendingSince = -1f;

    public static void Forget() { _slots.Clear(); _carry.Clear(); PendingJoin = IntPtr.Zero; _pendingSince = -1f; }
}
