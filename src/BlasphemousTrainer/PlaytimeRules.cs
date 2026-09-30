using System;

namespace BlasphemousTrainer
{
    // Pure rules for keeping menu time out of the game's playtime clock.
    // PersistentManager.GetCurrentTimePlayed() returns
    //     TimeStored + Time.realtimeSinceStartup - LastTimeStored,
    // i.e. real seconds since the process started plus the total stored at load.
    // Nothing in that expression follows Time.timeScale, so pausing the game does
    // not stop the clock: time spent in the inventory or map screen is counted like
    // any other play time. The only way to exclude it is to subtract an accumulated
    // real-time total from every read of that clock.
    internal static class PlaytimeRules
    {
        // A frame delta is usable only when it is finite and strictly positive.
        internal static bool IsCountable(float delta)
        {
            return !float.IsNaN(delta) && !float.IsInfinity(delta) && delta > 0f;
        }

        // Adds one frame of real time to the excluded total whenever this frame must not count.
        // Any unusable input returns the previous total unchanged, so one bad sample can never
        // rewind or inflate the clock.
        internal static float Accumulate(float excluded, float delta, bool exclude)
        {
            if (!exclude || !IsCountable(delta)) return excluded;
            float next = excluded + delta;
            return float.IsNaN(next) || float.IsInfinity(next) ? excluded : next;
        }

        // Removes the excluded total from a raw playtime reading. Never negative.
        internal static float Adjusted(float raw, float excluded)
        {
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return raw;
            if (float.IsNaN(excluded) || float.IsInfinity(excluded) || excluded <= 0f) return raw;
            float value = raw - excluded;
            return value < 0f ? 0f : value;
        }
    }
}
