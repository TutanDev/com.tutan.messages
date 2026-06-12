using System;

namespace Tutan.Messages
{
    /// <summary>
    /// Non-generic seam that lets <see cref="Subscription"/> unsubscribe without
    /// knowing the bus's base message type. Implemented by <see cref="MessageBus{TBase}"/>.
    /// </summary>
    internal interface ISubscriptionOwner
    {
        bool Unsubscribe(SubscriptionToken token);
    }

    /// <summary>
    /// Disposable handle returned by <c>Subscribe</c> — the one way to end a
    /// subscription. <see cref="Dispose"/> unsubscribes, so a subscription can be
    /// held in a field and disposed explicitly, ride a <c>using</c> scope, be
    /// collected in a <see cref="SubscriptionBag"/>, or be tied to a GameObject's
    /// lifetime with <c>AddTo(component)</c>.
    /// <para>
    /// Zero-allocation: the handle is a struct around the subscription's identity
    /// and a reference to the bus instance that issued it.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Mutable struct caveat: disposing a <i>copy</i> unsubscribes the handler but
    /// cannot clear the original's fields. That is safe — a second
    /// <see cref="Dispose"/> through any copy finds the token already removed and
    /// is a no-op — but <see cref="IsActive"/> is only trustworthy on the copy you
    /// disposed through.
    /// </remarks>
    public struct Subscription : IDisposable
    {
        ISubscriptionOwner _owner;
        SubscriptionToken _token;

        internal Subscription(ISubscriptionOwner owner, SubscriptionToken token)
        {
            _owner = owner;
            _token = token;
        }

        /// <summary>The wrapped token. Internal — identity is an implementation detail.</summary>
        internal SubscriptionToken Token => _token;

        /// <summary>False once this handle has been disposed (or was never issued by a bus).</summary>
        public bool IsActive => _owner != null && _token.IsValid;

        /// <summary>
        /// Unsubscribe the handler. Idempotent. Main thread only (same contract as
        /// <c>Subscribe</c>); safe to call during dispatch and safe to call after
        /// the issuing bus was <c>Reset</c> — that case is a no-op.
        /// </summary>
        public void Dispose()
        {
            var owner = _owner;
            if (owner == null) return;
            _owner = null;
            owner.Unsubscribe(_token);
            _token = default;
        }
    }
}
