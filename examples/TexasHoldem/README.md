# Texas Hold'em

A fully playable Texas Hold'em poker game built with **Sokol.NET** — 2D rendering via `sokol_gp`, UI via Dear ImGui, and game logic powered by the [TexasHoldem.Logic](https://github.com/tanczosm/TexasHoldem.NET) engine.

Runs on **Desktop (Windows, macOS, Linux)**, **Android**, **iOS**, and **WebAssembly**.

## Screenshots

| Lobby / Settings | Active Hand |
|:---:|:---:|
| ![Lobby](screenshots/Screenshot%202026-03-31%20at%2016.34.31.png) | ![Active Hand](screenshots/Screenshot%202026-03-31%20at%2016.35.10.png) |

| Hand Over — Win Banner | Multi-player Table |
|:---:|:---:|
| ![Win Banner](screenshots/Screenshot%202026-03-31%20at%2016.36.01.png) | ![Multi-player](screenshots/Screenshot%202026-03-31%20at%2016.36.23.png) |

## Features

- **Full Texas Hold'em rules** — Pre-flop, Flop, Turn, River, showdown, side pots
- **1–9 AI opponents** with four distinct personalities: Tight, Balanced, Aggressive, LAG
- **Player actions** — Fold, Check, Call, Raise (adjustable raise amount)
- **Deal animation** — Cards fly from the deck to each seat one at a time
- **Win animation** — The five winning cards slide from their positions into a highlight strip
- **Pulsing highlights** — Active seat, last-action flash, winner glow
- **Community cards** — Grouped as FLOP | TURN | RIVER with gold highlight on winning combos
- **Dealer chip** tracks the button seat across hands
- **Configurable settings** (gear icon):
  - Number of AI players (1–9)
  - Buy-in amount ($100–$5000)
  - Starting small blind level
  - Escalating blinds with configurable period
  - Action speed (delay between AI moves)
- **Simulation mode** — All-AI tournament; run N hands automatically and watch the results
- **Responsive layout** — Scales from mobile portrait through 4K desktop; DPI-aware on all platforms
- **Poker table background** + custom PNG card deck + chip icons

## AI Players — SmartPlayer

All AI opponents are powered by `SmartPlayer`, a rule-based engine with four personalities:

| Style | Behaviour |
|---|---|
| `Tight` | Conservative — folds marginal hands, minimal bluffing |
| `Balanced` | Solid baseline — straightforward preflop and postflop |
| `Aggressive` | Wide opens, large sizing, frequent c-bets and bluffs |
| `LAG` | Loose-Aggressive — widest range, highest bluff frequency, large 3-bets |

See [docs/SmartPlayer.md](docs/SmartPlayer.md) for the full decision-threshold tables.

## Project Structure

```
TexasHoldem/
├── Source/
│   ├── TexasHoldem-app.cs          # Main app: rendering, animations, ImGui UI
│   ├── PokerGame.cs                # Game-thread wrapper, RenderSnapshot, AI wiring
│   ├── CardRenderer.cs             # Text-based card fallback (non-PNG path)
│   ├── FileSystem.cs               # Cross-platform async asset loading
│   ├── Texture.cs / TextureCache.cs
│   ├── SamplerSettings.cs
│   ├── ViewTracker.cs
│   └── TexasHoldemGameEngine/      # TexasHoldem.Logic submodule
├── Assets/
│   ├── cards/                      # 52 face cards + back card (PNG)
│   ├── chips/                      # Dealer chip and bet chip (PNG)
│   └── fonts/                      # TrueType font for ImGui
├── shaders/
│   └── poker.glsl                  # Custom sokol_gp shader (compiled per platform)
├── TexasHoldem.csproj              # Desktop / macOS / Linux / iOS
├── TexasHoldemWeb.csproj           # WebAssembly (WASM)
└── docs/
    └── SmartPlayer.md              # AI strategy documentation
```

## Building

> Requires **.NET 10 SDK** and the standard Sokol.NET prerequisites.  
> See the [root README](../../README.md) for full setup instructions.

### Desktop (macOS / Windows / Linux)

```bash
dotnet run --project TexasHoldem.csproj
```

### WebAssembly

```bash
dotnet run --project TexasHoldemWeb.csproj
```

Or use the **VS Code task** `prepare-TexasHoldem-web` to build and serve locally.

### Android

Use the **VS Code task** `Android: Build APK` after selecting `TexasHoldem` as the active example.

### iOS

Use the **VS Code task** `iOS: Build` after selecting `TexasHoldem` as the active example.

## Gameplay

1. Launch the app — the **Settings** panel opens automatically.
2. Set the number of AI opponents, buy-in, and blind level, then click **New Game**.
3. Each round you are dealt two hole cards face-up; AI cards are shown face-down.
4. Use **Fold / Check / Call / Raise** buttons in the action bar at the bottom.
5. At showdown all remaining hands are revealed; the winner banner displays the winning hand name.
6. The game continues until only one player has chips remaining.

## Dependencies

| Library | Purpose |
|---|---|
| [sokol_gp](https://github.com/edubart/sokol_gp) | 2D painter API — table, cards, chips |
| [Dear ImGui](https://github.com/ocornut/imgui) (cimgui) | Settings panel, player overlays, action bar |
| [TexasHoldem.Logic](https://github.com/tanczosm/TexasHoldem.NET) | Game engine, hand evaluation, AI |
| sokol_fetch | Async asset loading (PNG cards, fonts) |
| stb_image | PNG decoding |
