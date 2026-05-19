# Threaded Dispatch Sample

Publishes messages from a worker thread and consumes them on the main thread.

1. Create an empty GameObject in your scene.
2. Add the `ThreadedDispatchSample` component to it.
3. Press Play and press Space repeatedly.
4. Watch the Console — every `WorkCompleted` log line shows that the handler
   runs on the main thread (same managed thread id as Update), even though
   the work and `Enqueue` happened on a `Task.Run` worker thread.

This is the safe pattern for network callbacks, async work, decoders, etc.
