namespace SaltExtraDebug;

// Keep the closing gesture blocked even after its menu has been deactivated.
// This is separate from Player.inputBlockCount: we never acquire/release a
// game's input lock and cannot accidentally release another menu's lock.
internal sealed class DebugMenuInputState
{
    private bool _wasVisible;
    private int _closedFrame = -1;
    private int _blockedFrame = -1;
    private int _heldAtClose;

    internal void Closed(int frame, int heldMouseButtons)
    {
        _wasVisible = false;
        _closedFrame = _blockedFrame = frame;
        _heldAtClose |= heldMouseButtons;
    }

    internal bool ShouldBlock(bool visible, int frame, int heldMouseButtons, int pressedMouseButtons)
    {
        if (visible)
        {
            _wasVisible = true;
            return true;
        }

        // Also handles a replaced/destroyed UI if no disable callback arrived.
        if (_wasVisible)
            Closed(frame, heldMouseButtons);

        // A new press proves that the previous gesture ended, even if input
        // wasn't sampled while another UI/loading screen was in control.
        if (frame != _closedFrame)
            _heldAtClose &= ~pressedMouseButtons;
        if (_heldAtClose != 0)
        {
            _heldAtClose &= heldMouseButtons;
            _blockedFrame = frame; // Include the release frame itself.
        }
        return _heldAtClose != 0 || _blockedFrame == frame;
    }
}
