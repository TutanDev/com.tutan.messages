using Tutan.MessageBus;

namespace Tutan.MessageBus.Samples.BasicPubSub
{
    public struct PlayerScored : IEvent
    {
        public int Points;
        public float Timestamp;
    }

    public struct ResetScore : ICommand { }
}
