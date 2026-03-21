// ReversiBoard.cs — C# port of the Rust Board struct from bothello1
// Pieces are stored as bits in two ulong bitfields.
// Bit index = row*8 + col (row 0 = top, col 0 = left).

namespace Reversi
{
    public struct Board
    {
        public ulong PlayerPieces;    // 1 bit set per player disc
        public ulong OpponentPieces;  // 1 bit set per opponent disc

        public Board(ulong player, ulong opponent)
        {
            PlayerPieces = player;
            OpponentPieces = opponent;
        }

        /// <summary>Return a board with player/opponent swapped.</summary>
        public Board Swapped() => new Board(OpponentPieces, PlayerPieces);

        public int PlayerScore()   => System.Numerics.BitOperations.PopCount(PlayerPieces);
        public int OpponentScore() => System.Numerics.BitOperations.PopCount(OpponentPieces);

        /// <summary>
        /// Try to place a piece at <paramref name="index"/> (0-63, row-major).
        /// Returns (true, newBoard) if the move is legal, (false, _) otherwise.
        /// </summary>
        public bool PlacePiece(int index, out Board result)
        {
            result = default;
            if (index < 0 || index >= 64) return false;

            ulong cursor = 1UL << index;
            if (((PlayerPieces | OpponentPieces) & cursor) != 0) return false;

            int y = index / 8;
            int x = index - y * 8;

            ulong mask = 0;

            if (x < 7)
            {
                mask |= FlipLeft(cursor, 1, 7 - x);
                if (y < 7) mask |= FlipLeft(cursor, 9, System.Math.Min(7 - x, 7 - y));
                if (y > 0) mask |= FlipRight(cursor, 7, System.Math.Min(7 - x, y));
            }
            if (x > 0)
            {
                mask |= FlipRight(cursor, 1, x);
                if (y > 0) mask |= FlipRight(cursor, 9, System.Math.Min(x, y));
                if (y < 7) mask |= FlipLeft(cursor, 7, System.Math.Min(x, 7 - y));
            }
            if (y < 7) mask |= FlipLeft(cursor, 8, 7 - y);
            if (y > 0) mask |= FlipRight(cursor, 8, y);

            if (mask == 0) return false;  // illegal move

            result = new Board(
                PlayerPieces | mask | cursor,
                OpponentPieces & ~mask
            );
            return true;
        }

        // Shift cursor left (<<) up to `steps` times, collecting opponent bits
        private ulong FlipLeft(ulong cursor, int shift, int steps)
        {
            ulong lineMask = 0;
            for (int i = 0; i < steps; i++)
            {
                cursor <<= shift;
                if ((OpponentPieces & cursor) != 0)
                    lineMask |= cursor;
                else if ((PlayerPieces & cursor) != 0)
                    return lineMask;
                else
                    return 0;
            }
            return 0;
        }

        // Shift cursor right (>>) up to `steps` times, collecting opponent bits
        private ulong FlipRight(ulong cursor, int shift, int steps)
        {
            ulong lineMask = 0;
            for (int i = 0; i < steps; i++)
            {
                cursor >>= shift;
                if ((OpponentPieces & cursor) != 0)
                    lineMask |= cursor;
                else if ((PlayerPieces & cursor) != 0)
                    return lineMask;
                else
                    return 0;
            }
            return 0;
        }

        /// <summary>
        /// Construct a Board from a byte[64] array where:
        ///   1 = player piece, 2 = opponent piece, 0 = empty.
        /// </summary>
        public static Board FromArray(byte[] spaces)
        {
            ulong player = 0, opponent = 0;
            int len = System.Math.Min(spaces.Length, 64);
            for (int i = 0; i < len; i++)
            {
                ulong bit = 1UL << i;
                if (spaces[i] == 1) player   |= bit;
                else if (spaces[i] == 2) opponent |= bit;
            }
            return new Board(player, opponent);
        }

        /// <summary>Standard initial Reversi position (player=Black=1, opponent=White=2).</summary>
        public static Board InitialPosition()
        {
            // d4=White, e4=Black, d5=Black, e5=White  (row-major 0-indexed)
            // d4 = index 27, e4=28, d5=35, e5=36
            ulong player   = (1UL << 28) | (1UL << 35); // Black
            ulong opponent = (1UL << 27) | (1UL << 36); // White
            return new Board(player, opponent);
        }
    }
}
