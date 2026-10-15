using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace AC_HWatchers;

// Girls who were near when H started gather around the scene and watch, reacting as it goes on.
[BepInProcess("Aicomi")]
[BepInPlugin(GUID, "AC_HWatchers", Version)]
public class Plugin : BasePlugin
{
    public const string GUID = "AC_HWatchers";
    public const string Version = "1.0.0";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> AutoBring;
    internal static ConfigEntry<int> MaxWatchers;
    internal static ConfigEntry<float> Reach;
    internal static ConfigEntry<float> Distance;
    internal static ConfigEntry<KeyCode> WatcherKey;
    internal static ConfigEntry<float> ExcitementSpeed;
    internal static ConfigEntry<bool> Masturbate;
    internal static ConfigEntry<bool> GirlPairs;
    internal static ConfigEntry<bool> WatchersJoin;
    internal static ConfigEntry<bool> Swaps;
    internal static ConfigEntry<bool> FiveP;
    internal static ConfigEntry<bool> FivePAll;
    internal static ConfigEntry<int> JoinExperience;
    internal static ConfigEntry<bool> SharedExperience;
    internal static ConfigEntry<bool> DebugLog;

    internal static void Debug(string message)
    {
        if (DebugLog != null && DebugLog.Value) Log.LogInfo(message);
    }

    public override void Load()
    {
        Log = base.Log;
        AutoBring = Config.Bind("Watchers", "Bring Watchers Automatically", true,
            "When H starts, the girls who were nearest come to watch.");
        MaxWatchers = Config.Bind("Watchers", "Maximum Watchers", 3,
            new ConfigDescription("How many girls can watch at once.", new AcceptableValueRange<int>(1, 8)));
        Reach = Config.Bind("Watchers", "Reach (m)", 25f,
            new ConfigDescription("Only girls this close to where H started come to watch, in meters.", new AcceptableValueRange<float>(5f, 200f)));
        Distance = Config.Bind("Watchers", "Distance (m)", 1.8f,
            new ConfigDescription("How far from the couple the watchers stand, in meters.", new AcceptableValueRange<float>(0.8f, 5f)));
        ExcitementSpeed = Config.Bind("Watchers", "Excitement Speed", 1f,
            new ConfigDescription("How quickly watchers get worked up as the scene goes on.", new AcceptableValueRange<float>(0.25f, 4f)));
        Masturbate = Config.Bind("Watchers", "Masturbation", true,
            "Worked-up watchers touch themselves. Never for virgins.");
        GirlPairs = Config.Bind("Watchers", "Girls Make Love", true,
            "Two worked-up watchers who both like girls make love with each other. Never for virgins.");
        WatchersJoin = Config.Bind("Watchers", "Watchers Join 3P", true,
            "The game's 3P button brings one of the watchers (not a virgin, with enough experience) instead of any girl nearby.");
        Swaps = Config.Bind("Watchers", "Swaps", true,
            "In a 3P, a watcher (not a virgin, with enough experience) can take the place of either girl from her menu, and the menu can pick who joins with the 3P button.");
        FiveP = Config.Bind("Watchers", "Watchers 5P", true,
            "Experimental: when three or more watchers are experienced enough, a watcher's menu can turn the 3P button into a 5P.");
        FivePAll = Config.Bind("Watchers", "All Positions In 5P", true,
            "Experimental: a 5P scene also lists the two- and three-person positions; the girls not in the position wait and watch.");
        JoinExperience = Config.Bind("Watchers", "Experience To Join", 50,
            new ConfigDescription("H experience a watcher needs (out of 100) to join a 3P or 5P or take a girl's place. The game's own 3P starts at 50.", new AcceptableValueRange<int>(0, 100)));
        SharedExperience = Config.Bind("Watchers", "Experience For Every Girl", true,
            "Every girl who took part in H (swapped in, the 3P girl, the 5P girls) gains the same H experience, part experience and H count the game gives the main girl.");
        WatcherKey = Config.Bind("Keys", "Watcher Key", KeyCode.F9,
            "Bring one more watcher. Shift + this key sends all of them back.");
        DebugLog = Config.Bind("Debug", "Detailed Log", false, "Write every pose change and scene event to the BepInEx log.");
        try { Harmony.CreateAndPatchAll(typeof(Hooks), GUID); }
        catch (Exception e) { Log.LogError($"Hooks failed: {e}"); }
        try { Harmony.CreateAndPatchAll(typeof(FivePHooks), GUID + ".5p"); }
        catch (Exception e) { Log.LogWarning($"5P hooks failed: {e.Message}"); }
        try { Harmony.CreateAndPatchAll(typeof(SceneHooks), GUID + ".scene"); }
        catch (Exception e) { Log.LogWarning($"Scene hooks failed: {e.Message}"); }
        try { Harmony.CreateAndPatchAll(typeof(ExperienceHooks), GUID + ".exp"); }
        catch (Exception e) { Log.LogWarning($"Experience hooks failed: {e.Message}"); }
        WatcherMenu.Create();
        Log.LogInfo($"AC_HWatchers {Version} loaded. {WatcherKey.Value} brings a watcher, Shift+{WatcherKey.Value} sends them back, " +
                    "Ctrl + right-click a watcher for her menu.");
    }
}
