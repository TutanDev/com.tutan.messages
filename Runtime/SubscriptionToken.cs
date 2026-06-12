using System;

namespace Tutan.Messages
{
    /// <summary>
    /// Internal identity of a subscription (id + message type). Carried inside the
    /// public <see cref="Subscription"/> handle; consumers never touch it directly —
    /// deterministic unsubscription goes through <see cref="Subscription.Dispose"/>.
    /// Lightweight struct — no GC pressure.
    /// </summary>
    internal readonly struct SubscriptionToken : IEquatable<SubscriptionToken>
    {
        public readonly int Id;
        internal readonly Type MessageType;

        internal SubscriptionToken(int id, Type messageType)
        {
            Id = id;
            MessageType = messageType;
        }

        public bool IsValid => Id != 0;
        public bool Equals(SubscriptionToken other) => Id == other.Id && MessageType == other.MessageType;
        public override bool Equals(object obj) => obj is SubscriptionToken t && Equals(t);
        public override int GetHashCode() => HashCode.Combine(Id, MessageType);
        public static bool operator ==(SubscriptionToken a, SubscriptionToken b) => a.Equals(b);
        public static bool operator !=(SubscriptionToken a, SubscriptionToken b) => !a.Equals(b);
    }
}
