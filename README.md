# AC_HWatchers

[日本語版はこちら (Japanese)](README.ja.md)

Other girls watch your H scenes in **Aicomi**. This is a BepInEx 6 (IL2CPP) plugin.

When H starts, the nearest girls in the same area gather around and watch. As the scene goes on they get worked up, touch themselves and make love with each other. Experienced watchers can join a 3P or a 5P, or take a girl's place in the scene.

## Features

- **Watchers:** the nearest girls in the same room or area come to watch, including the special residents. They stand on the floor around the scene, clear of the people in it, walls and the camera.
- **Reactions:** faces and gaze that follow how worked up each girl is.
- **Masturbation:** worked-up watchers touch themselves, standing, crouching or lying down.
- **Girls together:** two worked-up watchers who both like girls make love (scissoring or standing), or one caresses the other. Their heights are matched so kisses line up.
- **3P:** the game's 3P button brings one of the watchers. From a watcher's menu you can pick who joins.
- **5P (experimental):** with three or more experienced watchers, the 3P button can start a 5P instead. All two- and three-person positions are listed in the 5P too, and the girls not in the position watch.
- **Swapping girls:** a watcher can take the place of a girl in any H, solo, 3P or 5P. Her body, clothes and voice carry over, and the girl she replaces becomes a watcher.
- **Tired girls:** a girl who gets worn out stays worn out for the rest of H and lies down when she watches. A fresh girl swapped in starts fresh, with her own climax count.
- **Experience for everyone:** every girl who took part in H gets the same H experience, part experience and H count the game gives the main girl. What a scene earned isn't lost when the 3P or 5P button restarts it.
- **Moving a watcher:** while her menu is open, the game's move tool moves her instead of the scene.

### Rules

- **Virgins** only watch: they don't touch themselves, make love, join or swap in.
- Girls make love with each other only if both have the **likes girls** trait.
- Joining a 3P or 5P or swapping in needs **H experience 50** or more (configurable).

## Requirements

- Aicomi
- [BepInEx 6 (IL2CPP, bleeding edge)](https://builds.bepinex.dev/projects/bepinex_be), as included in the usual modpacks

## Installation

1. Download `AC_HWatchers-v1.0.0.zip` from the [Releases](../../releases) page.
2. Extract it into the game folder (the one with `Aicomi.exe`), so you end up with `BepInEx/plugins/AC_HWatchers.dll`.
3. Start the game.

## Controls

| Key | Action |
| --- | --- |
| F9 | Bring one more watcher |
| Shift + F9 | Send all watchers back |
| Ctrl + right-click a watcher | Open her menu |

The watcher menu shows her experience and whether she likes girls. From it you can:
- change her outfit and clothes
- start or stop touching herself
- pick a partner and a lesbian or caress pose
- swap her into the scene
- make her the girl who joins with the 3P button
- send her back

The **5P** button next to the game's 3P button turns the 3P into a 5P when enough watchers are experienced.

## Configuration

Edit `BepInEx/config/AC_HWatchers.cfg`, or use the ConfigurationManager plugin (F1).

| Setting | Default | What it does |
| --- | --- | --- |
| Bring Watchers Automatically | On | Girls come to watch when H starts |
| Maximum Watchers | 3 | How many girls can watch at once (1 to 8) |
| Reach (m) | 25 | Only girls this close come to watch |
| Distance (m) | 1.8 | How far from the couple they stand |
| Excitement Speed | 1 | How quickly they get worked up |
| Masturbation | On | Worked-up watchers touch themselves |
| Girls Make Love | On | Watchers who like girls make love with each other |
| Watchers Join 3P | On | The 3P button brings a watcher |
| Swaps | On | Watchers can take a girl's place |
| Watchers 5P | On | The 3P button can start a 5P (experimental) |
| All Positions In 5P | On | The 5P also lists two- and three-person positions |
| Experience To Join | 50 | H experience needed to join or swap in |
| Experience For Every Girl | On | Every girl who took part gets the H results |
| Watcher Key | F9 | The key that brings watchers |
| Detailed Log | Off | Writes scene events to the BepInEx log, for bug reports |

## Notes

- After a swap, the game still counts the H itself for the girl the scene started with. The mod gives the other girls their share when the results are written.
- Lewdness and favor stay as the game sets them.
- Works together with AC_GroupHAnywhere (3P and 5P on every map), AC_PovX and AC_Subtitles.
- When reporting a bug, turn on **Detailed Log**, reproduce it, and attach `BepInEx/LogOutput.log`.

## Building

You need the .NET 6 SDK or newer, and a game folder where BepInEx has run once (so `BepInEx/interop` exists).

```
cd src
dotnet build -c Release -p:GameDir="C:\path\to\Aicomi\BepInEx"
```

If your interop assemblies are elsewhere, add `-p:InteropDir=<folder>`. The DLL is written to `src/bin/Release/net6.0/AC_HWatchers.dll`.

## License

MIT. See [LICENSE](LICENSE).
