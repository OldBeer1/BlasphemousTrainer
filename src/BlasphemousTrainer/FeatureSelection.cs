using System;

namespace BlasphemousTrainer
{
    internal sealed partial class FeatureController
    {
        internal bool SetEnabled(int index, bool value)
        {
            if (index < 0 || index >= Enabled.Length) return false;
            if (value && (!Available[index] || !GameBindings.Ready)) return false;
            // Commit a timer interval under the old switch state before changing it.
            if (index == MenuFeature || index == FreezeFeature) SamplePlaytime();
            Enabled[index] = value;
            if (index == 0 && value) DefenseMode = 0;
            Log.LogInfo("Feature " + index + " enabled=" + value);
            return true;
        }
        internal bool SetDefenseMode(int mode)
        {
            if (mode < 0 || mode > 2) return false;
            if (mode != 0 && (!Available[0] || !GameBindings.Ready)) return false;
            Enabled[0] = mode == 2;
            DefenseMode = mode == 1 ? 1 : 0;
            Log.LogInfo("Defense mode=" + mode);
            return true;
        }
        internal void Toggle(int index) { if (index >= 0 && index < Enabled.Length) SetEnabled(index, !Enabled[index]); }
        internal void DisableAll()
        {
            SamplePlaytime();
            Array.Clear(Enabled, 0, Enabled.Length);
            DefenseMode = 0;
            Log.LogInfo("All features disabled.");
        }
    }
}
