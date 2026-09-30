namespace BlasphemousTrainer
{
    // IMGUI still draws hover/pressed states. Activation uses one raw input sequence,
    // independent of IMGUI keyboard focus and control capture.
    internal sealed class PointerClick
    {
        private int pressed = -1;
        private float startX, startY;
        internal void Cancel() { pressed = -1; }
        internal int Step(bool down, bool up, bool held, int hit, float x, float y)
        {
            if (down) { pressed = hit; startX = x; startY = y; }
            if (pressed >= 0 && ((x - startX) * (x - startX) + (y - startY) * (y - startY) > 64)) Cancel();
            if (up)
            {
                int result = pressed >= 0 && pressed == hit ? pressed : -1;
                Cancel();
                return result;
            }
            if (!held && !down) Cancel();
            return -1;
        }
    }
}
