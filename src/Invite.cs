using AC.Scene;
using AC.Scene.Explore;
using AC.User;
using UnityEngine;

namespace AC_HWatchers;

// The game's 3P button: when H starts, ExploreScene.GetHSceneIntrusions lists the girls nearby who may join, and
// pressing the button ends H and restarts it with one of them (5P takes more). The list is narrowed to the girls who
// will come to watch, so whoever joins was standing there watching. A watcher counts when she isn't a virgin and has
// enough experience (Plugin.JoinExperience, 50 by default, as the game's own 3P). The first one in it is who the button brings.
internal static class Invite
{
    // The game copies the list as soon as it's filled, so the order is fixed when H starts; this is that order.
    private static readonly List<IntPtr> _order = new();

    public static bool Eligible(ActorData d)
    {
        try { return d != null && !d.IsVirgin && d.Sexperience >= Plugin.JoinExperience.Value; } catch { return false; }
    }

    // Who will come to watch a scene starting at target: the nearest girls within reach, as Watchers picks them.
    private static List<NPC> FutureWatchers(Actor target)
    {
        var result = new List<NPC>();
        var explore = ExploreScene.Instance;
        if (explore == null || target == null) return result;
        var origin = target.Transform.position;
        var shown = Swaps.ShownKeys();
        var k = Watchers.LastScale;
        var all = new List<(NPC npc, float dist)>();
        var area = -1;
        try { area = target.AreaID; } catch { }
        var residents = Residents.All(explore).Where(n => Residents.SameArea(n, area)).ToList();
        for (var i = 0; i < residents.Count; i++)
        {
            var npc = residents[i];
            try
            {
                if (npc?.BaseData == null || npc.BaseData.Pointer == target.BaseData?.Pointer || shown.Contains(npc.BaseData.Pointer)) continue;
                if (Residents.Girl(npc.BaseData) == null) continue;
                var dist = (npc.Transform.position - origin).magnitude / k;
                if (dist <= Plugin.Reach.Value) all.Add((npc, dist));
            }
            catch { }
        }
        result.AddRange(all.OrderBy(x => x.dist).Take(Plugin.MaxWatchers.Value).Select(x => x.npc));
        return result;
    }

    public static void Narrow(Il2CppSystem.Collections.Generic.List<Actor> candidates, Actor target)
    {
        if (candidates == null || !Plugin.WatchersJoin.Value) return;
        var was = new List<string>();
        for (var i = 0; i < candidates.Count; i++) was.Add(Report.Name(candidates[i]?.BaseData?.HumanData));
        var watchers = FutureWatchers(target).Where(n => Eligible(Residents.Girl(n.BaseData))).ToList();
        var shown = Swaps.ShownKeys();
        for (var i = candidates.Count - 1; i >= 0; i--)
            if (candidates[i]?.BaseData != null && shown.Contains(candidates[i].BaseData.Pointer)) candidates.RemoveAt(i);
        if (watchers.Count == 0)
        {
            Plugin.Log.LogInfo($"No watcher can join a 3P (not a virgin, experience {Plugin.JoinExperience.Value}+); the game's own list stays: {string.Join(", ", was)}.");
            _order.Clear();
            for (var i = 0; i < candidates.Count; i++) if (candidates[i]?.BaseData != null) _order.Add(candidates[i].BaseData.Pointer);
            return;
        }
        var already = new HashSet<IntPtr>();
        for (var i = 0; i < candidates.Count; i++) if (candidates[i]?.BaseData != null) already.Add(candidates[i].BaseData.Pointer);
        // The game's own picks first (it may check more than we do), then the other eligible watchers.
        var ordered = watchers.OrderBy(n => already.Contains(n.BaseData.Pointer) ? 0 : 1).ToList();
        candidates.Clear();
        foreach (var n in ordered) candidates.Add(n);
        _order.Clear();
        _order.AddRange(ordered.Select(n => n.BaseData.Pointer));
        Plugin.Log.LogInfo($"3P candidates: {string.Join(", ", ordered.Select(n => Report.Name(n.BaseData.HumanData)))} (the game had: {string.Join(", ", was)}).");
    }

    public static bool CanJoin(Watcher w) => Eligible(w.Data) && _order.Contains(w.Key);

    public static bool Any => _order.Count > 0;

    public static bool IsFirst(Watcher w) => _order.Count > 0 && _order[0] == w.Key;

    // The scene the 3P button ended is gone by the time the 3P starts; its first pick is kept to recognise her.
    private static IntPtr _lastFirst;

    public static bool WasFirst(IntPtr key) => key != IntPtr.Zero && (key == _lastFirst || (_order.Count > 0 && _order[0] == key));

    public static void Reset()
    {
        if (_order.Count > 0) _lastFirst = _order[0];
        _order.Clear();
    }
}
