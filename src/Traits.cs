using AC.Scene;
using Character;

namespace AC_HWatchers;

// Character traits. "Womanizer" (女好き, likes girls) is the one the game's own girl-on-girl growth uses.
internal static class Traits
{
    // lib/g/scr/pp/ind/000_00 (individuality), by id. PropertyData.IndividualityID reads back shifted in this
    // interop (Womanizer came out as 1, Easy's id), so the ids are taken from the list itself.
    private static readonly string[] JapaneseNames =
    {
        "はらぺこ", "チョロイ", "読書好き", "奥手", "フレンドリー", "綺麗好き", "神出鬼没", "一人好き", "真面目",
        "コイバナ好き", "おしゃべり", "仲間意識", "のんびり", "寂しがり屋", "サボリ癖", "女の子好き", "エッチ好き", "むっつり"
    };
    private static readonly string[] EnglishNames =
    {
        "hungry", "easy", "bookworm", "shy", "friendly", "neat", "elusive", "loner", "serious",
        "love talk", "talkative", "team spirit", "laid-back", "lonely", "slacker", "likes girls", "likes sex", "secretly horny"
    };

    internal const int WomanizerIndex = 15;   // 女の子好き

    internal static bool LikesGirls(HumanData data)
    {
        try
        {
            var idx = WomanizerIndex;
            var flags = data.GameParameter.individuality;
            return idx >= 0 && flags != null && idx < flags.Length && flags[idx];
        }
        catch { return false; }
    }

    private static Dictionary<int, string> _names;

    internal static Dictionary<int, string> Names
    {
        get
        {
            if (_names != null) return _names;
            _names = new Dictionary<int, string>();
            for (var i = 0; i < EnglishNames.Length; i++) _names[i] = EnglishNames[i];
            return _names;
        }
    }

    internal static string NameList(HumanData data)
    {
        try
        {
            var flags = data.GameParameter.individuality;
            var on = new List<string>();
            for (var i = 0; flags != null && i < flags.Length; i++)
                if (flags[i]) on.Add(Names.TryGetValue(i, out var n) ? n : i.ToString());
            return string.Join(", ", on);
        }
        catch { return "?"; }
    }

    internal static string List(HumanData data)
    {
        try
        {
            var flags = data.GameParameter.individuality;
            var on = new List<int>();
            for (var i = 0; flags != null && i < flags.Length; i++) if (flags[i]) on.Add(i);
            return string.Join(",", on);
        }
        catch { return "?"; }
    }
}
