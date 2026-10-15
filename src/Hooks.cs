using AC.Scene;
using AC.Scene.Explore;
using H;
using HarmonyLib;

namespace AC_HWatchers;

// Never patch a method that returns UniTask (HScene.OnEnd, HScene.End, ChangeAnimation...): in Amanatsu such a
// patch closed the game to desktop. Only void and plain methods are hooked.
internal static class Hooks
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Dispose))]
    public static void HScene_Pre_Dispose()
    {
        try { Experience.SceneDisposing(Watchers.Scene); } catch (Exception e) { Plugin.Log.LogWarning($"Experience: {e.Message}"); }
        try { Tired.RememberPool(); } catch (Exception e) { Plugin.Log.LogWarning($"Tiredness: {e.Message}"); }
        try { FiveP.Remember(); FiveP.SceneEnded(); } catch (Exception e) { Plugin.Log.LogWarning($"5P: {e.Message}"); }
        // Bodies back first (the 3P button ends the scene without IsEnded), then the scene's own data.
        try { if (Watchers.Scene != null) Swaps.EndCheck(Watchers.Scene, true); } catch (Exception e) { Plugin.Log.LogError($"Swap: giving bodies back failed: {e}"); }
        try { Swaps.RestoreAll(Watchers.Scene, false); } catch (Exception e) { Plugin.Log.LogError($"Swap cleanup failed: {e}"); }
        try { Watchers.Clear("H ended"); Invite.Reset(); }
        catch (Exception e) { Plugin.Log.LogError($"Watcher cleanup failed: {e}"); }
    }
}

// 5P scenes list every position: the game asks the scene whether it has the people a position category needs.
internal static class FivePHooks
{
    private static readonly HashSet<int> _logged = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.IsParticipants))]
    public static void HScene_Post_IsParticipants(HScene __instance, Define.PostureCategory postureCategory, ref bool __result)
    {
        try
        {
            var is5P = __instance._inputParameter?.Is5P == true;
            if (!is5P) { _logged.Clear(); return; }
            var was = __result;
            if (!__result && Plugin.FivePAll.Value && postureCategory != Define.PostureCategory.M2F1) __result = true;
            if (_logged.Add((int)postureCategory)) Plugin.Debug($"5P: positions {postureCategory}: game says {was}, listed {__result}.");
        }
        catch { }
    }
}

// The start of H and the game's choice of the 3P girl.
internal static class SceneHooks
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.InitializeParameter))]
    public static void HScene_Pre_InitializeParameter(HScene.InputParameter parameter)
    {
        try { FiveP.AtSceneStart(parameter); } catch (Exception e) { Plugin.Log.LogWarning($"5P: {e.Message}"); }
        try { Report.LogInput(parameter); } catch (Exception e) { Plugin.Log.LogWarning($"InitializeParameter log: {e.Message}"); }
        try { Swaps.AtSceneStart(parameter); } catch (Exception e) { Plugin.Log.LogWarning($"3P pick: {e.Message}"); }
        try { Tired.AtSceneStart(parameter); } catch (Exception e) { Plugin.Log.LogWarning($"Tiredness: {e.Message}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ExploreScene), nameof(ExploreScene.GetHSceneIntrusions))]
    public static void Explore_Post_GetHSceneIntrusions(Il2CppSystem.Collections.Generic.List<Actor> candidates, Actor target)
    {
        try { Report.LogIntrusions(candidates, target); } catch (Exception e) { Plugin.Log.LogWarning($"Intrusions log: {e.Message}"); }
        try { Invite.Narrow(candidates, target); } catch (Exception e) { Plugin.Log.LogWarning($"3P candidates: {e.Message}"); }
    }
}
