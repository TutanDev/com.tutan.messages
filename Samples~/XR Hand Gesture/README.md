# XR Hand Gesture Sample

A representative XR pattern: one input source, many decoupled consumers.

1. Create a GameObject and add `HandGestureSimulator` to it.
2. Create three more GameObjects and add `GestureMenu`, `GestureAnalytics`,
   and `GestureHaptics` — one per object (or all on one, your call).
3. Press Play. Synthetic gestures fire on a timer; each subscriber reacts to
   the ones it cares about.
4. Disable any subscriber GameObject during play — the others keep working.
   The publisher has no idea.

Replace `HandGestureSimulator` with your XR Hands / Meta SDK / OpenXR
hand-tracking pipeline; the subscribers don't change.
