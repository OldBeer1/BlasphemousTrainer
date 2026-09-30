using System;
using System.Reflection;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;

namespace BlasphemousTrainer
{
    internal static class GameBindings
    {
        public static Penitent Player
        {
            get
            {
                if (!Core.ready || Core.Logic == null) return null;
                return Core.Logic.Penitent;
            }
        }

        public static bool Ready
        {
            get
            {
                Penitent p = Player;
                return p != null && p.Stats != null && p.Stats.Life != null &&
                    !p.Status.Dead && p.Stats.Life.Current > 0 &&
                    Core.GameModeManager != null &&
                    Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.NEW_GAME) &&
                    Core.Logic.CurrentState == LogicStates.Playing &&
                    Core.LevelManager != null && Core.LevelManager.currentLevel != null &&
                    Core.LevelManager.currentLevel.LevelName != "D24Z01S01";
            }
        }

        public static MethodInfo Require(Type type, string name, Type result, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, parameters, null);
            if (method == null || method.ReturnType != result || method.GetMethodBody() == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
