# Into the Chasm

A handcrafted, exploration-first summoner tactics game (MonoGame, .NET 8).

## Run it

```bash
dotnet run --project src/Chasm.Desktop     # the game (title screen, 3 autosave slots)
dotnet run --project src/Chasm.Tools       # the developer launcher
dotnet test                                # rules + content checks
```

Tools can jump straight to a tool: `--mode map|world|combat|content [--arg <tile id>]`.

## Projects

| Project | What it is |
|---|---|
| `src/Chasm.Core` | Game rules and data. No MonoGame, fully unit-tested. |
| `src/Chasm.Game` | MonoGame runtime: scenes, drawing, input, content loading, saves. |
| `src/Chasm.Desktop` | The shipped game. |
| `src/Chasm.Tools` | Launcher: Content Browser, Map Editor, World Sandbox, Combat Sandbox. |
| `tests/Chasm.Core.Tests` | Tests, including "all content loads and every reference resolves" and a playthrough of Floor 1. |
| `content/art` | PNGs, loaded at runtime (no content pipeline). Missing art shows as a labelled box. |
| `content/defs/<type>/<id>.json` | Every piece of content, one file each. Hot-reloads while the game runs. |

## Words

- **Floor**: a numbered depth. **Tile**: one 1920x1080 screen of a floor. **Cell**: a 128x64 diamond on a tile.
- **Placement**: something put on a cell (item, interactable, monster group, the Figure).
- **Rule**: "when these *conditions* are all true, do these *outcomes*". Used everywhere.

## Making content (no code needed)

Open **Tools > Content Browser** (or the **New** menu anywhere). Every content type has a form; required
fields are marked `*`. Any field that points at other content has a dropdown of everything that exists
plus **+ New...**, which opens that type's form on top and fills the field in when you save.

**An item that only appears if the player has another item**
1. Map Editor, open the tile, tool **Select (1)**, click a cell.
2. **Add to this cell > Item pickup**.
3. *Item*: pick one, or **+ New Item...** and fill in the form.
4. *Visible When*: **+ Add Condition > Player has item**, then pick the other item.
5. Ctrl+S. Then **Tile > Play this tile** to try it in the World Sandbox (give yourself items from its panel).

**A new monster**: Content Browser > Monsters > **+ New Monster**. Set stats, abilities (or **+ New Ability**),
animation (empty = labelled box), and *Ai Priorities*: the monster does the first rule that's possible, repeatedly,
until none is. Try it straight away in **Combat Sandbox** (Add it as an enemy, or AI vs AI at 4x speed).

**A summon**: any monster can be one. Fill its *As a summon* group (AP cost, uses per rest, affinity levels:
what each level grants). Unlock it for the player with the outcome **Player can now summon a creature**
(e.g. as an item's use rule, or a quest reward).

**A secret spot**: Interactable, tick *Invisible*, *Trigger* = "Player steps onto the cell", rules with
conditions such as **Aspect is switched on** and outcomes such as **Ask the player yes / no > Move player to tile**.

**Monster groups in the world**: Add > Monster group. Pick an Encounter, a patrol (use **Pick** to click the
points on the map), whether it's aggressive.

**World flags** are the glue: any outcome can set `treant_defeated`, any condition can check it, on any floor.

## Adding a new *kind* of thing (needs code)

New behaviours are small classes in Core; editors find them automatically:
- a condition: subclass `Condition` (see `Rules/Conditions.cs`)
- an outcome: subclass `Outcome` (see `Rules/Outcomes.cs`)
- an AI behaviour: subclass `AiRule` (see `Combat/CombatAi.cs`)
- a placement: subclass `Placement` (see `World/Placements.cs`)
- a whole new content type: subclass `Def` with `[DefFolder("folder", "Name")]`

Give it a `[TypeId("...")]` and never rename that id once content uses it.

## Saving

There is no save button. Each slot is one file, rewritten when something meaningful happens (changing tile,
gaining items, flags, resting, after fights, quitting). Starting a fight writes "fight in progress": quitting
mid-fight counts as a loss next time. Saves live in your user AppData folder, not in the repo.
