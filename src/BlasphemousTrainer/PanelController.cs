using System;
using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace BlasphemousTrainer
{
    // Action ids come from this game's RewiredConsts.Action, not physical button numbers.
    internal sealed class PanelController
    {
        internal Joystick Device;
        private readonly List<ActionElementMap> maps = new List<ActionElementMap>();
        private readonly ControllerMapCache cache = new ControllerMapCache();
        internal void InvalidateMaps() { cache.Invalidate(); }
        internal string Status = "未连接手柄";
        internal bool Connected { get { return Device != null; } }

        internal bool Refresh()
        {
            Joystick previous = Device;
            Device = null;
            if (ReInput.isReady)
            {
                var player = ReInput.players.GetPlayer(0);
                Device = player.controllers.GetLastActiveController(ControllerType.Joystick) as Joystick;
                if (Device == null && player.controllers.joystickCount > 0) Device = player.controllers.Joysticks[0];
                if (cache.Due(Time.unscaledTime, !ReferenceEquals(previous, Device)))
                {
                    maps.Clear();
                    if (Device != null)
                    foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Joystick))
                        if (map.controllerId == Device.id)
                            foreach (ActionElementMap entry in map.AllMaps) maps.Add(entry);
                }
            }
            else { maps.Clear(); cache.Invalidate(); }
            Status = Device == null ? "未连接手柄" : Device.name;
            return !ReferenceEquals(previous, Device);
        }

        internal float Read(int action)
        {
            float result = 0;
            if (Device == null) return 0;
            foreach (var map in maps)
            {
                if (!map.enabled || map.actionId != action) continue;
                float value;
                if (map.elementType == ControllerElementType.Button)
                    value = Device.GetButtonById(map.elementIdentifierId) ? (map.axisContribution == Pole.Negative ? -1 : 1) : 0;
                else
                {
                    value = Device.GetAxisById(map.elementIdentifierId);
                    if (map.axisRange != AxisRange.Full)
                    {
                        value = map.axisRange == AxisRange.Positive ? Mathf.Max(0, value) : Mathf.Max(0, -value);
                        if (map.axisContribution == Pole.Negative) value = -value;
                    }
                    if (map.invert) value = -value;
                }
                if (Mathf.Abs(value) > Mathf.Abs(result)) result = value;
            }
            return result;
        }
        internal bool HasAction(int action) { return maps.Exists(m => m.enabled && m.actionId == action); }
        internal string Label(int action)
        {
            var map = maps.Find(m => m.enabled && m.actionId == action);
            return map == null ? "未映射" : map.elementIdentifierName;
        }
        internal bool Neutral()
        {
            if (Device == null) return true;
            if (Device.GetAnyButton()) return false;
            // Triggers can rest at -1. Only navigation/stick axes participate in release gating.
            foreach (int action in new[] { 0, 4, 20, 21, 48, 49 }) if (Mathf.Abs(Read(action)) > .3f) return false;
            return true;
        }
        internal int FindStick(bool left)
        {
            if (Device == null) return -1;
            foreach (var item in Device.ButtonElementIdentifiers)
            {
                string name = item.name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
                bool side = name.Contains(left ? "left" : "right");
                if ((side && (name.Contains("stick") || name.Contains("thumb"))) || name == (left ? "l3" : "r3")) return item.id;
            }
            return -1;
        }
        internal bool Held(int id) { return Device != null && id >= 0 && Device.GetButtonById(id); }
    }
}
