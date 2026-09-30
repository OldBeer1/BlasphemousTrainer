using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore.Attributes.Logic;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Tools.Items;
using UnityEngine;

namespace BlasphemousTrainer
{
    // Pooled objects are tagged only inside verified player-prayer creation paths.
    public sealed class TrainerPrayerSource : MonoBehaviour
    {
        internal Penitent Owner;
    }

    internal sealed partial class FeatureController
    {
        internal float TearsMultiplier = 1, MoveMultiplier = 1, JumpMultiplier = 1, PrayerMultiplier = 1;
        [ThreadStatic] private static int prayerCreationDepth;

        private void InstallExtensions()
        {
            InstallGroup(4, h => Patch(h, GameBindings.Require(typeof(Penitent), "GetPurge", typeof(void), typeof(Enemy)), null, "RewardSpending"));
            InstallGroup(5, h => Patch(h, GameBindings.Require(typeof(PlatformCharacterController), "Update", typeof(void)), null, "MovementReads"));
            InstallGroup(6, h => Patch(h, GameBindings.Require(typeof(PlatformCharacterController), "Update", typeof(void)), null, "JumpRead"));
            InstallGroup(7, h => {
                foreach (Type type in new[] { typeof(PenitentCrawlerOrbsEffect), typeof(PenitentLightBeamEffect) })
                    h.Patch(GameBindings.Require(type, "OnApplyEffect", typeof(bool)), prefix: Hook("PrayerCreationEnter"), finalizer: Hook("PrayerCreationExit"));
                h.Patch(GameBindings.Require(typeof(ToxicCloudEffect), "InstantiateToxicCloud", typeof(void)), prefix: Hook("PrayerCreationEnter"), finalizer: Hook("PrayerCreationExit"));
                h.Patch(GameBindings.Require(typeof(PoolManager.ObjectInstance), "Reuse", typeof(void), typeof(Vector3), typeof(Quaternion)), prefix: Hook("TagPrayerObject"));
                Patch(h, GameBindings.Require(typeof(EnemyDamageArea), "TakeDamageAmount", typeof(void), typeof(Hit)), "ScalePrayerHit", null);
            });
        }

        internal string AddTears(string input)
        {
            if (!GameBindings.Ready || Core.SkillManager == null) return "玩家未就绪，未调整。";
            var resource = GameBindings.Player.Stats.Purge;
            int amount; float expected; string error;
            if (!TrainerRules.TryAdjustment(input, resource.Current, resource.CurrentMax, out amount, out expected, out error)) return error;
            // Revalidate at execution, then use the original setter/HUD event outside GetPurge.
            Core.SkillManager.AddPurgePoints(amount);
            if (resource.Current != expected) return "余额与预期不一致，请核对当前余额后再操作。";
            return (amount > 0 ? "已增加 " : "已减少 ") + Math.Abs((long)amount) + " 赎罪之泪（会随游戏保存）。";
        }
        public static void SetReward(VariableAttribute resource, float value)
        {
            if (Active(4) && ReferenceEquals(resource, GameBindings.Player.Stats.Purge))
                value = TrainerRules.Reward(resource.Current, value, resource.CurrentMax, Current.TearsMultiplier);
            resource.Current = value;
        }
        private static IEnumerable<CodeInstruction> RewardSpending(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, "set_Current", "SetReward", 1); }

        private static bool OwnController(PlatformCharacterController controller, int feature)
        { return Active(feature) && controller == GameBindings.Player.PlatformCharacterController; }
        public static float ReadMoveSpeed(PlatformCharacterController controller)
        { return controller.MaxWalkingSpeed * (OwnController(controller, 5) ? TrainerRules.Clamp(Current.MoveMultiplier, 1, 2) : 1); }
        public static float ReadMoveAcceleration(PlatformCharacterController controller)
        { return controller.HorizontalMovingAcc * (OwnController(controller, 5) ? TrainerRules.Clamp(Current.MoveMultiplier, 1, 2) : 1); }
        public static float ReadJump(PlatformCharacterController controller)
        { return controller.JumpingSpeed * (OwnController(controller, 6) ? TrainerRules.Clamp(Current.JumpMultiplier, 1, 1.5f) : 1); }

        private static IEnumerable<CodeInstruction> ReplaceControllerRead(IEnumerable<CodeInstruction> source, string getter, string helper, int expected)
        {
            var code = new List<CodeInstruction>(source);
            var method = GameBindings.Require(typeof(PlatformCharacterController), getter, typeof(float));
            int count = 0;
            foreach (var instruction in code)
                if (instruction.Calls(method))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(FeatureController), helper);
                    count++;
                }
            if (count != expected) throw new InvalidOperationException(helper + ": call site count=" + count);
            Current.Log.LogInfo("Verified IL: " + helper + ", sites=" + count);
            return code;
        }
        private static IEnumerable<CodeInstruction> MovementReads(IEnumerable<CodeInstruction> source)
        { return ReplaceControllerRead(ReplaceControllerRead(source, "get_MaxWalkingSpeed", "ReadMoveSpeed", 1), "get_HorizontalMovingAcc", "ReadMoveAcceleration", 2); }
        private static IEnumerable<CodeInstruction> JumpRead(IEnumerable<CodeInstruction> source)
        { return ReplaceControllerRead(source, "get_JumpingSpeed", "ReadJump", 1); }

        private static void PrayerCreationEnter(out bool __state)
        { __state = GameBindings.Ready; if (__state) prayerCreationDepth++; }
        private static Exception PrayerCreationExit(Exception __exception, bool __state)
        { if (__state) prayerCreationDepth--; return __exception; }
        private static void TagPrayerObject(PoolManager.ObjectInstance __instance)
        {
            if (__instance == null || __instance.GameObject == null) return;
            var marker = __instance.GameObject.GetComponent<TrainerPrayerSource>();
            if (marker != null) marker.Owner = null; // Clear attribution on every reuse, including enemy reuse.
            if (prayerCreationDepth <= 0 || !GameBindings.Ready) return;
            if (marker == null) marker = __instance.GameObject.AddComponent<TrainerPrayerSource>();
            marker.Owner = GameBindings.Player;
        }
        private static void ScalePrayerHit(EnemyDamageArea __instance, ref Hit __0)
        {
            if (!Active(7) || swordDepth > 0 || !(__instance.OwnerEntity is Enemy) || __0.AttackingEntity == null) return;
            var marker = __0.AttackingEntity.GetComponentInParent<TrainerPrayerSource>();
            if (marker == null || marker.Owner == null || marker.Owner != GameBindings.Player) return;
            float before = __0.DamageAmount;
            if (before > 0 && !float.IsNaN(before) && !float.IsInfinity(before))
                __0.DamageAmount = Mathf.Min(before * TrainerRules.Clamp(Current.PrayerMultiplier, 1, 10), 1000000);
            Trace(7, "prayer=" + before + "->" + __0.DamageAmount + "; source=" + __0.AttackingEntity.name);
        }
    }
}
