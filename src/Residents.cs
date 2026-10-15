using AC.Scene;
using AC.User;

namespace AC_HWatchers;

// The girls living on the map: the usual residents (NPC) and the "other residents" (UniqueNPC, such as Hanamori
// Natsume, Yoshizawa Yuri and Nagakura Shiori). Both are explore actors with save data; the player is neither.
internal static class Residents
{
    public static ActorData Girl(ActorData d)
    {
        try
        {
            if (d == null || d.HumanData == null) return null;
            if (d.TryCast<NPCData>() == null && d.TryCast<UniqueNPCData>() == null) return null;
            return d.HumanData.Parameter.sex == 1 ? d : null;
        }
        catch { return null; }
    }

    // The map area (room) H is in: where the scene's girls stand on the map. -1 when it can't be told.
    public static int SceneArea(H.HScene scene, ExploreScene explore)
    {
        try
        {
            var all = All(explore);
            foreach (var a in Watchers.ActiveActors(scene))
            {
                var key = a.ActorData?.Pointer ?? IntPtr.Zero;
                if (key == IntPtr.Zero) continue;
                var m = all.FirstOrDefault(x => x.BaseData != null && x.BaseData.Pointer == key);
                if (m != null) return m.AreaID;
            }
        }
        catch { }
        return -1;
    }

    // Only girls in the same area (room) as the scene come: a girl behind a wall in the next room doesn't.
    public static bool SameArea(NPC npc, int area)
    {
        if (area < 0) return true;
        try { return npc.AreaID == area; } catch { return true; }
    }

    public static List<NPC> All(ExploreScene explore)
    {
        var result = new List<NPC>();
        if (explore == null) return result;
        try
        {
            var list = explore.NPCList;
            for (var i = 0; i < list.Count; i++) if (list[i] != null) result.Add(list[i]);
        }
        catch { }
        try
        {
            var uniques = explore._uniqueNPCList;
            if (uniques != null)
                foreach (var key in uniques.Keys)
                    if (uniques.TryGetValue(key, out var u) && u != null && u.Transform != null && u.Transform.gameObject.activeInHierarchy) result.Add(u);
        }
        catch (Exception e) { Plugin.Debug($"Other residents: {e.Message}"); }
        return result.Where(a => Girl(a.BaseData) != null).ToList();
    }
}
