using UnityEngine;

namespace AC_HWatchers;

// Caresses are made for a man and a girl. The taller girl plays the man's part, and for the length of the caress
// both girls are scaled a little toward each other's height (at most a few percent each), with the animators told
// they are the same height, so hands and lips meet without either girl changing much.
internal static partial class Watchers
{
    private const float MaxEven = 0.06f;

    // Her body scale from her height slider: the height bone when the body has one, else the slider's usual range.
    private static float BodyScale(Watcher w)
    {
        try
        {
            var bone = FindBone(w.Human, "cf_n_height");
            if (bone != null && bone.localScale.y > 0.5f && bone.localScale.y < 1.5f) return bone.localScale.y;
        }
        catch { }
        try { return 0.9f + 0.2f * Mathf.Clamp01(w.Human.fileBody.shapeValueBody[0]); } catch { return 1f; }
    }

    private static float Slider(Watcher w)
    {
        try { return Mathf.Clamp01(w.Human.fileBody.shapeValueBody[0]); } catch { return 0.5f; }
    }

    internal static bool Taller(Watcher a, Watcher b) => BodyScale(a) > BodyScale(b) + 0.001f;

    private static void EvenHeights(Watcher a, Watcher b)
    {
        float sa = BodyScale(a), sb = BodyScale(b);
        var mid = (sa + sb) * 0.5f;
        var fa = Mathf.Clamp(mid / sa, 1f - MaxEven, 1f + MaxEven);
        var fb = Mathf.Clamp(mid / sb, 1f - MaxEven, 1f + MaxEven);
        // When the gap is too big to close, both still meet halfway and the animators are told the average.
        var blend = (Slider(a) + Slider(b)) * 0.5f;
        Rescale(a, fa, blend);
        Rescale(b, fb, blend);
        Plugin.Debug($"Caress heights: {a.Name} {sa:0.000} x{fa:0.000}, {b.Name} {sb:0.000} x{fb:0.000}, animators at {blend:0.00}.");
    }

    private static void Rescale(Watcher w, float f, float blend)
    {
        var t = w.Human?.transform;
        if (t == null) return;
        w.BaseScale ??= t.localScale;
        t.localScale = w.BaseScale.Value * f;
        w.BlendHeight = blend;
    }

    internal static void RestoreHeight(Watcher w)
    {
        w.BlendHeight = null;
        if (w.BaseScale == null) return;
        try { if (w.Human?.transform != null) w.Human.transform.localScale = w.BaseScale.Value; } catch { }
        w.BaseScale = null;
    }
}
