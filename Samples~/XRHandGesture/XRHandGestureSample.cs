using Tutan.Messages;

namespace Tutan.Messages.Samples.XRHandGesture
{
    public struct HandGesture : IEvent
    {
        public int HandIndex;        // 0 = left, 1 = right
        public int GestureId;        // mapped from your enum
        public float Confidence;
    }

    public static class Gestures
    {
        public const int Pinch = 1;
        public const int OpenPalm = 2;
        public const int Fist = 3;
    }
}
