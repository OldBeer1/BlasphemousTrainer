using System;
using Framework.Managers;
using Gameplay.UI;
using UnityEngine;

namespace BlasphemousTrainer
{
    // Features 8 and 9: keep real time out of the game's playtime clock.
    //
    // AC44 ("Bronze Medal") grants when
    //     PersistentManager.GetCurrentTimePlayedForAC44() / 60 < 180
    // and that value is GetCurrentTimePlayed() - TimeAtSlotAscension, so every read of
    // GetCurrentTimePlayed() is exactly what the achievement sees. That clock is driven by
    // Time.realtimeSinceStartup, which ignores Time.timeScale, so neither pausing the game
    // nor sitting in the inventory stops it: the total has to be subtracted from those reads.
    //
    // Feature 8 drops the time spent with the game's inventory, map or pause screen open.
    // Feature 9 drops every frame while it is switched on, i.e. a manual freeze.
    // Both feed one excluded total, so they combine, and neither rewinds the clock when it is
    // switched off: time already excluded stays excluded.
    //
    // Sampling runs every frame from Plugin.Update, ahead of every early return, so the
    // real-time delta always covers the frame that was just executed.
    internal sealed partial class FeatureController
    {
        internal const int MenuFeature = 8;
        internal const int FreezeFeature = 9;

        internal float ExcludedTime;
        private float clockSample = -1f;
        private bool clockSamplingStopped;

        private void InstallPlaytimeGuard()
        {
            InstallGroup(MenuFeature, h =>
            {
                // The menu state is only read at runtime; verify the member before binding.
                GameBindings.Require(typeof(UIController), "get_IsShowingMenu", typeof(bool));
                h.Patch(GameBindings.Require(typeof(PersistentManager), "GetCurrentTimePlayed", typeof(float)),
                    postfix: Hook("AdjustPlayTime"));
                h.Patch(GameBindings.Require(typeof(PersistentManager), "GetCurrentPersistentState",
                        typeof(PersistentManager.PersistentData), typeof(string), typeof(bool)),
                    postfix: Hook("AdjustSavedTime"));
                // A load or a new game establishes a new baseline for the stored total, so the
                // running exclusion has to restart there. Keeping it would subtract the same
                // interval a second time from the freshly loaded value.
                h.Patch(GameBindings.Require(typeof(PersistentManager), "SetCurrentPersistentState",
                        typeof(void), typeof(PersistentManager.PersistentData), typeof(bool), typeof(string)),
                    prefix: Hook("ResetExcludedOnLoad"));
                h.Patch(GameBindings.Require(typeof(PersistentManager), "ResetPersistence", typeof(void)),
                    prefix: Hook("ResetExcludedOnReset"));
            });
            // The freeze switch drives the same two postfixes, so it is available exactly when
            // they bound and needs no patch of its own.
            Available[FreezeFeature] = Available[MenuFeature];
            Errors[FreezeFeature] = Errors[MenuFeature];
        }

        // Sampling is independent of Ready because menus also need exclusion while paused.
        internal void SamplePlaytime()
        {
            float now = Time.realtimeSinceStartup;
            if (clockSample < 0f) { clockSample = now; return; }
            float delta = now - clockSample;
            clockSample = now;
            // A manual freeze wins outright, so the menu check is skipped while it is on.
            bool exclude = Available[FreezeFeature] && Enabled[FreezeFeature];
            if (!exclude && !clockSamplingStopped && Available[MenuFeature] && Enabled[MenuFeature])
            {
                try
                {
                    UIController ui = UIController.instance;
                    exclude = ui != null && ui.IsShowingMenu;
                }
                catch (Exception error)
                {
                    clockSamplingStopped = true;
                    Log.LogError("Menu time sampling stopped: " + error.GetBaseException().Message);
                    return;
                }
            }
            ExcludedTime = PlaytimeRules.Accumulate(ExcludedTime, delta, exclude);
        }

        private static void AdjustPlayTime(ref float __result)
        {
            if (Current == null) return;
            Current.SamplePlaytime();
            __result = PlaytimeRules.Adjusted(__result, Current.ExcludedTime);
        }

        // Saves build the same expression inline instead of calling the getter, so the
        // reduction has to reach this path as well: otherwise the next load would restore
        // the inflated total and the excluded time would be given back.
        private static void AdjustSavedTime(ref PersistentManager.PersistentData __result)
        {
            if (Current == null || __result == null) return;
            Current.SamplePlaytime();
            var data = __result as PersistentManager.PersitentPersistenceData;
            if (data == null) return;
            data.Time = PlaytimeRules.Adjusted(data.Time, Current.ExcludedTime);
        }

        // __1 is the isloading flag. Positional injection is used on purpose so the patch does
        // not depend on the game's parameter names surviving a rebuild.
        private static void ResetExcludedOnLoad(bool __1)
        {
            if (Current == null || !__1) return;
            Current.ResetPlaytimeBaseline();
        }

        private static void ResetExcludedOnReset()
        {
            if (Current == null) return;
            Current.ResetPlaytimeBaseline();
        }

        private void ResetPlaytimeBaseline()
        {
            ExcludedTime = 0f;
            clockSample = Time.realtimeSinceStartup;
            clockSamplingStopped = false;
        }
    }
}
