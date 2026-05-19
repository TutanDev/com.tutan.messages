using System;

namespace Tutan.MessageBus
{
    /// <summary>
    /// Opaque handle returned on Subscribe. Used for deterministic unsubscription.
    /// Lightweight struct — no GC pressure.
    /// </summary>
    /// <remarks>
    /// <c>SubscriptionToken</c> contains a <see cref="System.Type"/> field and is
    /// therefore <b>not</b> <c>unmanaged</c>. It cannot be stored in a
    /// <c>NativeArray</c> or passed to Burst-compiled code.
    /// </remarks>
    public readonly struct SubscriptionToken : IEquatable<SubscriptionToken>
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
