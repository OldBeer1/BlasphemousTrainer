namespace BlasphemousTrainer
{
    // Bound stale mappings even if a game/remap screen mutates them without a public event.
    internal sealed class ControllerMapCache
    {
        private float nextRefresh = -1;
        internal void Invalidate() { nextRefresh = -1; }
        internal bool Due(float now, bool deviceChanged)
        {
            if (!deviceChanged && nextRefresh >= 0 && now < nextRefresh) return false;
            nextRefresh = now + .5f;
            return true;
        }
    }
}
