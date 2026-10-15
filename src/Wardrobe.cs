using Character;
using H;
using UnityEngine;

namespace AC_HWatchers;

// What each girl had on (outfit and every piece's state) when she last changed places, so she keeps it wherever she
// goes: a watcher who swaps in wears what she had watching, the girl going out what she had in H, and the 3P girl
// joins dressed as she was watching and keeps what she had in the scene when she goes back to watching.
// Kept until the player is back on the map (it lives through the 3P button's restart).
internal static class Wardrobe
{
    private sealed class Worn
    {
        public int Coord = -1;
        public Dictionary<ChaFileDefine.ClothesKind, ChaFileDefine.ClothesState> Pieces = new();
    }

    private static readonly Dictionary<IntPtr, Worn> _worn = new();
    private static readonly Dictionary<IntPtr, float> _dressOnJoin = new();
    private static HashSet<IntPtr> _wasActive = new();
    private static float _noSceneSince = -1f;

    public static void Remember(IntPtr key, Human h)
    {
        if (key == IntPtr.Zero || h?.cloth == null) return;
        var worn = new Worn();
        try { worn.Coord = h.fileStatus.coordinateType; } catch { }
        try
        {
            foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                if (h.cloth.IsClothesStateKind(kind)) worn.Pieces[kind] = h.cloth.GetClothesStateType(kind);
        }
        catch { }
        if (worn.Pieces.Count == 0) return;
        _worn[key] = worn;
        Plugin.Debug($"Clothes kept for {Report.Name(h.data)}: outfit {worn.Coord}, {string.Join(", ", worn.Pieces.Select(kv => $"{kv.Key}={kv.Value}"))}.");
    }

    public static bool TryGet(IntPtr key, out Dictionary<ChaFileDefine.ClothesKind, ChaFileDefine.ClothesState> pieces, out int coord)
    {
        pieces = null; coord = -1;
        if (!_worn.TryGetValue(key, out var w)) return false;
        pieces = new Dictionary<ChaFileDefine.ClothesKind, ChaFileDefine.ClothesState>(w.Pieces);
        coord = w.Coord;
        return true;
    }

    // Puts on what she had, piece by piece. A piece without that state (no half-off look) comes off instead.
    public static void Dress(Human h, IntPtr key)
    {
        if (h?.cloth == null || !_worn.TryGetValue(key, out var w)) return;
        foreach (var (kind, state) in w.Pieces)
        {
            try
            {
                if (!h.cloth.IsExist(kind)) continue;
                var st = state;
                if (st != ChaFileDefine.ClothesState.Clothing && !h.cloth.IsClothesStateType(kind, st))
                    st = h.cloth.IsClothesStateType(kind, ChaFileDefine.ClothesState.Naked) ? ChaFileDefine.ClothesState.Naked : ChaFileDefine.ClothesState.Clothing;
                if (h.cloth.GetClothesStateType(kind) != st) h.cloth.SetClothesState(kind, st, true);
            }
            catch { }
        }
    }

    // A watcher (her own copy of the card): the outfit first, then the pieces.
    public static void DressWatcher(Watcher w)
    {
        if (w?.Human == null || !_worn.TryGetValue(w.Key, out var worn)) return;
        if (worn.Coord >= 0 && Watchers.Outfit(w) != worn.Coord)
            try { w.Human.coorde.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)worn.Coord, true); } catch { }
        Dress(w.Human, w.Key);
        Plugin.Debug($"{w.Name} is dressed as she was last time (outfit {worn.Coord}).");
    }

    public static void Joining(IntPtr key) { if (_worn.ContainsKey(key)) _dressOnJoin[key] = -1f; }

    // Every few frames in the scene: the girls joining are dressed once the game has dressed them, and the girls who
    // just left the position (the 3P girl going back to waiting) have what they wore noted.
    public static void Tick(HScene scene)
    {
        _noSceneSince = -1f;
        var active = Watchers.ActorDataKeys(scene, true);
        try
        {
            var actors = scene._hActorAll;
            for (var i = 0; actors != null && i < actors.Length; i++)
            {
                var a = actors[i];
                if (a?.Human == null || a.Sex != 1 || a.ActorData == null) continue;
                var key = Swaps.KeyOf(a);
                if (_wasActive.Contains(key) && !active.Contains(key)) Remember(key, a.Human);
                if (active.Contains(key) && _dressOnJoin.TryGetValue(key, out var since))
                {
                    var now = Time.realtimeSinceStartup;
                    if (since < 0f) _dressOnJoin[key] = now;
                    else if (now - since >= 0.6f)
                    {
                        _dressOnJoin.Remove(key);
                        Dress(a.Human, key);
                        Plugin.Debug($"{Report.Name(a.Human.data)} joined dressed as she was watching.");
                    }
                }
            }
        }
        catch { }
        _wasActive = active;
    }

    // Back on the map for a few seconds: everything is forgotten.
    public static void NoScene()
    {
        if (_worn.Count == 0 && _dressOnJoin.Count == 0) return;
        if (_noSceneSince < 0f) { _noSceneSince = Time.realtimeSinceStartup; return; }
        if (Time.realtimeSinceStartup - _noSceneSince < 8f) return;
        _worn.Clear();
        _dressOnJoin.Clear();
        _wasActive.Clear();
        _noSceneSince = -1f;
    }
}
