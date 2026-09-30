using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using Framework.FrameworkCore.Attributes.Logic;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Penitent.Damage;
using HarmonyLib;
using UnityEngine;

namespace BlasphemousTrainer
{
    internal sealed partial class FeatureController : IDisposable
    {
        internal static FeatureController Current;
        internal readonly bool[] Enabled = new bool[FeatureCatalog.Count];
        internal readonly bool[] Available = new bool[FeatureCatalog.Count];
        internal readonly string[] Errors = new string[FeatureCatalog.Count];
        internal readonly int[] Hits = new int[FeatureCatalog.Count];
        internal float Multiplier = 2;
        internal readonly ManualLogSource Log;
        private readonly List<Harmony> owners = new List<Harmony>();
        [ThreadStatic] private static int swordDepth;
        private int defenseHits;

        // Defense mode (session state; restored only when cheat memory is enabled):
        // 0 = off, 1 = reduce by DefenseTier. "免扣血" is exactly feature 0 (锁定生命),
        // so the two controls stay linked and cannot drift apart.
        internal int DefenseMode;
        internal float DefenseTier = .5f;

        internal FeatureController(ManualLogSource log) { Log = log; Current = this; }

        internal void Install()
        {
            InstallGroup(0, h => Patch(h, GameBindings.Require(typeof(PenitentDamageArea), "RaiseDamageEvent", typeof(void), typeof(Hit)), "ProtectLife", null));
            InstallGroup(1, h => {
                Patch(h, GameBindings.Require(typeof(PrayerUse), "get_CanUsePrayer", typeof(bool)), null, "PrayerAvailability");
                Patch(h, GameBindings.Require(typeof(PrayerUse), "OnUpdate", typeof(void)), null, "PrayerAvailability");
                Patch(h, GameBindings.Require(typeof(PrayerUse), "StartUsingPrayer", typeof(void), typeof(Prayer)), null, "PrayerSpending");
            });
            InstallGroup(2, h => Patch(h, GameBindings.Require(typeof(Healing), "Heal", typeof(void)), null, "FlaskSpending"));
            InstallGroup(3, h => {
                h.Patch(GameBindings.Require(typeof(PenitentSword), "Attack", typeof(void), typeof(Hit)),
                    prefix: Hook("SwordEnter"), finalizer: Hook("SwordExit"));
                Patch(h, GameBindings.Require(typeof(EnemyDamageArea), "TakeDamageAmount", typeof(void), typeof(Hit)), "ScaleEnemyHit", null);
            });
            InstallExtensions();
            InstallPlaytimeGuard();
        }

        private void InstallGroup(int feature, Action<Harmony> install)
        {
            var owner = new Harmony("local.blasphemous.trainer.feature." + feature);
            try
            {
                install(owner);
                owners.Add(owner);
                Available[feature] = true;
                Log.LogInfo("Feature binding ready: " + feature + "; enabled=false");
            }
            catch (Exception error)
            {
                try { owner.UnpatchSelf(); }
                catch (Exception rollbackError)
                {
                    // A failed rollback must not prevent unrelated features from initializing.
                    // Available remains false, so any surviving hooks follow original behavior.
                    owners.Add(owner);
                    Log.LogError("Patch rollback failed for " + feature + ": " + rollbackError.GetBaseException().Message);
                }
                Errors[feature] = error.GetBaseException().Message;
                Log.LogError("Feature binding rejected: " + feature + ": " + Errors[feature]);
            }
        }

        private static HarmonyMethod Hook(string name) { return new HarmonyMethod(typeof(FeatureController), name); }
        private static void Patch(Harmony h, MethodInfo original, string prefix, string transpiler)
        {
            h.Patch(original, prefix: prefix == null ? null : Hook(prefix), transpiler: transpiler == null ? null : Hook(transpiler));
        }

        private static bool Active(int feature)
        {
            return Current != null && Current.Available[feature] && Current.Enabled[feature] && GameBindings.Ready;
        }

        private static void Trace(int feature, string message)
        {
            if (Current == null) return;
            int count = ++Current.Hits[feature];
            if (count <= 8) Current.Log.LogInfo("Observed " + feature + " #" + count + ": " + message);
        }

        private void TraceDefense(int mode, string message)
        {
            int count = ++defenseHits;
            if (count <= 8) Log.LogInfo("Observed defense(mode=" + mode + ") #" + count + ": " + message);
        }

        // The caller already performed hurt animation, rumble and hit recovery. Only this local Hit copy changes.
        // Defense reduction applies at the raw layer: elemental resistance (GetReducedDamage) and flat
        // defense (Stats.Defense.Final) run afterwards, so the observed HP loss shrinks by at least the tier.
        private static void ProtectLife(PenitentDamageArea __instance, ref Hit __0)
        {
            if (__instance.OwnerEntity != GameBindings.Player || Current == null) return;
            Trace(0, "incoming=" + __0.DamageAmount + "; enabled=" + Active(0));
            if (Active(0)) { __0.DamageAmount = 0; return; }
            if (!GameBindings.Ready || Current.DefenseMode != 1) return;
            float before = __0.DamageAmount;
            if (before <= 0f || float.IsNaN(before) || float.IsInfinity(before)) return;
            // Bleeding Heart style stocks overwrite DamageAmount in TakeDamage; the final CeilToInt
            // would round any scaled stock value back up, so leave the stock value untouched.
            if (Core.PenitenceManager != null && Core.PenitenceManager.UseStocksOfHealth) return;
            __0.DamageAmount = DefenseRules.Reduce(before, Current.DefenseTier);
            Current.TraceDefense(1, "reduce " + (Current.DefenseTier * 100f).ToString("0") + "%: incoming=" + before + "->" + __0.DamageAmount);
        }

        // Scope guarantees that prayers, reflected projectiles and unrelated enemy damage are not amplified.
        private static void SwordEnter(PenitentSword __instance, Hit __0, out bool __state)
        {
            var player = GameBindings.Player;
            __state = player != null && __instance.WeaponOwner == player && __0.AttackingEntity == player.gameObject;
            if (__state) swordDepth++;
        }

        private static Exception SwordExit(Exception __exception, bool __state)
        {
            if (__state) swordDepth--;
            return __exception;
        }

        private static void ScaleEnemyHit(EnemyDamageArea __instance, ref Hit __0)
        {
            var player = GameBindings.Player;
            if (swordDepth <= 0 || player == null || __0.AttackingEntity != player.gameObject || !(__instance.OwnerEntity is Enemy)) return;
            float before = __0.DamageAmount;
            if (Active(3) && before > 0 && !float.IsNaN(before) && !float.IsInfinity(before))
                __0.DamageAmount = Mathf.Min(before * Mathf.Clamp(Current.Multiplier, 1, 10), 1000000);
            Trace(3, "melee=" + before + "->" + __0.DamageAmount + "; enemy=" + __instance.OwnerEntity.name);
        }

        // These helpers replace access only at known call sites, never patch the global attribute setter/getter.
        public static float ReadPrayerAvailability(VariableAttribute resource)
        {
            if (Active(1) && ReferenceEquals(resource, GameBindings.Player.Stats.Fervour)) return float.MaxValue;
            return resource.Current;
        }

        public static void SetPrayerResource(VariableAttribute resource, float value)
        {
            var player = GameBindings.Player;
            bool own = player != null && ReferenceEquals(resource, player.Stats.Fervour);
            if (own) Trace(1, "prayer cost=" + (resource.Current - value) + "; enabled=" + Active(1));
            if (!(own && Active(1) && value < resource.Current)) resource.Current = value;
        }

        public static void SetHealingResource(VariableAttribute resource, float value)
        {
            var player = GameBindings.Player;
            bool flask = player != null && ReferenceEquals(resource, player.Stats.Flask);
            if (flask) Trace(2, "flask=" + resource.Current + "->" + value + "; enabled=" + Active(2));
            if (!(flask && Active(2) && value < resource.Current)) resource.Current = value;
        }

        private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> source, string accessor, string helper, int expected)
        {
            var code = new List<CodeInstruction>(source);
            var method = GameBindings.Require(typeof(VariableAttribute), accessor,
                accessor == "get_Current" ? typeof(float) : typeof(void),
                accessor == "get_Current" ? Type.EmptyTypes : new[] { typeof(float) });
            MethodInfo replacement = AccessTools.Method(typeof(FeatureController), helper);
            int count = 0;
            foreach (var instruction in code)
            {
                if (!instruction.Calls(method)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                count++;
            }
            if (count != expected) throw new InvalidOperationException(helper + ": expected " + expected + " call sites, found " + count);
            Current.Log.LogInfo("Verified IL: " + helper + ", sites=" + count);
            return code;
        }

        private static IEnumerable<CodeInstruction> PrayerAvailability(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, "get_Current", "ReadPrayerAvailability", 1); }
        private static IEnumerable<CodeInstruction> PrayerSpending(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, "set_Current", "SetPrayerResource", 1); }
        private static IEnumerable<CodeInstruction> FlaskSpending(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, "set_Current", "SetHealingResource", 4); }

        public void Dispose()
        {
            DisableAll();
            foreach (var owner in owners)
                try { owner.UnpatchSelf(); }
                catch (Exception error) { Log.LogError("Patch cleanup failed: " + error.GetBaseException().Message); }
            if (Current == this) Current = null;
        }
    }
}
