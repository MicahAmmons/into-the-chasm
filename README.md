# Into the Chasm

A handcrafted, exploration-first summoner tactics game built with MonoGame.

## Layout

| Project | What it is | Depends on |
|---|---|---|
| `src/Chasm.Core` | Pure game rules and data: grid math, combat rules, progression, world state. **No MonoGame**, so all of it is unit-testable. | nothing |
| `src/Chasm.Game` | MonoGame runtime: window, scenes, rendering, input, content loading. | Core, MonoGame |
| `src/Chasm.Desktop` | The shipped game (`IntoTheChasm.exe`). A thin entry point only. | Game |
| `src/Chasm.Tools` | Developer launcher (`ChasmTools.exe`): sandboxes and editors. Never shipped. | Game |
| `tests/Chasm.Core.Tests` | xUnit tests for Core. | Core |
| `content/` | Runtime-loaded art and data (PNG/JSON). Copied next to the exe on build. | - |

Rule of thumb: if it decides *what happens*, it goes in Core. If it decides *how it looks or feels*, it goes in Game.

## Requirements

- .NET 8 SDK

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/Chasm.Desktop     # play the game
dotnet run --project src/Chasm.Tools       # open the developer launcher
```

`--smoke-test [frames]` makes either exe boot, run that many frames (default 120) and exit. Handy for checking nothing is broken.
