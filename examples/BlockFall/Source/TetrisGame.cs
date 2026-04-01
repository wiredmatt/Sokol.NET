// TetrisGame.cs — Pure game-logic for BlockFall (Tetris clone).
// No Sokol dependencies.  All coordinates are in grid units (row / col).

using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// Tetrimino shape data
// Each piece has 4 rotation states, each state is 4 cells (row, col relative).
// Block IDs 1-7 match the color palette index (0 = empty).
// ---------------------------------------------------------------------------
public static class TetrisPieces
{
    // (row, col) relative to piece origin — identical visual output to the C++ reference.
    public static readonly (int r, int c)[][][] Shapes = new (int r, int c)[][][]
    {
        // ID 0 — unused (index 0 = empty cell)
        Array.Empty<(int,int)[]>(),

        // ID 1 — L-block (green)
        new[]
        {
            new[]{(0,2),(1,0),(1,1),(1,2)},
            new[]{(0,1),(1,1),(2,1),(2,2)},
            new[]{(1,0),(1,1),(1,2),(2,0)},
            new[]{(0,0),(0,1),(1,1),(2,1)},
        },

        // ID 2 — J-block (red)
        new[]
        {
            new[]{(0,0),(1,0),(1,1),(1,2)},
            new[]{(0,1),(0,2),(1,1),(2,1)},
            new[]{(1,0),(1,1),(1,2),(2,2)},
            new[]{(0,1),(1,1),(2,0),(2,1)},
        },

        // ID 3 — I-block (orange)
        new[]
        {
            new[]{(1,0),(1,1),(1,2),(1,3)},
            new[]{(0,2),(1,2),(2,2),(3,2)},
            new[]{(2,0),(2,1),(2,2),(2,3)},
            new[]{(0,1),(1,1),(2,1),(3,1)},
        },

        // ID 4 — O-block (yellow)
        new[]
        {
            new[]{(0,0),(0,1),(1,0),(1,1)},
            new[]{(0,0),(0,1),(1,0),(1,1)},
            new[]{(0,0),(0,1),(1,0),(1,1)},
            new[]{(0,0),(0,1),(1,0),(1,1)},
        },

        // ID 5 — S-block (purple)
        new[]
        {
            new[]{(0,1),(0,2),(1,0),(1,1)},
            new[]{(0,1),(1,1),(1,2),(2,2)},
            new[]{(1,1),(1,2),(2,0),(2,1)},
            new[]{(0,0),(1,0),(1,1),(2,1)},
        },

        // ID 6 — T-block (cyan)
        new[]
        {
            new[]{(0,1),(1,0),(1,1),(1,2)},
            new[]{(0,1),(1,1),(1,2),(2,1)},
            new[]{(1,0),(1,1),(1,2),(2,1)},
            new[]{(0,1),(1,0),(1,1),(2,1)},
        },

        // ID 7 — Z-block (blue)
        new[]
        {
            new[]{(0,0),(0,1),(1,1),(1,2)},
            new[]{(0,2),(1,1),(1,2),(2,1)},
            new[]{(1,0),(1,1),(2,1),(2,2)},
            new[]{(0,1),(1,0),(1,1),(2,0)},
        },
    };

    // Initial spawn column offset (added to all cells on spawn).
    // Matches the C++ Move(rowOff, colOff) per block type.
    public static readonly (int r, int c)[] SpawnOffsets = new (int r, int c)[]
    {
        (0, 0),  // 0 — unused
        (0, 3),  // 1 — L
        (0, 3),  // 2 — J
        (-1,3),  // 3 — I
        (0, 4),  // 4 — O
        (0, 3),  // 5 — S
        (0, 3),  // 6 — T
        (0, 3),  // 7 — Z
    };
}

// ---------------------------------------------------------------------------
// Tetrimino — active or preview piece
// ---------------------------------------------------------------------------
public struct Tetrimino
{
    public int Id;         // 1-7
    public int Row;        // row offset (top of bounding box in grid)
    public int Col;        // col offset
    public int Rotation;   // 0-3

    // Returns absolute (row, col) grid positions of all 4 cells.
    public readonly (int r, int c)[] GetCells()
    {
        var cells = TetrisPieces.Shapes[Id][Rotation];
        var result = new (int r, int c)[4];
        for (int i = 0; i < 4; i++)
            result[i] = (Row + cells[i].r, Col + cells[i].c);
        return result;
    }
}

// ---------------------------------------------------------------------------
// TetrisGame — complete game state and logic
// ---------------------------------------------------------------------------
public class TetrisGame
{
    // Grid dimensions
    public const int Rows = 20;
    public const int Cols = 10;

    // Grid: 0 = empty, 1-7 = locked piece color id
    public int[,] Grid = new int[Rows, Cols];

    public Tetrimino Current;
    public Tetrimino Next;

    public int Score;
    public int Level;
    public int TotalLines;
    public bool GameOver;
    public bool Paused;

    // Event flags — cleared at the start of each frame by the app
    public bool RotateOccurred;
    public bool LineClearOccurred;
    public bool LockOccurred;

    // Internal
    private float _fallTimer;
    private readonly List<int> _bag = new();
    private Random _rng = new();

    public TetrisGame() { Reset(); }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public void Reset()
    {
        Array.Clear(Grid, 0, Grid.Length);
        Score = 0;
        Level = 1;
        TotalLines = 0;
        GameOver = false;
        Paused = false;
        _fallTimer = 0f;
        _bag.Clear();
        RotateOccurred = LineClearOccurred = LockOccurred = false;

        Next = SpawnPiece(DrawFromBag());
        Current = SpawnPiece(DrawFromBag());
    }

    // Call once per frame with delta time in seconds.
    public void Update(float dt)
    {
        if (GameOver || Paused) return;

        _fallTimer += dt;
        float interval = FallInterval();
        if (_fallTimer >= interval)
        {
            _fallTimer -= interval;
            if (!TryMoveDown())
                LockCurrent();
        }
    }

    // Returns true if the piece moved.
    public bool MoveLeft()
    {
        if (GameOver || Paused) return false;
        Current.Col--;
        if (!IsValid(Current)) { Current.Col++; return false; }
        return true;
    }

    public bool MoveRight()
    {
        if (GameOver || Paused) return false;
        Current.Col++;
        if (!IsValid(Current)) { Current.Col--; return false; }
        return true;
    }

    // Soft drop — move down one row; returns false if locked.
    public bool SoftDrop()
    {
        if (GameOver || Paused) return false;
        if (!TryMoveDown())
        {
            LockCurrent();
            return false;
        }
        Score += 1;   // 1 point per manual soft-drop row
        _fallTimer = 0f;
        return true;
    }

    // Hard drop — slam to bottom immediately.
    public void HardDrop()
    {
        if (GameOver || Paused) return;
        int dropped = 0;
        while (TryMoveDown()) dropped++;
        Score += dropped * 2;   // 2 points per cell hard-dropped
        LockCurrent();
    }

    // Rotate clockwise.
    public bool Rotate()
    {
        if (GameOver || Paused) return false;
        int prev = Current.Rotation;
        Current.Rotation = (Current.Rotation + 1) % 4;
        if (!IsValid(Current))
        {
            // Simple wall-kick: try ±1 column
            Current.Col++;
            if (!IsValid(Current))
            {
                Current.Col -= 2;
                if (!IsValid(Current))
                {
                    Current.Col++;            // restore col
                    Current.Rotation = prev;  // restore rotation
                    return false;
                }
            }
        }
        RotateOccurred = true;
        return true;
    }

    // Ghost piece row — where the current piece would land.
    public int GhostRow()
    {
        var ghost = Current;
        while (true)
        {
            ghost.Row++;
            if (!IsValid(ghost)) { ghost.Row--; break; }
        }
        return ghost.Row;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private bool TryMoveDown()
    {
        Current.Row++;
        if (!IsValid(Current)) { Current.Row--; return false; }
        return true;
    }

    private void LockCurrent()
    {
        LockOccurred = true;
        foreach (var (r, c) in Current.GetCells())
            if (r >= 0 && r < Rows && c >= 0 && c < Cols)
                Grid[r, c] = Current.Id;

        int cleared = ClearFullRows();
        UpdateScore(cleared);
        if (cleared > 0)
        {
            TotalLines += cleared;
            Level = TotalLines / 10 + 1;
            LineClearOccurred = true;
        }

        Current = Next;
        Next = SpawnPiece(DrawFromBag());

        if (!IsValid(Current))
            GameOver = true;
    }

    private int ClearFullRows()
    {
        int cleared = 0;
        for (int row = Rows - 1; row >= 0; row--)
        {
            if (IsRowFull(row))
            {
                ClearRow(row);
                cleared++;
            }
            else if (cleared > 0)
            {
                MoveRowDown(row, cleared);
            }
        }
        return cleared;
    }

    private bool IsRowFull(int row)
    {
        for (int c = 0; c < Cols; c++)
            if (Grid[row, c] == 0) return false;
        return true;
    }

    private void ClearRow(int row)
    {
        for (int c = 0; c < Cols; c++)
            Grid[row, c] = 0;
    }

    private void MoveRowDown(int row, int distance)
    {
        for (int c = 0; c < Cols; c++)
        {
            Grid[row + distance, c] = Grid[row, c];
            Grid[row, c] = 0;
        }
    }

    private void UpdateScore(int lines)
    {
        Score += lines switch
        {
            1 => 100,
            2 => 300,
            3 => 500,
            4 => 800,
            _ => 0
        };
    }

    private bool IsValid(Tetrimino t)
    {
        foreach (var (r, c) in t.GetCells())
        {
            if (r < 0 || r >= Rows || c < 0 || c >= Cols) return false;
            if (Grid[r, c] != 0) return false;
        }
        return true;
    }

    private Tetrimino SpawnPiece(int id)
    {
        var (sr, sc) = TetrisPieces.SpawnOffsets[id];
        return new Tetrimino { Id = id, Row = sr, Col = sc, Rotation = 0 };
    }

    // 7-bag randomiser — ensures every piece appears before any repeats.
    private int DrawFromBag()
    {
        if (_bag.Count == 0)
        {
            _bag.AddRange(new[] { 1, 2, 3, 4, 5, 6, 7 });
            // Fisher-Yates shuffle
            for (int i = _bag.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
            }
        }
        int drawn = _bag[0];
        _bag.RemoveAt(0);
        return drawn;
    }

    // Fall interval decreases with level (minimum 0.05 s).
    private float FallInterval() => MathF.Max(0.05f, 1.0f - (Level - 1) * 0.1f);
}
