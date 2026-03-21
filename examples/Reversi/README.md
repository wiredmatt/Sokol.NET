# Reversi Example

A fully playable **Reversi (Othello)** example built with Sokol.NET.

This sample demonstrates:
- 3D board and procedural disc rendering
- Flip animations for captured discs
- Alpha-beta AI with adjustable depth
- Mouse input on desktop/web and touch input on iOS/Android
- Cross-platform build targets from a single C# codebase

## Features

- **Gameplay**
  - Standard 8x8 Reversi rules
  - Valid move markers
  - Turn pass handling when a side has no legal moves
  - End-of-game winner announcement and score summary
  - Undo support

- **Rendering**
  - Checkerboard board with wooden frame
  - Two-color disc mesh (white/black faces) generated procedurally
  - Dynamic game-over overlay in the center of the screen

- **AI**
  - Async AI turn execution to avoid blocking the frame loop
  - Configurable search depth via UI slider (1-10)

## Controls

- **Desktop/Web**
  - Left click a highlighted marker to place a disc

- **Mobile (iOS/Android)**
  - Tap the board cell (touch ended) to place a disc

## UI

- AI depth slider
- New Game
- Undo
- "Play as White (AI first)" side toggle

## Build and Run

From this directory:

```bash
# Desktop
dotnet run -p Reversi.csproj

# WebAssembly
dotnet run -p ReversiWeb.csproj
```

For platform-specific workflows, see the workspace guides in [../../docs](../../docs).

## Screenshots

- Gameplay: [Screenshot 2026-03-21 at 12.59.47.png](screenshots/Screenshot%202026-03-21%20at%2012.59.47.png)
- Game Over Overlay: [Screenshot 2026-03-21 at 13.01.23.png](screenshots/Screenshot%202026-03-21%20at%2013.01.23.png)

## Project Files

- App + rendering: [Source/Reversi-app.cs](Source/Reversi-app.cs)
- Game state + flow: [Source/ReversiGame.cs](Source/ReversiGame.cs)
- AI search: [Source/ReversiAI.cs](Source/ReversiAI.cs)
- Bitboard logic: [Source/ReversiBoard.cs](Source/ReversiBoard.cs)
- Shader source: [shaders/reversi.glsl](shaders/reversi.glsl)
