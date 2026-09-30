using System;
using System.Reflection;
using HarmonyLib;
using Rewired;

namespace BlasphemousTrainer
{
    internal sealed class PanelInputGate : IDisposable
    {
        private readonly Harmony harmony = new Harmony("local.blasphemous.trainer.panel.input");
        internal static Func<bool> OwnsInput;
        internal void Install()
        {
            // The game UI reads Player directly and does not always honor Core.Input's blocker.
            // Raw Controller reads remain available to our panel; other players are untouched.
            foreach (MethodInfo method in typeof(Player).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                int kind = PanelGateRules.ReturnKind(method.Name);
                bool button = kind == 1 && method.ReturnType == typeof(bool);
                bool axis = kind == 2 && method.ReturnType == typeof(float);
                if (button || axis) harmony.Patch(method, prefix: new HarmonyMethod(typeof(PanelInputGate), button ? "Button" : "Axis"));
            }
        }
        private static bool Block(Player player) { return player.id == 0 && OwnsInput != null && OwnsInput(); }
        private static bool Button(Player __instance, ref bool __result)
        { if (!Block(__instance)) return true; __result = false; return false; }
        private static bool Axis(Player __instance, ref float __result)
        { if (!Block(__instance)) return true; __result = 0; return false; }
        public void Dispose() { OwnsInput = null; harmony.UnpatchSelf(); }
    }
}
