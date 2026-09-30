using System;
using BepInEx.Logging;
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlasphemousTrainer
{
    // Boot flow on this build: splash scene (BlasphemousSplashScreen video) -> Landing
    // (loads FMOD banks and pre-inits Core, then waits for a key) -> MainMenu_MAIN.
    // We drop the logo video and auto-advance Landing AFTER its initialization so audio
    // and Core stay intact; Landing itself is never skipped.
    internal sealed class IntroSkip : IDisposable
    {
        private static IntroSkip current;
        private readonly Harmony harmony = new Harmony("local.blasphemous.trainer.intro");
        private readonly ManualLogSource log;
        private readonly bool enabled;

        internal IntroSkip(ManualLogSource log, bool enabled)
        {
            this.log = log;
            this.enabled = enabled;
            current = this;
        }

        internal bool Install()
        {
            if (!enabled) return true;
            try
            {
                harmony.Patch(GameBindings.Require(typeof(BlasphemousSplashScreen), "Awake", typeof(void)),
                    prefix: new HarmonyMethod(typeof(IntroSkip), "SplashAwake"));
                harmony.Patch(GameBindings.Require(typeof(Landing), "Start", typeof(void)),
                    postfix: new HarmonyMethod(typeof(IntroSkip), "LandingStart"));
                log.LogInfo("Intro skip installed: splash video and landing key press bypassed.");
                return true;
            }
            catch (Exception error)
            {
                log.LogError("Intro skip binding failed: " + error.GetBaseException().Message);
                try { harmony.UnpatchSelf(); } catch (Exception) { }
                return false;
            }
        }

        // Replace the original Awake (which prepared/played the VideoPlayer). Load the next
        // scene right away; the video is never prepared, so no logo sequence appears.
        private static bool SplashAwake(BlasphemousSplashScreen __instance)
        {
            if (current == null || !current.enabled) return true;
            string next = string.IsNullOrEmpty(__instance.postSplashScene) ? "Landing" : __instance.postSplashScene;
            current.log.LogInfo("Skipping splash video; loading " + next + ".");
            SceneManager.LoadScene(next, LoadSceneMode.Single);
            return false;
        }

        // Landing.Start already preloaded MainMenu_MAIN with allowSceneActivation=false.
        // Flip it on so the menu opens without the key press; Awake/Start already ran.
        private static void LandingStart(Landing __instance)
        {
            if (current == null || !current.enabled) return;
            try
            {
                AsyncOperation menu = Traverse.Create(__instance).Field("preloadMenu").GetValue<AsyncOperation>();
                if (menu == null) return;
                menu.allowSceneActivation = true;
                current.log.LogInfo("Skipping landing key press; activating main menu.");
            }
            catch (Exception error)
            {
                current.log.LogError("Landing skip failed: " + error.GetBaseException().Message);
            }
        }

        public void Dispose()
        {
            if (current == this) current = null;
            try { harmony.UnpatchSelf(); } catch (Exception) { }
        }
    }
}
