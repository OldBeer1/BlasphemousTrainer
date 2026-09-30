using System;

namespace BlasphemousTrainer
{
    internal static class PanelGateRules
    {
        internal static int ReturnKind(string name)
        {
            if (name.StartsWith("GetButton", StringComparison.Ordinal) || name.StartsWith("GetAnyButton", StringComparison.Ordinal) || name.StartsWith("GetNegativeButton", StringComparison.Ordinal)) return 1;
            if (name == "GetAxis" || name == "GetAxisRaw" || name == "GetAxisPrev" || name == "GetAxisRawPrev" || name == "GetAxisTimeActive" || name == "GetAxisRawTimeActive") return 2;
            return 0;
        }
    }

    internal static class PanelGrid
    {
        internal static int Move(int selected, int vertical, int horizontal)
        {
            int row = selected < 12 ? selected / 3 : selected == 12 ? 4 : 5, col = selected < 12 ? selected % 3 : selected == 12 ? 0 : selected - 13;
            row = (row + vertical + 6) % 6;
            int columns = row == 5 ? 2 : row == 4 ? 1 : 3;
            col = horizontal != 0 ? (col + horizontal + columns) % columns : Math.Min(col, columns - 1);
            return row == 5 ? 13 + col : row == 4 ? 12 : row * 3 + col;
        }
    }

    // Uses real (unscaled) time, so repeat still works while the game is paused.
    internal sealed class PanelRepeat
    {
        private int direction;
        private float next;
        internal void Reset() { direction = 0; next = 0; }
        internal int Step(float value, float now)
        {
            if (Math.Abs(value) <= .3f) { Reset(); return 0; }
            int candidate = value >= .6f ? 1 : value <= -.6f ? -1 : 0;
            if (candidate != 0 && candidate != direction)
            { direction = candidate; next = now + .35f; return direction; }
            if (direction != 0 && now >= next)
            { next = now + .12f; return direction; }
            return 0;
        }
    }

    // Rearm only after release, including reconnect, focus return and panel close.
    internal sealed class PanelPress
    {
        private bool armed;
        internal void Reset() { armed = false; }
        internal bool Step(bool held)
        {
            if (!held) { armed = true; return false; }
            if (!armed) return false;
            armed = false; return true;
        }
    }
}