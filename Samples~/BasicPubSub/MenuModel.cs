namespace Tutan.Messages.Samples.BasicPubSub
{
    public sealed class MenuModel : ICommandHandler<StartGame>
    {
        /// <summary>Handles <see cref="StartGame"/>. Always invoked on the main thread
        /// (the bus drains queued commands in <c>LateUpdate</c>), so publishing an event
        /// from here is safe.</summary>
        public void Handle(ref StartGame cmd)
        {
            // Broadcast the result. The model neither knows nor cares who is listening.
            EventBus.Publish(new GameStarted());
        }
    }
}
