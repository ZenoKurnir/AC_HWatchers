using H;
using HarmonyLib;
using UnityEngine;

namespace AC_HWatchers;

// The game's move panel (the position button on the left in H) moves the whole scene. While a watcher's menu is
// open and the panel is open, its moves go to her instead (with her partner, when she is making love), and she
// stays where she was put. Each frame the scene's base is checked: a move of it goes to her and the base is put
// back. HScene.SetPosition is not hooked: its struct arguments (Nullable<float>) crash Il2CppInterop's trampoline,
// and every H start goes through it.
internal static class Moving
{
    private static Watcher _who;
    private static Transform _base;
    private static Vector3 _basePos;
    private static Quaternion _baseRot;
    private static bool _wasOpen;
    private static int _caught, _restored;
    // Where the scene's own people stood when the panel started moving her: the panel moves them along with the
    // base, so they are put back too.
    private static readonly List<(Transform t, Vector3 pos, Quaternion rot)> _people = new();

    private static void Snapshot(HScene scene)
    {
        _people.Clear();
        foreach (var a in Watchers.ActiveActors(scene))
            try { var t = a.Human?.transform; if (t != null) _people.Add((t, t.position, t.rotation)); } catch { }
    }

    private static bool PutPeopleBack()
    {
        var moved = false;
        foreach (var (t, pos, rot) in _people)
            try
            {
                if (t == null) continue;
                if ((t.position - pos).sqrMagnitude < 1e-8f && Quaternion.Angle(t.rotation, rot) < 0.01f) continue;
                t.SetPositionAndRotation(pos, rot);
                moved = true;
            }
            catch { }
        return moved;
    }

    private static Watcher Target
    {
        get
        {
            var w = Watchers.Selected;
            if (w?.Human == null) return null;
            return w.Lover != null && !w.LoverLeads ? w.Lover : w;
        }
    }

    internal static bool PanelOpen(HScene scene)
    {
        try
        {
            var p = scene?._sprite?._freeMovePanel;
            return p != null && p.gameObject.activeInHierarchy;
        }
        catch { return false; }
    }

    internal static bool Active(HScene scene) => Target != null && PanelOpen(scene);

    // Every frame while the scene runs.
    internal static void Tick(HScene scene)
    {
        var open = PanelOpen(scene);
        if (open != _wasOpen)
        {
            _wasOpen = open;
            Plugin.Debug($"Move panel {(open ? "open" : "closed")}{(open && Target != null ? $"; it moves {Target.Name}" : "")}.");
        }
        var target = open ? Target : null;
        if (target == null) { _who = null; _base = null; _people.Clear(); return; }
        Transform b = null;
        try { b = scene.BasePosition; } catch { }
        if (b == null) return;
        if (_who != target || _base == null || _base.Pointer != b.Pointer)
        {
            _who = target; _base = b;
            _basePos = b.position; _baseRot = b.rotation;
            Snapshot(scene);
            return;
        }
        if ((b.position - _basePos).sqrMagnitude < 1e-8f && Quaternion.Angle(b.rotation, _baseRot) < 0.01f)
        {
            // The scene's people moved without the base: the panel moved them directly; she isn't moved for it.
            if (PutPeopleBack() && _restored++ < 3) Plugin.Debug("Move panel moved the scene's people; they were put back.");
            return;
        }
        Give(target, b.position, b.rotation);
        b.SetPositionAndRotation(_basePos, _baseRot);
        PutPeopleBack();
        if (_restored++ < 3) Plugin.Debug("Move panel moved the scene's base; the move went to the watcher and the base was put back.");
    }

    // The panel asks for the scene's base at pos/rot: she moves by the same amount instead.
    internal static bool Intercept(HScene scene, Vector3 pos, Quaternion rot)
    {
        if (!Active(scene)) return false;
        var target = Target;
        Transform b = null;
        try { b = scene.BasePosition; } catch { }
        if (b == null) return false;
        _who = target; _base = b;
        _basePos = b.position; _baseRot = b.rotation;
        Give(target, pos, rot);
        if (_caught++ < 3) Plugin.Debug($"Move panel asked the scene to move; {target.Name} moves instead.");
        return true;
    }

    private static void Give(Watcher target, Vector3 pos, Quaternion rot)
    {
        var shift = pos - _basePos;
        var turn = rot * Quaternion.Inverse(_baseRot);
        foreach (var w in new[] { target, target.Lover })
        {
            if (w?.Human == null) continue;
            var t = w.Human.transform;
            t.SetPositionAndRotation(t.position + shift, turn * t.rotation);
            w.Pinned = true;
        }
        _basePos = pos; _baseRot = rot;
    }
}
