# Chess

A fully playable **Chess** game built with Sokol.NET and the Lynx chess engine.

[Back to the main Sokol.NET README](../../README.md)

## Screenshot

### Gameplay
[Open screenshot](screenshots/Screenshot%202026-03-26%20at%2015.06.27.png)

![Chess gameplay](screenshots/Screenshot%202026-03-26%20at%2015.06.27.png)

## Features

- Full chess rules driven by the Lynx engine
- Human vs AI gameplay with configurable search depth
- Play as White or Black
- Board flip option
- Move history in UCI or algebraic notation
- Copy move history to the clipboard
- Optional per-side time control
- Check, last-move, and legal-move highlighting
- Game-over detection for checkmate, stalemate, fifty-move rule, and insufficient material

## Supported Platforms

Currently supported:

- Desktop
- Android
- iOS

Currently not supported:

- WebAssembly / Web

## Controls

- Desktop: click a piece to select it, then click a highlighted destination square
- Mobile: tap to select and move
- `F`: flip the board
- `Esc`: show or hide the UI panel

## UI Panel

| Control | Description |
|---|---|
| AI Depth | Adjust AI search depth |
| Use Time Limit | Enable chess clocks |
| Minutes/Side | Set the starting time for each side |
| Reset Clocks | Reset both clocks |
| Flip Board | Toggle board orientation |
| New Game (White) | Start a new game as White |
| New Game (Black) | Start a new game as Black |
| UCI / Algebraic | Change move history notation |
| Copy Move History | Copy the current move list to the clipboard |

## Build and Run

From the `examples/Chess` directory:

```bash
# Desktop
dotnet run -p Chess.csproj
```

For Android and iOS workflows, see [../../docs](../../docs).

## Project Files

| File | Purpose |
|---|---|
| [Source/Chess-app.cs](Source/Chess-app.cs) | Sokol app entry point, rendering, input, UI, and time control |
| [Source/ChessGame.cs](Source/ChessGame.cs) | Game state wrapper over the Lynx engine |
| [Source/ChessAI.cs](Source/ChessAI.cs) | Background AI execution and completion handoff |
| [Source/Program.cs](Source/Program.cs) | Desktop, Android, and iOS entry points |
| [Assets](Assets) | Chess piece textures |

## License

MIT — see [../../LICENSE](../../LICENSE)