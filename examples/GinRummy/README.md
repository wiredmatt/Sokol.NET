# Gin Rummy

A fully playable Gin Rummy card game built with [Sokol.NET](../../README.md), featuring an AI opponent, complete rule enforcement, animated card deals, and cross-platform support.

## Screenshots

| Main Menu | Gameplay |
|-----------|----------|
| ![Main Menu](screenshots/Screenshot%202026-04-06%20at%2023.33.48.png) | ![Gameplay](screenshots/Screenshot%202026-04-06%20at%2023.34.19.png) |

| Draw Phase | Discard Phase |
|------------|---------------|
| ![Draw Phase](screenshots/Screenshot%202026-04-06%20at%2023.34.27.png) | ![Discard Phase](screenshots/Screenshot%202026-04-06%20at%2023.34.44.png) |

| Round Result |
|--------------|
| ![Round Result](screenshots/Screenshot%202026-04-06%20at%2023.38.09.png) |

## Gameplay

Standard Gin Rummy rules (two-player, human vs AI):

1. **Deal** - Each player receives 10 cards; one card is turned face-up to start the discard pile.
2. **Draw** - On your turn, draw from the stock pile or take the top discard card.
3. **Discard** - Discard one card from your hand (11 → 10 cards).
4. **Knock** - When your deadwood (unmatched cards) is 10 or fewer points, you may knock.
5. **Gin** - Knock with 0 deadwood for a Gin bonus (+25 pts).
6. **Lay-offs** - After a knock, the opposing player may lay off their deadwood onto your melds.
7. **Undercut** - If the opponent's deadwood is ≤ yours after lay-offs, they win +25 bonus pts.
8. **Target Score** - First player to reach the target score wins the game.

## Features

- **AI Opponent** - Greedy AI that evaluates melds, manages deadwood, and decides when to knock
- **Knock & Gin** - Full knock/gin resolution with lay-off support and undercut detection
- **Card Sorting** - Sort hand by suit/rank with animated reorder
- **Score Bar** - Live round, score, target, and deadwood display (centered, top bar)
- **Event Log** - Toggleable in-game log of every draw, discard, knock, and result
- **Card Animations** - Smooth fly animations for deals, draws, and discards
- **Game Modes** - Casual (target 50) and Competitive (target 100) via main menu
- **Mid-game Menu** - Return to main menu at any point during a round

## Controls

| Action | Input |
|--------|-------|
| Draw from stock | Click **Draw from Stock** button, or left-click / tap the stock pile directly |
| Take top discard | Click **Take Discard** button, or left-click / tap the discard pile directly |
| Select card to discard | Left-click or tap a card in your hand |
| Discard selected card | Click **Discard Selected** button, or left-click / tap the selected card again |
| Knock | Click **Knock (N)** button (when deadwood ≤ 10) |
| Gin | Click **Gin!** button (when deadwood = 0) |
| Toggle event log | Click **Log** / **Log X** button (top-right) |
| Return to menu | Click **Menu** button (top-right) |

## Building & Running

### Desktop

```bash
cd examples/GinRummy
dotnet build GinRummy.csproj
dotnet run --project GinRummy.csproj
```

Or use the VS Code task **prepare-GinRummy**.

### WebAssembly

```bash
dotnet run --project ../../tools/SokolApplicationBuilder -- \
  --task build --architecture web --path .
```

### Android / iOS

Use VS Code Command Palette → **Tasks: Run Task** → select the appropriate Android or iOS task.

## Project Structure

```
GinRummy/
├── Source/
│   ├── GinRummy-app.cs              # Main app: rendering, UI, card animations
│   └── Rummy.Logic/
│       ├── Cards/                   # Card, Deck, Suit/Type enums
│       ├── Melds/                   # Meld validation, deadwood calculation
│       └── GinRummy/
│           ├── GinRummyGame.cs      # Game logic, state machine, AI turn
│           └── GinAI.cs             # AI strategy (draw, discard, knock decisions)
├── Assets/                          # Card atlas textures, fonts
├── shaders/                         # GLSL shader sources
└── screenshots/                     # Screenshots
```

## Back to Main

[← Back to Sokol.NET](../../README.md)
