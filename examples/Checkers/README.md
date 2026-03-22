# Checkers

A fully playable **Checkers / Draughts** game built with Sokol.NET — Human vs AI.

## Screenshots

### Gameplay
![Gameplay](screenshots/Screenshot%202026-03-22%20at%2020.54.58.png)

## Features

- **Gameplay**
  - 8×8 and 10×10 board support
  - Human (Light) vs AI (Dark), or swap colors via config
  - Multi-hop chain captures with animated piece paths
  - Ghost of captured piece shown during animation
  - Mandatory capture enforcement (configurable)
  - King promotion with crown rendering
  - End-of-game overlay with winner announcement and win counts
  - Undo support (reverts both your move and the AI's response)

- **Rules (configurable before each game)**
  - Board size: 8×8 or 10×10
  - Kings: Flying kings or no flying kings
  - Capture: Mandatory max-men, mandatory any, or optional
  - Men: Backward capture allowed or forbidden
  - Default: International rules (8×8, flying kings, mandatory max-men, backward capture)

- **Rendering**
  - 3D perspective board with wooden frame
  - Procedurally generated piece meshes with Phong lighting
  - Board labelled with columns (A–H) and rows (1–8)
  - Highlighted movable pieces (green), selected piece (yellow), valid destinations (blue)
  - AI last-move highlight (orange), cleared when human picks a piece

- **AI**
  - Alpha-beta minimax with positional weight tables
  - Piece value + king bonus + mobility score + back-rank guard bonus
  - Move ordering: captures-first for better alpha-beta pruning
  - Parallel root search on desktop/mobile for faster response
  - Runs on background thread (non-web) to keep the frame loop smooth
  - Configurable search depth via UI slider (1–8)

## Controls

- **Desktop / Web** — Click a highlighted piece, then click a destination square
- **Mobile (iOS / Android)** — Tap to select and move

## UI Panel

| Control | Description |
|---|---|
| Score display | Wins for You and AI |
| Phase status | Your turn / AI thinking / Game over |
| AI depth slider | Adjust search depth 1–8 |
| New Game | Reset the board (keeps score) |
| Configure | Change rules (starts a new game) |
| Undo | Take back your last move pair |

## Build and Run

From the `examples/Checkers` directory:

```bash
# Compile shaders (required once, or after shader edits)
dotnet build Checkers.csproj -t:CompileShaders

# Desktop
dotnet run -p Checkers.csproj

# WebAssembly
dotnet run -p CheckersWeb.csproj
```

For iOS, Android, and other platform workflows see [../../docs](../../docs).

## Project Files

| File | Purpose |
|---|---|
| [Source/Checkers-app.cs](Source/Checkers-app.cs) | Sokol app entry point, 3D rendering, input handling |
| [Source/CheckersGame.cs](Source/CheckersGame.cs) | Game state, phases, move application, win detection |
| [Source/CheckersAI.cs](Source/CheckersAI.cs) | Alpha-beta minimax AI with parallel root search |
| [Source/MoveGenerator.cs](Source/MoveGenerator.cs) | Legal move generation for all rule variants |
| [shaders/checkers.glsl](shaders/checkers.glsl) | GLSL Phong shaders for board and pieces |

## License

MIT — see [../../LICENSE](../../LICENSE)
