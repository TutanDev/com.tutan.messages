using Tutan.Messages;

namespace Tutan.Messages.Samples.BasicPubSub
{
    public struct PlayerScored : IEvent
    {
        public int Points;
        public float Timestamp;
    }

    public struct ResetScore : ICommand { }
}
