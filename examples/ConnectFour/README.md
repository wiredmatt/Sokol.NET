# Connect 4

A fully playable **Connect 4** game built with Sokol.NET — Human vs AI.

## Screenshots

### Gameplay
![Gameplay](screenshots/Screenshot%202026-03-22%20at%2010.10.09.png)

### AI Wins
![AI Wins](screenshots/Screenshot%202026-03-22%20at%2010.10.21.png)

## Features

- **Gameplay**
  - Classic 7-column × 6-row Connect 4 rules
  - Human (Red) vs AI (Yellow)
  - Drop animation — disc falls into the column in real time
  - Ghost disc preview shows where your piece will land
  - Pulsing win-ring highlight on the four winning discs
  - End-of-game overlay with winner announcement and score
  - Undo support (reverts both your move and the AI's response)

- **Rendering**
  - 3D top-down perspective board with wooden frame
  - Procedurally generated disc meshes with Phong lighting
  - Column hover highlight and ghost disc preview
  - Per-column alternating board colors for readability

- **AI**
  - Alpha-beta negamax with center-first column ordering
  - Immediate win / block detection before search
  - Parallel root search on desktop for faster response
  - Configurable search depth via UI slider (1–10)
  - Async execution to keep the frame loop smooth

## Controls

- **Desktop / Web** — Left-click a column to drop your disc
- **Mobile (iOS / Android)** — Tap a column (touch ended)

## UI Panel

| Control | Description |
|---|---|
| Score display | Shows wins for You and AI |
| Phase status | Your turn / AI thinking / Game over |
| AI depth slider | Adjust search depth 1–10 |
| Play as Yellow | Let the AI move first |
| New Game | Reset the board |
| Undo | Take back your last move pair |

## Build and Run

From the `examples/ConnectFour` directory:

```bash
# Compile shaders (required once, or after shader edits)
dotnet build ConnectFour.csproj -t:CompileShaders

# Desktop
dotnet run -p ConnectFour.csproj

# WebAssembly
dotnet run -p ConnectFourWeb.csproj
```

For iOS, Android, and other platform workflows see [../../docs](../../docs).

## Project Files

| File | Purpose |
|---|---|
| [Source/ConnectFour-app.cs](Source/ConnectFour-app.cs) | Sokol app entry point, 3D rendering, input handling |
| [Source/ConnectFourGame.cs](Source/ConnectFourGame.cs) | Game state, phases, drop animation, win detection |
| [Source/ConnectFourAI.cs](Source/ConnectFourAI.cs) | Alpha-beta negamax AI and column-major bitboard |
| [shaders/connectfour.glsl](shaders/connectfour.glsl) | GLSL Phong shaders for board and discs |

## License

MIT — see [../../LICENSE](../../LICENSE)
