using AC.Scene;
using AC.Scene.Explore;
using AC.User;
using Character;
using H;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace AC_HWatchers;

internal sealed class Watcher
{
    public bool Mixed;            // her animator is the game's base + position mix (states without size letters)
    public ActorData Data;
    public NPC Npc;               // the girl walking the map; she stays where she is
    public IntPtr Key;            // her save data object
    public string Name;
    public int Slot;
    public float StartDistance;   // how far she was from the scene when H started, in meters
    public Human Human;           // a copy built from her card
    public bool Virgin;           // a virgin only watches: every option is locked
    public bool LikesGirls;       // has the "likes girls" trait
    public int Sexperience;       // 0..100, the game's H experience track
    public bool Locked => Virgin;

    public float Excitement;      // 0..1, grows as the scene goes on
    public float Temper;          // how quickly this girl gets worked up
    public float NextPoseAt;
    public float ReactAt = -1f;   // a pending reaction to a climax
    public float ReactionUntil;
    public string Pose;
    public bool Hidden;           // hidden while the camera is inside her
    public bool Pinned;           // moved by hand: she stays where she was put
    public bool Wet;              // standing in water: only standing poses
    public bool Tired;            // worn out in this H: she lies down and does nothing else

    public string TokSet;         // set while she is masturbating (tok_f_XX)
    public int TokStage;
    public float TokStageUntil;
    public Dictionary<ChaFileDefine.ClothesKind, (ChaFileDefine.ClothesState was, ChaFileDefine.ClothesState set)> Undressed;

    public Watcher Lover;         // her partner while the two of them make love
    public bool LoverLeads;       // the one of the two that runs their timing
    public int LoveStage;
    public float LoveStageUntil;
    public string LoveSet;        // les_fX_00 scissoring, 01 standing, 02 against a wall
    public Vector3? BaseScale;    // her own scale while a caress evens out the two heights
    public float? BlendHeight;    // the height the animators are told during a caress
    public float HeadCheckAt;     // a kiss: when to start watching where the two mouths are
    public Vector3 MouthSum;      // the gap between the mouths, summed over the frames watched
    public int MouthCount;
    public int MouthPasses;

    public Mood Mood;
    public float NextFaceAt;
    public float NextGaspAt;
}

internal static partial class Watchers
{
    private static readonly List<Watcher> _list = new();
    private static readonly Queue<(NPC npc, float dist)> _pending = new();
    private static HScene _scene;
    private static float _autoAt;
    private static bool _autoDone;
    private static float _nextSpawnAt;
    private static float _scale;
    internal static float LastScale => _scale > 0 ? _scale : 1.05f;
    private static Vector3 _ringDir;
    private static bool _wasOrgasm;
    private static Vector3 _anchor;
    private static bool _anchored;
    private static Vector3 _startPos;     // where H started
    private static bool _haveStart;
    private static float _nextArrangeAt;
    private static readonly Dictionary<IntPtr, Transform[]> _actorBones = new();

    internal const string TokBundle = "h/animator/female/000_01/tok.unity3d";
    internal const string TokBundle2 = "h/animator/female/020_01/tok.unity3d";
    internal const string LesBundle = "h/animator/female/000_01/les.unity3d";
    private static readonly float[] SlotAngles = { 35f, -35f, 65f, -65f, 95f, -95f, 125f, -125f };

    internal static IEnumerable<Watcher> Present => _list.Where(w => w.Human != null);
    internal static HScene Scene => _scene;
    public static Watcher Selected;

    // Called every frame from our own object; the H scene is found through its singleton.
    public static void Update()
    {
        HScene scene = null;
        try { if (HScene.IsActive()) scene = HScene.Instance; } catch { }
        if (scene == null)
        {
            try { FiveP.AfterScene(); } catch { }
            try { Wardrobe.NoScene(); } catch { }
            try { Swaps.NoScene(); Tired.NoScene(); Experience.NoScene(); } catch { }
            if (_scene != null) { Clear("H ended"); _scene = null; FiveP.SceneEnded(); }
            return;
        }
        if (_scene == null || _scene.Pointer != scene.Pointer)
        {
            Clear("new H scene");
            _sentBack.Clear();
            _scene = scene;
            _autoAt = Time.realtimeSinceStartup + 1.5f;
            _autoDone = false;
            _ringDir = Vector3.zero;
            _anchored = false;
            _haveStart = false;
            _actorBones.Clear();
            Report.Scheduled = Time.realtimeSinceStartup + 3f;
        }
        try { Swaps.EndCheck(scene); } catch (Exception e) { Plugin.Log.LogWarning($"Swaps at the end: {e.Message}"); }
        try { AC_HWatchers.Tired.AtEnd(scene); } catch { }
        bool ready = false;
        try { ready = scene._initialized && !scene.IsEnded && AnyActive(scene); } catch { }
        if (!ready) return;

        var now = Time.realtimeSinceStartup;
        if (!_haveStart) { _startPos = SceneCenter(scene); _haveStart = true; }
        Report.Tick(scene, now);
        try { Swaps.Tick(scene, now); } catch (Exception e) { Plugin.Log.LogWarning($"Swaps: {e.Message}"); }
        try { Moving.Tick(scene); } catch (Exception e) { Plugin.Log.LogWarning($"Moving: {e.Message}"); }
        try { Tired.Tick(scene, now); } catch (Exception e) { Plugin.Log.LogWarning($"Tiredness: {e.Message}"); }
        try { Experience.Tick(scene, now); } catch (Exception e) { Plugin.Log.LogWarning($"Experience: {e.Message}"); }
        if (!_autoDone && now > _autoAt)
        {
            _autoDone = true;
            if (Plugin.AutoBring.Value) QueueWatchers(scene, Plugin.MaxWatchers.Value);
        }

        if (Input.GetKeyDown(Plugin.WatcherKey.Value))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Clear("sent back");
            else QueueWatchers(scene, 1);
        }

        if (_pending.Count > 0 && now >= _nextSpawnAt)
        {
            // One girl at a time, so building them doesn't freeze the game in one go.
            var (npc, dist) = _pending.Dequeue();
            Spawn(scene, npc, dist);
            _nextSpawnAt = now + 0.3f;
        }

        if (_list.Count == 0) return;
        if (Time.frameCount % 15 == 0) { SyncWithActors(scene); Wardrobe.Tick(scene); }
        if (now >= _nextArrangeAt)
        {
            _nextArrangeAt = now + 0.4f;
            try { Arrange(scene); } catch (Exception e) { Plugin.Debug($"Arranging watchers failed: {e.Message}"); }
        }
        var intensity = ReadScene(scene, now);
        foreach (var w in Present.ToList())
            try { Tick(scene, w, now, intensity); }
            catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: {e.Message}"); }
    }

    // ---------- who watches ----------

    // The girls of the map nearest to where H started, within reach, nearest first.
    internal static List<(NPC npc, float dist)> Nearby(HScene scene)
    {
        var result = new List<(NPC, float)>();
        var explore = ExploreScene.Instance;
        if (explore == null) return result;
        var inScene = ActorDataKeys(scene, false);
        var k = Scale(scene);
        var area = Residents.SceneArea(scene, explore);
        var list = Residents.All(explore).Where(n => Residents.SameArea(n, area)).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var npc = list[i];
            try
            {
                var data = npc?.BaseData;
                if (data == null || inScene.Contains(data.Pointer)) continue;
                if (Residents.Girl(data) == null) continue;
                var dist = (npc.Transform.position - _startPos).magnitude / k;
                result.Add((npc, dist));
            }
            catch { }
        }
        return result.OrderBy(r => r.Item2).ToList();
    }

    private static void QueueWatchers(HScene scene, int count)
    {
        // A girl waiting in the scene for her turn (the 3P girl) watches too, and takes one of the spots.
        var room = Plugin.MaxWatchers.Value - _list.Count - _pending.Count - WaitingGirls(scene).Count(k => !_list.Any(x => x.Key == k) && !IsQueued(k));
        if (room <= 0)
        {
            Plugin.Debug($"All {Plugin.MaxWatchers.Value} watcher spots are taken.");
            return;
        }
        var taken = new HashSet<IntPtr>(_list.Select(w => w.Key).Concat(_pending.Select(p => p.npc.BaseData.Pointer)));
        var picked = Nearby(scene).Where(c => !taken.Contains(c.npc.BaseData.Pointer) && c.dist <= Plugin.Reach.Value).Take(Math.Min(count, room)).ToList();
        if (picked.Count == 0)
        {
            Plugin.Log.LogInfo($"No other girl within {Plugin.Reach.Value:0} m of the scene can come to watch.");
            return;
        }
        foreach (var c in picked) _pending.Enqueue(c);
    }

    // Builds her at once (a swap needs her right away), skipping the queue.
    internal static Watcher SpawnNow(HScene scene, NPC npc, float dist)
    {
        var key = npc.BaseData.Pointer;
        var already = _list.FirstOrDefault(x => x.Key == key);
        if (already != null) return already;
        Spawn(scene, npc, dist);
        return _list.FirstOrDefault(x => x.Key == key);
    }

    internal static bool IsQueued(IntPtr key) => _pending.Any(p => p.npc.BaseData.Pointer == key);

    private static void Spawn(HScene scene, NPC npc, float dist)
    {
        var t0 = Time.realtimeSinceStartup;
        var data = Residents.Girl(npc.BaseData);
        var w = new Watcher
        {
            Data = data, Npc = npc, Key = data.Pointer, StartDistance = dist,
            Name = SafeName(() => data.HumanData.Parameter.fullname) ?? npc.CharaFileName,
            Excitement = Random.Range(0.05f, 0.2f), Temper = Random.Range(0.7f, 1.3f)
        };
        try { w.Virgin = data.IsVirgin; } catch { }
        try { w.Sexperience = data.Sexperience; } catch { }
        w.LikesGirls = Traits.LikesGirls(data.HumanData);
        w.Slot = Enumerable.Range(0, SlotAngles.Length).Where(i => _list.All(x => x.Slot != i)).DefaultIfEmpty(_list.Count).First();
        if (_list.Count == 0) _ringDir = Vector3.zero; // a new group: aim the ring at the current camera
        try
        {
            // Her own copy of the card, so nothing done to the watcher (outfits, clothes) reaches the save.
            var copy = new HumanData();
            copy.CopyWithoutPngData(data.HumanData);
            w.Human = Human.Create(copy);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not build {w.Name} from her card: {e.Message}");
            return;
        }
        _list.Add(w);
        MatchOutfit(w);
        Wardrobe.DressWatcher(w);
        var anim = w.Human.body?.animBody;
        if (anim != null)
        {
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        w.Human.gameObject.SetActive(true);
        QuietRig(w);
        LayerLikeActors(scene, w.Human);
        Place(scene, w);
        LookAtScene(scene, w);
        BackToWatching(w, false);
        w.NextGaspAt = Time.realtimeSinceStartup + Random.Range(10f, 25f);
        Plugin.Debug($"{w.Name} came to watch ({dist:0.0} m away at the start; virgin {w.Virgin}, likes girls {w.LikesGirls}, " +
                           $"experience {w.Sexperience}, traits [{Traits.NameList(data.HumanData)}], size {Size(w)}; {(Time.realtimeSinceStartup - t0) * 1000:0} ms).");
    }

    // Every body carries a full-body IK and the pose corrections the H scene feeds per position (hand and foot targets,
    // per-bone fixes). Nobody feeds them on a watcher, so they pinned her feet, pulled her arms and lifted her off the
    // floor. The clips pose the whole skeleton on their own, so a watcher runs without them.
    private static void QuietRig(Watcher w)
    {
        var off = new List<string>();
        void Off<T>() where T : Behaviour
        {
            foreach (var c in w.Human.gameObject.GetComponentsInChildren<T>(true))
                if (c.enabled) { c.enabled = false; off.Add(c.GetIl2CppType().Name); }
        }
        try
        {
            Off<RootMotion.FinalIK.FullBodyBipedIK>();
            Off<ILLGAMES.Unity.Correct.BaseCorrect>();
            Off<ILLGAMES.Unity.Correct.Process.BaseProcess>();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: could not switch off the IK: {e.Message}"); }
        Plugin.Debug($"{w.Name}: switched off {off.Count} rig parts: " +
                     string.Join(", ", off.GroupBy(x => x).Select(g => $"{g.Key} x{g.Count()}")));
    }

    // A girl who joins the scene (the 3P girl) leaves the watchers.
    private static void SyncWithActors(HScene scene)
    {
        var inScene = ActorDataKeys(scene, true);
        foreach (var w in _list.ToList())
            if (inScene.Contains(w.Key))
            {
                Plugin.Log.LogInfo($"{w.Name} joined the scene; she stops watching.");
                Wardrobe.Remember(w.Key, w.Human);
                Wardrobe.Joining(w.Key);
                Dismiss(w);
            }
        // The 3P girl waits hidden in the scene while the position is for two; she watches until it's her turn.
        if (!_autoDone) return;
        try
        {
            foreach (var key in WaitingGirls(scene))
            {
                if (_list.Any(x => x.Key == key) || IsQueued(key)) continue;
                var npc = Swaps.NpcByKey(key);
                if (npc == null) continue;
                Plugin.Debug($"{SafeName(() => npc.BaseData.HumanData.Parameter.fullname)} waits for her turn in the scene; she watches meanwhile.");
                _pending.Enqueue((npc, 0f));
            }
        }
        catch (Exception e) { Plugin.Debug($"Waiting girl check failed: {e.Message}"); }
    }

    // The girls of the scene hidden right now (not in the position), who watch meanwhile.
    private static List<IntPtr> WaitingGirls(HScene scene)
    {
        var result = new List<IntPtr>();
        try
        {
            var inScene = ActorDataKeys(scene, true);
            var actors = scene._hActorAll;
            for (var i = 0; actors != null && i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null || a.Sex != 1 || Residents.Girl(a.ActorData) == null) continue;
                var key = Swaps.KeyOf(a);
                if (!inScene.Contains(key) && !_sentBack.Contains(key)) result.Add(key);
            }
        }
        catch { }
        return result;
    }

    // Girls sent back from the menu don't come back on their own in this scene.
    private static readonly HashSet<IntPtr> _sentBack = new();

    public static void SendBack(Watcher w)
    {
        _sentBack.Add(w.Key);
        Dismiss(w);
    }

    private static bool _clearing;

    public static void Clear(string why)
    {
        try { FiveP.Remember(); } catch { }
        try { AC_HWatchers.Tired.RememberPool(); } catch { }
        _pending.Clear();
        if (_list.Count == 0) return;
        _clearing = true;
        foreach (var w in _list) Dispose(w);
        _clearing = false;
        Plugin.Log.LogInfo($"Watchers left ({why}): {string.Join(", ", _list.Select(w => w.Name))}");
        _list.Clear();
    }

    private static void Dispose(Watcher w)
    {
        if (Selected == w) Selected = null;
        if (w.Lover != null)
        {
            var other = w.Lover;
            w.Lover = null;
            other.Lover = null;
            other.LoverLeads = false;
            if (!_clearing && other.Human != null) BackToWatching(other, true);
        }
        try { if (w.Human?.gameObject != null) w.Human.Dispose(); }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: unloading failed: {e.Message}"); }
        w.Human = null;
    }

    public static void Dismiss(Watcher w)
    {
        Dispose(w);
        _list.Remove(w);
    }

    // ---------- reacting to the scene ----------

    // How fast excitement grows right now, and a reaction for everyone when someone climaxes.
    private static float ReadScene(HScene scene, float now)
    {
        var intensity = 1f;
        try
        {
            var f = scene.CtrlFlag;
            if (f == null) return intensity;
            if (f.NowSpeedStateFast) intensity = 2.5f;
            var orgasm = f.IsNowOrgasm;
            if (orgasm && !_wasOrgasm)
            {
                Plugin.Debug("Someone in the scene climaxed.");
                foreach (var w in Present)
                    if (w.ReactAt < 0) w.ReactAt = now + Random.Range(0.1f, 0.8f);
            }
            _wasOrgasm = orgasm;
        }
        catch { }
        return intensity;
    }

    private static void Tick(HScene scene, Watcher w, float now, float intensity)
    {
        Faces.Tick(w, now);
        if (AC_HWatchers.Tired.Is(w.Key) && w.Lover == null)
        {
            if (!w.Tired) { w.Tired = true; LieDown(w); }
            TiredTick(w, now);
            return;
        }
        w.Excitement = Mathf.Min(1f, w.Excitement + Time.deltaTime * 0.006f * intensity * w.Temper * Plugin.ExcitementSpeed.Value);
        if (w.Lover != null)
        {
            if (w.LoverLeads) LoveTick(w, now);
            else w.ReactAt = -1f;
            return;
        }
        if (w.TokSet != null)
        {
            TokTick(w, now);
            return;
        }
        if (w.ReactAt >= 0 && now >= w.ReactAt)
        {
            w.ReactAt = -1f;
            w.Excitement = Mathf.Min(1f, w.Excitement + 0.15f);
            Faces.Set(w, Mood.Hit); // surprised
            w.ReactionUntil = now + 3.5f;
            return;
        }
        if (now < w.ReactionUntil) return;
        if (w.ReactionUntil > 0f) { w.ReactionUntil = 0f; w.NextPoseAt = 0f; }
        if (now < w.NextPoseAt) return;

        w.NextPoseAt = now + Random.Range(10f, 22f);
        var e = w.Excitement;
        if (e < 0.35f) Faces.Set(w, Mood.Shy);
        else if (e < 0.7f) Faces.Set(w, Mood.Curious);
        else
        {
            if (TryPairUp(w)) return;
            if (CanTouch(w) && Random.value < 0.45f)
            {
                StartTouching(w, null);
                return;
            }
            Faces.Set(w, Mood.Aroused);
        }
        Plugin.Debug($"{w.Name}: excitement {e:0.00}");
    }

    // ---------- placement ----------

    private static readonly float[] AngleTries = { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 90f, -90f };
    private static readonly float[] DistanceTries = { 1f, 1.3f, 1.65f, 2.1f, 2.6f, 3.2f };

    internal static void Place(HScene scene, Watcher w)
    {
        w.Pinned = false;
        if (w.Lover != null)
        {
            if (!PlaceCouple(scene, w.LoverLeads ? w : w.Lover)) EndLove(w);
            return;
        }
        var center = SceneCenter(scene);
        AimRing(scene, center);
        var k = Scale(scene);
        center.y = SceneFloor(scene, center, k);
        if (!_anchored) { _anchor = center; _anchored = true; LogGround(center, k, "the scene"); MeasureGroundOffsets(center, k); }
        var raised = center.y;
        var floor = RoomFloor(center, k);
        var onFloor = floor < center.y;
        if (onFloor) center.y = floor;
        var bodies = ActorBones(scene);
        var baseAngle = SlotAngles[w.Slot % SlotAngles.Length];
        Vector3? first = null, chosen = null, cramped = null;
        bool firstWet = false, chosenWet = false;
        for (var pass = 0; pass < 2 && chosen == null; pass++)
        {
            if (pass == 1)
            {
                if (!onFloor) break;
                center.y = raised; first = null;
            }
            foreach (var dist in DistanceTries)
            {
                foreach (var off in AngleTries)
                {
                    var dir = Quaternion.Euler(0, baseAngle + off, 0) * _ringDir;
                    if (!Usable(center, center + dir * Plugin.Distance.Value * dist * k, k, out var pos, out var wet)) continue;
                    if (first == null && Roomy(pos, center.y, k) > 0) { first = pos; firstWet = wet; }
                    if (!IsClear(w, pos, bodies, k)) continue;
                    if (pass == 0 && onFloor && !Standable(pos, k)) continue; // in or under the bed
                    var room = Roomy(pos, center.y, k, out var why);
                    if (room < 2)
                    {
                        if (room == 1) cramped ??= pos;
                        if (Plugin.DebugLog.Value && pass == 0 && dist == DistanceTries[0]) Plugin.Debug($"{w.Name}: spot {off:0}° turned down: {why}.");
                        continue;
                    }
                    chosen = pos; chosenWet = wet;
                    break;
                }
                if (chosen != null) break;
            }
        }
        var floorCenter = center; floorCenter.y = onFloor ? floor : center.y;
        if (chosen == null) chosen = SnapToWalkable(w, floorCenter, baseAngle, bodies, k, ref cramped);
        if (chosen == null && cramped != null) Plugin.Debug($"{w.Name}: no open floor around the couple, using a cramped spot.");
        var spot = chosen ?? cramped ?? first ?? center + Quaternion.Euler(0, baseAngle, 0) * _ringDir * Plugin.Distance.Value * k;
        w.Wet = chosen != null ? chosenWet : firstWet;
        if (chosen == null) Plugin.Debug($"{w.Name}: no free spot around the couple, using {(first != null ? "the nearest one" : "a spot on their level")}.");
        if (Plugin.DebugLog.Value && Ground(spot, k, out _, out _, out var from, center.y))
            Plugin.Debug($"{w.Name}: stands on the {from} ({(spot.y - center.y) / k * 100f:0} cm from the couple's floor).");
        if (Plugin.DebugLog.Value) LogSurroundings(w, spot, k);
        var face = center - spot; face.y = 0;
        w.Human.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face : Vector3.forward));
    }

    private static void AimRing(HScene scene, Vector3 center)
    {
        if (_ringDir != Vector3.zero) return;
        var camera = Camera(scene);
        var cam = camera != null ? camera.transform.position : center + Vector3.back;
        _ringDir = cam - center; _ringDir.y = 0;
        if (_ringDir.sqrMagnitude < 0.01f) _ringDir = Vector3.back;
        _ringDir.Normalize();
    }

    private static bool Standable(Vector3 pos, float k)
    {
        try
        {
            var cols = Physics.OverlapCapsule(pos + Vector3.up * 0.45f * k, pos + Vector3.up * 1.4f * k, 0.2f * k, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
                if (c != null && !IsCharacter(c.transform) && !IsSound(c)) return false;
        }
        catch { }
        return true;
    }

    private static bool Usable(Vector3 center, Vector3 pos, float k, out Vector3 spot, out bool wet)
    {
        pos.y = center.y;
        wet = false;
        if (!Ground(pos, k, out spot, out var water, out _, center.y)) return false;
        if (Mathf.Abs(spot.y - center.y) > 0.3f * k) return false;
        if (!Level(spot, k)) return false;
        wet = water - spot.y > 0.2f * k;
        return InSight(center, spot, k);
    }

    private static bool Ground(Vector3 pos, float k, out Vector3 result) => Ground(pos, k, out result, out _);

    private static float Floor(Vector3 pos, float k) => Ground(pos, k, out var g) ? g.y : pos.y;

    private static float SceneFloor(HScene scene, Vector3 center, float k)
    {
        var lowest = float.PositiveInfinity;
        Vector3 at = center;
        try
        {
            foreach (var a in ActiveActors(scene))
                foreach (var name in new[] { "cf_J_Toes01_L", "cf_J_Toes01_R", "cf_J_Foot01_L", "cf_J_Foot01_R", "cf_j_toes_L", "cf_j_toes_R", "cf_j_foot_L", "cf_j_foot_R" })
                {
                    var t = FindBone(a.Human, name);
                    if (t != null && t.position.y < lowest) { lowest = t.position.y; at = t.position; }
                }
        }
        catch { }
        if (float.IsPositiveInfinity(lowest)) return Floor(center, k);
        at.y = lowest + 0.1f * k;
        return Ground(at, k, out var g, out _, out _, lowest) ? g.y : lowest;
    }

    // Where a spot's height came from. Away from the H spot a map often has no walkable collider but the footstep
    // (SE) mesh and the navmesh, and both sit a few cm above the floor the girls stand on.
    private enum GroundFrom { Floor, SoundMesh, NavMesh }
    private static float _soundOffset, _navOffset;

    private static bool Ground(Vector3 pos, float k, out Vector3 result, out float water) =>
        Ground(pos, k, out result, out water, out _);

    private static bool Ground(Vector3 pos, float k, out Vector3 result, out float water, out GroundFrom from, float? near = null)
    {
        if (!GroundRaw(pos, k, out result, out water, out from, near)) return false;
        if (from == GroundFrom.SoundMesh) result.y -= _soundOffset;
        else if (from == GroundFrom.NavMesh) result.y -= _navOffset;
        return true;
    }

    // Map colliders don't tell which surface is the walkable floor (the H spot's own mesh, footstep meshes and props
    // can sit above it), but the navmesh the map's girls walk on does. So the collider closest to the navmesh wins,
    // and the navmesh's own height when no collider is near it. Without a navmesh: the collider closest to near
    // (the couple's floor) if given, else the highest.
    private static bool GroundRaw(Vector3 pos, float k, out Vector3 result, out float water, out GroundFrom from, float? near = null)
    {
        result = pos;
        from = GroundFrom.Floor;
        water = float.NegativeInfinity;
        float? nav = null;
        try
        {
            var probe = pos;
            if (near.HasValue) probe.y = near.Value;
            if (NavMesh.SamplePosition(probe, out var nh, 0.3f * k, PlayerAreas()) &&
                new Vector2(nh.position.x - pos.x, nh.position.z - pos.z).magnitude < 0.15f * k)
                nav = nh.position.y;
        }
        catch { }
        var target = nav ?? near;
        var best = float.NegativeInfinity;
        var fallback = float.NegativeInfinity;
        try
        {
            var hits = Physics.RaycastAll(pos + Vector3.up * 0.4f * k, Vector3.down, 2f * k, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || IsCharacter(h.collider.transform)) continue;
                if (IsWater(h.collider)) { water = Mathf.Max(water, h.point.y); continue; }
                if (h.collider.isTrigger) continue;
                if (h.normal.y < 0.6f) continue;
                if (NotFloor(h.collider)) { if (h.point.y > fallback) fallback = h.point.y; continue; }
                if (target.HasValue)
                {
                    if (float.IsNegativeInfinity(best) || Mathf.Abs(h.point.y - target.Value) < Mathf.Abs(best - target.Value)) best = h.point.y;
                }
                else if (h.point.y > best) best = h.point.y;
            }
        }
        catch { }
        if (nav.HasValue && (float.IsNegativeInfinity(best) || Mathf.Abs(best - nav.Value) > 0.08f * k))
        {
            result.y = nav.Value;
            from = GroundFrom.NavMesh;
            return true;
        }
        if (float.IsNegativeInfinity(best))
        {
            if (float.IsNegativeInfinity(fallback)) return false;
            best = fallback;
            from = GroundFrom.SoundMesh;
        }
        result.y = best;
        return true;
    }

    // Debug: map meshes whose box holds her waist, and whether they have a collider (stairs that don't would be
    // invisible to the clearance check).
    private static void LogSurroundings(Watcher w, Vector3 spot, float k)
    {
        try
        {
            var waist = spot + Vector3.up * 0.9f * k;
            var found = new List<string>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (r == null || !r.enabled || IsCharacter(r.transform)) continue;
                var b = r.bounds;
                if (!b.Contains(waist) || b.size.magnitude > 60f * k) continue;
                var col = r.GetComponentInParent<Collider>() != null || r.GetComponentInChildren<Collider>() != null;
                found.Add($"{r.name}{(col ? "" : " (no collider)")}");
                if (found.Count >= 8) break;
            }
            var walk = NavMesh.SamplePosition(spot, out var nh, 0.15f * k, PlayerAreas()) ? $"player navmesh area {nh.mask:X}" : "off the player's navmesh";
            var any = NavMesh.SamplePosition(spot, out var ah, 0.15f * k, NavMesh.AllAreas) ? $"area {ah.mask:X}" : "no navmesh";
            Plugin.Debug($"{w.Name}: {walk} ({any}); meshes around her: {(found.Count > 0 ? string.Join(", ", found) : "none")}");
        }
        catch (Exception e) { Plugin.Debug($"{w.Name}: surroundings failed: {e.Message}"); }
    }

    // The navmesh areas the player may walk on. The map's girls also use areas he can't (stairs, for one), and
    // their parts of the navmesh run through stairs and props that have no colliders.
    private static int _playerAreas;

    internal static int PlayerAreas()
    {
        if (_playerAreas != 0) return _playerAreas;
        var mask = 0;
        try { mask = ExploreScene.Instance._propertyData._explore._navMesh._areaMask; } catch { }
        if (mask == 0) try { mask = ExploreScene.Instance.Player._agent.areaMask; } catch { }
        if (mask == 0) return NavMesh.AllAreas;
        _playerAreas = mask;
        var girls = 0;
        try { girls = ExploreScene.Instance._propertyData._explore._heroineNavMesh._areaMask; } catch { }
        Plugin.Debug($"Watchers stand where the player can walk: navmesh areas {mask:X} (the girls' {girls:X}).");
        return mask;
    }

    // Open floor: on the couple's level, on the player's walkable area when the map has one, and with nothing (walls,
    // stairs, boxes, benches) within 30 cm of her from the ankles up.
    // 2: open floor; 1: walkable but something stands close (a cramped spot, still better than nothing); 0: not
    // floor the player could walk on at the couple's level.
    internal static int Roomy(Vector3 spot, float floorY, float k) => Roomy(spot, floorY, k, out _);

    internal static int Roomy(Vector3 spot, float floorY, float k, out string why)
    {
        why = null;
        if (Mathf.Abs(spot.y - floorY) > 0.12f * k) { why = "not on the couple's level"; return 0; }
        try
        {
            var mask = PlayerAreas();
            if (NavMesh.SamplePosition(new Vector3(spot.x, floorY, spot.z), out _, 1f * k, mask) &&
                (!NavMesh.SamplePosition(spot, out var nh, 0.15f * k, mask) ||
                 new Vector2(nh.position.x - spot.x, nh.position.z - spot.z).magnitude > 0.1f * k))
            { why = "off the player's navmesh"; return 0; }
            var cols = Physics.OverlapCapsule(spot + Vector3.up * 0.38f * k, spot + Vector3.up * 1.3f * k, 0.3f * k, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
                if (c != null && !IsCharacter(c.transform) && !IsSound(c) && !IsWater(c) && !CameraOnly(c))
                { why = $"{c.name} ({LayerMask.LayerToName(c.gameObject.layer)})"; return 1; }
        }
        catch { }
        return 2;
    }

    // Last try before settling: pull each candidate spot onto the nearest floor the player can walk on.
    private static Vector3? SnapToWalkable(Watcher w, Vector3 center, float baseAngle, List<Vector3> bodies, float k, ref Vector3? cramped)
    {
        var mask = PlayerAreas();
        if (mask == NavMesh.AllAreas) return null;
        foreach (var dist in DistanceTries)
            foreach (var off in AngleTries)
            {
                var dir = Quaternion.Euler(0, baseAngle + off, 0) * _ringDir;
                var want = center + dir * Plugin.Distance.Value * dist * k;
                if (!NavMesh.SamplePosition(want, out var nh, 1.2f * k, mask)) continue;
                var pos = nh.position;
                if (Ground(pos, k, out var g, out _, out _, center.y)) pos.y = g.y;
                if (!InSight(center, pos, k) || !IsClear(w, pos, bodies, k)) continue;
                var room = Roomy(pos, center.y, k);
                if (room == 2) { Plugin.Debug($"{w.Name}: moved onto the walkable floor nearby."); return pos; }
                if (room == 1) cramped ??= pos;
            }
        return null;
    }

    // Invisible volumes that aren't things: camera blockers, map areas and event zones.
    private static readonly string[] NotSolidLayers = { "camera", "area", "event", "explore", "hscene", "ui", "ignore" };

    private static bool CameraOnly(Collider c)
    {
        try
        {
            var layer = LayerMask.LayerToName(c.gameObject.layer) ?? "";
            return NotSolidLayers.Any(x => layer.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch { return false; }
    }

    // Stairs, ramps and edges: the floor around the spot isn't at one height. A girl there floats over the steps.
    private static bool Level(Vector3 spot, float k)
    {
        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            if (!Ground(spot + d * 0.2f * k, k, out var g, out _, out _, spot.y)) continue;
            if (Mathf.Abs(g.y - spot.y) > 0.04f * k) return false;
        }
        return true;
    }

    // How far the footstep mesh and the navmesh sit above the floor under the couple, whose feet are on it.
    private static void MeasureGroundOffsets(Vector3 floor, float k)
    {
        _soundOffset = _navOffset = 0f;
        try
        {
            var hits = Physics.RaycastAll(floor + Vector3.up * 0.4f * k, Vector3.down, 0.8f * k, ~0, QueryTriggerInteraction.Collide);
            var sound = float.NegativeInfinity;
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.isTrigger || h.normal.y < 0.6f || IsCharacter(h.collider.transform)) continue;
                if (NotFloor(h.collider) && h.point.y > sound) sound = h.point.y;
            }
            var d = sound - floor.y;
            if (d > 0f && d < 0.3f * k) _soundOffset = d;
            if (NavMesh.SamplePosition(floor, out var nh, 0.3f * k, NavMesh.AllAreas))
            {
                d = nh.position.y - floor.y;
                if (Mathf.Abs(d) < 0.3f * k) _navOffset = d;
            }
        }
        catch { }
        Plugin.Debug($"Footstep mesh sits {_soundOffset / k * 100f:0} cm and the navmesh {_navOffset / k * 100f:0} cm above the floor.");
    }

    private static bool InSight(Vector3 from, Vector3 to, float k) =>
        ClearLine(from + Vector3.up * 0.9f * k, to + Vector3.up * 0.9f * k) || ClearLine(from + Vector3.up * 1.45f * k, to + Vector3.up * 1.45f * k);

    private static bool ClearLine(Vector3 a, Vector3 b)
    {
        var d = b - a;
        var len = d.magnitude;
        if (len < 0.01f) return true;
        try
        {
            var hits = Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
                if (hits[i].collider != null && !IsCharacter(hits[i].collider.transform) && !IsSound(hits[i].collider)) return false;
        }
        catch { }
        return true;
    }

    private static readonly Dictionary<int, bool> _notFloor = new();

    private static bool NotFloor(Collider c)
    {
        var id = c.GetInstanceID();
        if (_notFloor.TryGetValue(id, out var known)) return known;
        var result = false;
        try
        {
            var layer = LayerMask.LayerToName(c.gameObject.layer) ?? "";
            result = layer == "SE" || layer.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (c.name ?? "").StartsWith("se_", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        _notFloor[id] = result;
        return result;
    }

    private static bool IsSound(Collider c)
    {
        try { return LayerMask.LayerToName(c.gameObject.layer) == "SE" || (c.name ?? "").StartsWith("se_", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static readonly Dictionary<int, bool> _water = new();

    private static bool IsWater(Collider c)
    {
        var id = c.GetInstanceID();
        if (_water.TryGetValue(id, out var known)) return known;
        var result = false;
        try
        {
            var layer = LayerMask.LayerToName(c.gameObject.layer) ?? "";
            var name = (c.name ?? "").ToLowerInvariant();
            result = layer.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     new[] { "water", "mizu", "umi", "sea", "pool", "suimen", "ocean", "river", "lake" }.Any(name.Contains);
        }
        catch { }
        _water[id] = result;
        return result;
    }

    internal static void LogGround(Vector3 pos, float k, string what)
    {
        if (!Plugin.DebugLog.Value) return;
        try
        {
            var hits = Physics.RaycastAll(pos + Vector3.up * 1f * k, Vector3.down, 4f * k, ~0, QueryTriggerInteraction.Collide);
            var parts = new List<string>();
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || IsCharacter(h.collider.transform)) continue;
                parts.Add($"{h.collider.name} (layer {LayerMask.LayerToName(h.collider.gameObject.layer)}, {(h.collider.isTrigger ? "trigger, " : "")}" +
                          $"{(h.point.y - pos.y) / k:+0.00;-0.00} m{(IsWater(h.collider) ? ", water" : "")})");
            }
            Plugin.Debug($"Ground under {what}: {(parts.Count > 0 ? string.Join("; ", parts) : "nothing")}");
        }
        catch { }
    }

    private static bool IsCharacter(Transform t)
    {
        for (var i = 0; t != null && i < 40; i++, t = t.parent)
        {
            var n = t.name;
            if (n.StartsWith("chaF") || n.StartsWith("chaM") || n.StartsWith("p_cf_") || n.StartsWith("p_cm_")) return true;
        }
        return false;
    }

    private static bool IsClear(Watcher w, Vector3 pos, List<Vector3> bodies, float k)
    {
        var room = (w.Lover != null ? 1.0f : 0.55f) * k;
        foreach (var b in bodies)
        {
            if (b.y - pos.y > 2.2f * k) continue;
            var dx = b.x - pos.x; var dz = b.z - pos.z;
            if (dx * dx + dz * dz < room * room) return false;
        }
        foreach (var o in Present)
        {
            if (o == w || o == w.Lover) continue;
            var d = o.Human.transform.position - pos; d.y = 0;
            var need = o.Lover != null || w.Lover != null ? 1.3f : 0.7f;
            if (d.magnitude < need * k) return false;
        }
        return true;
    }

    private static List<Vector3> ActorBones(HScene scene)
    {
        var list = new List<Vector3>();
        foreach (var a in ActiveActors(scene))
        {
            var h = a.Human;
            if (!_actorBones.TryGetValue(h.Pointer, out var bones) || bones.Length == 0 || bones[0] == null)
            {
                Transform root = null;
                try { root = h.body?._trfBodyBone; } catch { }
                var found = new List<Transform>();
                foreach (var t in (root ?? h.transform).GetComponentsInChildren<Transform>(true))
                    if (t.name.IndexOf("_j_", StringComparison.OrdinalIgnoreCase) >= 0) found.Add(t);
                _actorBones[h.Pointer] = bones = found.ToArray();
            }
            foreach (var t in bones) if (t != null) list.Add(t.position);
        }
        return list;
    }

    // Keeps the watchers with the couple: if the scene moves they follow, and if someone ends up where a watcher
    // stands, she steps aside. A watcher the camera ends up inside is hidden meanwhile.
    private static void Arrange(HScene scene)
    {
        var present = Present.ToList();
        if (present.Count == 0) return;
        var k = Scale(scene);
        var center = SceneCenter(scene);
        if (_anchored && (center - _anchor).magnitude > 1.2f * k)
        {
            Plugin.Debug("The scene moved; the watchers follow.");
            _anchor = center;
            var floor = center; floor.y = SceneFloor(scene, center, k);
            LogGround(floor, k, "the scene");
            MeasureGroundOffsets(floor, k);
            _ringDir = Vector3.zero;
            foreach (var w in present) { w.Pinned = false; w.Human.transform.position = center + Vector3.down * 100f * k; }
            foreach (var w in present)
            {
                if (w.Lover != null && !w.LoverLeads) continue;
                Place(scene, w);
                if (w.Lover == null) LookAtScene(scene, w);
            }
        }
        else
        {
            var bodies = ActorBones(scene);
            foreach (var w in present)
                if (!w.Pinned && (w.Lover == null || w.LoverLeads) && !IsClear(w, w.Human.transform.position, bodies, k))
                {
                    Plugin.Debug($"{w.Name} is in the way; she steps aside.");
                    Place(scene, w);
                }
        }

        var camera = Camera(scene);
        var cam = camera != null ? camera.transform.position : Vector3.zero;
        foreach (var w in present)
        {
            var p = w.Human.transform.position;
            var d = cam - p; var up = d.y; d.y = 0;
            var inside = camera != null && d.magnitude < 0.45f * k && up > -0.2f * k && up < 1.9f * k;
            if (inside == w.Hidden) continue;
            w.Hidden = inside;
            try { w.Human.visibleAll = !inside; w.Human.UpdateVisible(); } catch { }
        }
    }

    // Units per meter, from a girl in the scene: the length of her bone chain from head to ankle, which doesn't
    // change with the pose (about 1.32 m on a ~1.55 m girl).
    internal static float Scale(HScene scene)
    {
        if (_scale > 0) return _scale;
        try
        {
            foreach (var a in ActiveActors(scene))
            {
                if (a.Sex != 1) continue;
                var chain = Chain(FindBone(a.Human, "cf_j_head"), "cf_j_hips") + Chain(FindBone(a.Human, "cf_j_foot_L"), "cf_j_hips");
                if (chain > 0.3f && chain < 50f)
                {
                    _scale = chain / 1.32f;
                    Plugin.Debug($"World scale: {_scale:0.00} units per meter (head to ankle {chain:0.00} units).");
                    return _scale;
                }
            }
        }
        catch { }
        return 1f;
    }

    // Length along the bones from t up to the named ancestor (0 if it isn't one).
    private static float Chain(Transform t, string ancestor)
    {
        var len = 0f;
        while (t != null && t.parent != null)
        {
            if (t.name == ancestor) return len;
            len += (t.position - t.parent.position).magnitude;
            t = t.parent;
        }
        return t != null && t.name == ancestor ? len : 0f;
    }

    internal static Vector3 SceneCenter(HScene scene)
    {
        var sum = Vector3.zero; var n = 0;
        foreach (var a in ActiveActors(scene)) { sum += a.Human.transform.position; n++; }
        return n > 0 ? sum / n : Vector3.zero;
    }

    internal static void LookAtScene(HScene scene, Watcher w)
    {
        Transform target = null;
        foreach (var a in ActiveActors(scene))
            if (a.Sex == 1) { target = a.Human.face?.objHeadBone?.transform ?? a.Human.transform; break; }
        if (target != null) Gaze(w, target);
    }

    internal static void Gaze(Watcher w, Transform target, bool neckFromAnimation = false)
    {
        try
        {
            var face = w.Human.face;
            var eyes = face.eyeLookCtrl;
            if (eyes != null)
            {
                eyes.Target = target;
                var states = eyes.EyeLookScript?.EyeTypeStates;
                for (var i = 0; states != null && i < states.Length; i++)
                    if (states[i] != null && states[i].LookType == ILLGAMES.Unity.Animations.EYE_LOOK_TYPE.TARGET) { eyes.PtnNo = i; break; }
            }
            var neck = face.neckLookCtrl;
            if (neck != null)
            {
                neck.Target = target;
                var want = neckFromAnimation ? ILLGAMES.Unity.Animations.NECK_LOOK_TYPE_VER2.ANIMATION : ILLGAMES.Unity.Animations.NECK_LOOK_TYPE_VER2.TARGET;
                var states = neck.NeckLookScript?.neckTypeStates;
                for (var i = 0; states != null && i < states.Length; i++)
                    if (states[i] != null && states[i].lookType == want) { neck.PtnNo = i; break; }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: gaze setup failed: {e.Message}"); }
    }

    // ---------- click menu ----------

    public static void PickAt(Vector2 mouse)
    {
        var cam = _scene != null ? Camera(_scene) : null;
        if (cam == null) return;
        Watcher best = null;
        var bestDepth = float.MaxValue;
        var k = Scale(_scene);
        foreach (var w in Present)
        {
            try
            {
                var feet = cam.WorldToScreenPoint(w.Human.transform.position);
                var head = cam.WorldToScreenPoint(w.Human.transform.position + Vector3.up * 1.7f * k);
                if (feet.z <= 0 || head.z <= 0) continue;
                var height = Mathf.Abs(head.y - feet.y);
                var half = Mathf.Max(height * 0.2f, 20f);
                var cx = (feet.x + head.x) * 0.5f;
                if (mouse.x < cx - half || mouse.x > cx + half) continue;
                if (mouse.y < Mathf.Min(feet.y, head.y) || mouse.y > Mathf.Max(feet.y, head.y)) continue;
                if (feet.z < bestDepth) { bestDepth = feet.z; best = w; }
            }
            catch { }
        }
        if (best != null) Selected = Selected == best ? null : best;
    }

    public static void Redress(Watcher w, ChaFileDefine.ClothesState state)
    {
        var cloth = w.Human?.cloth;
        if (cloth == null) return;
        foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
            try
            {
                if (cloth.IsClothesStateKind(kind) && cloth.IsExist(kind) && cloth.IsClothesStateType(kind, state))
                    cloth.SetClothesState(kind, state, true);
            }
            catch { }
    }

    public static void CyclePart(Watcher w, ChaFileDefine.ClothesKind kind)
    {
        var cloth = w.Human?.cloth;
        if (cloth == null) return;
        try
        {
            var state = cloth.GetClothesStateType(kind);
            for (var i = 1; i <= 3; i++)
            {
                var next = (ChaFileDefine.ClothesState)(((int)state + i) % 3);
                if (!cloth.IsClothesStateType(kind, next)) continue;
                cloth.SetClothesState(kind, next, true);
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: clothes change failed: {e.Message}"); }
    }

    public static int Outfit(Watcher w)
    {
        try { return (int)w.Human.fileStatus.coordinateType; } catch { return -1; }
    }

    public static void SetOutfit(Watcher w, int type)
    {
        try
        {
            if (Outfit(w) == type) return;
            w.Human.coorde.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)type, true);
            w.Pose = null;
            w.NextPoseAt = 0f;
            if (_scene != null) LookAtScene(_scene, w);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: outfit change failed: {e.Message}"); }
    }

    // The copy starts in her card's default outfit; dress her like the girl walking the map.
    private static void MatchOutfit(Watcher w)
    {
        try
        {
            var original = w.Npc?.Params?.Chara;
            if (original == null) return;
            var type = (int)original.fileStatus.coordinateType;
            if (Outfit(w) != type) w.Human.coorde.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)type, true);
            // Clothes she had half off or off on the map stay that way.
            var cloth = original.cloth;
            foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                try
                {
                    var s = cloth.GetClothesStateType(kind);
                    if (s != ChaFileDefine.ClothesState.Clothing && w.Human.cloth.IsClothesStateType(kind, s)) w.Human.cloth.SetClothesState(kind, s, true);
                }
                catch { }
        }
        catch { }
    }

    // ---------- helpers ----------

    internal static List<HActor> ActiveActors(HScene scene)
    {
        var list = new List<HActor>();
        try
        {
            var actors = scene._hActorAll;
            for (var i = 0; actors != null && i < actors.Length; i++)
            {
                var a = actors[i];
                if (a?.Human?.gameObject == null || !a.Human.gameObject.activeInHierarchy) continue;
                bool visible;
                try { visible = a.IsVisible(); } catch { visible = true; }
                if (visible) list.Add(a);
            }
        }
        catch { }
        return list;
    }

    private static bool AnyActive(HScene scene) => ActiveActors(scene).Count > 0;

    // The save data of the people in the scene; with activeOnly, only those shown right now.
    internal static HashSet<IntPtr> ActorDataKeys(HScene scene, bool activeOnly)
    {
        var keys = new HashSet<IntPtr>();
        try
        {
            if (activeOnly) { foreach (var a in ActiveActors(scene)) if (a.ActorData != null) keys.Add(Swaps.KeyOf(a)); }
            else
            {
                var actors = scene._hActorAll;
                for (var i = 0; actors != null && i < actors.Length; i++)
                    if (actors[i]?.ActorData != null) keys.Add(Swaps.KeyOf(actors[i]));
            }
        }
        catch { }
        return keys;
    }

    internal static Camera Camera(HScene scene)
    {
        try { return scene._mainCamera ?? UnityEngine.Camera.main; } catch { return UnityEngine.Camera.main; }
    }

    // Watchers drawn on the same layer as the people in the scene, so the H camera shows them.
    private static void LayerLikeActors(HScene scene, Human h)
    {
        try
        {
            var actor = ActiveActors(scene).FirstOrDefault(a => a.Sex == 1);
            var r = actor?.Human?.body?.rendBody;
            if (r == null) return;
            var layer = r.gameObject.layer;
            var mine = h.body?.rendBody;
            if (mine != null && mine.gameObject.layer != layer)
            {
                foreach (var t in h.gameObject.GetComponentsInChildren<Renderer>(true)) t.gameObject.layer = layer;
                Plugin.Debug($"Watcher renderers moved to layer {layer}.");
            }
        }
        catch { }
    }

    private static readonly Dictionary<string, RuntimeAnimatorController> _controllers = new();

    internal static RuntimeAnimatorController LoadController(string bundle, string asset, bool quiet = false)
    {
        try
        {
            if (_controllers.TryGetValue(asset, out var cached) && cached != null) return cached;
            var obj = AssetBundleManager.LoadAssetSimple(Il2CppType.Of<RuntimeAnimatorController>(), bundle, asset, null);
            var rac = obj?.TryCast<RuntimeAnimatorController>();
            if (rac != null) _controllers[asset] = rac;
            else if (!quiet) Plugin.Log.LogWarning($"Animator {asset} not found in {bundle}.");
            return rac;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not load the {asset} animator: {e.Message}");
            return null;
        }
    }

    internal static Transform FindBone(Human h, string name)
    {
        try
        {
            Transform root = null;
            try { root = h.body?._trfBodyBone; } catch { }
            foreach (var t in (root ?? h.transform).GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        }
        catch { }
        return null;
    }

    // H animations come in three sizes, picked by the girl's height.
    internal static string Size(Watcher w) => Size(w.Human);

    internal static string Size(Human h)
    {
        try
        {
            var c = h.body.GetHeightCategory();
            return c <= 0 ? "S" : c >= 2 ? "L" : "M";
        }
        catch { return "M"; }
    }

    internal static string SafeName(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }
}
