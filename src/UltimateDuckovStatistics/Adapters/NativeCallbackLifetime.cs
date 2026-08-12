using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeCallbackLifetime
{
    private readonly IdempotentSubscriptionSet subscriptions = new();

    public bool DisposalStarted { get; private set; }

    public bool IsActive => subscriptions.IsActive;

    public bool CanHandleCallbacks => !DisposalStarted && subscriptions.IsActive;

    public bool Activate(IEnumerable<SubscriptionBinding> bindings)
    {
        if (DisposalStarted)
        {
            throw new ObjectDisposedException(nameof(NativeCallbackLifetime));
        }

        return subscriptions.Activate(bindings);
    }

    public bool BeginDisposal()
    {
        var firstAttempt = !DisposalStarted;
        DisposalStarted = true;
        return firstAttempt;
    }

    public bool TryCleanup(Func<bool> detachInstanceSubscriptions, out Exception? staticCleanupFailure)
    {
        if (detachInstanceSubscriptions == null)
        {
            throw new ArgumentNullException(nameof(detachInstanceSubscriptions));
        }

        BeginDisposal();
        detachInstanceSubscriptions();

        var staticCleaned = true;
        staticCleanupFailure = null;
        try
        {
            subscriptions.Deactivate();
        }
        catch (Exception exception)
        {
            staticCleaned = false;
            staticCleanupFailure = exception;
        }

        // Detach again after static cleanup. A hostile/custom event remover may
        // synchronously invoke a retained callback while removal is attempted.
        var instanceCleaned = detachInstanceSubscriptions();
        return staticCleaned && instanceCleaned && !subscriptions.HasPendingCleanup;
    }

    public Action Guard(Action callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return () =>
        {
            if (CanHandleCallbacks)
            {
                callback();
            }
        };
    }

    public Action<T> Guard<T>(Action<T> callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return value =>
        {
            if (CanHandleCallbacks)
            {
                callback(value);
            }
        };
    }

    public Action<T1, T2> Guard<T1, T2>(Action<T1, T2> callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return (first, second) =>
        {
            if (CanHandleCallbacks)
            {
                callback(first, second);
            }
        };
    }

    public Action<T1, T2, T3> Guard<T1, T2, T3>(Action<T1, T2, T3> callback)
    {
        if (callback == null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        return (first, second, third) =>
        {
            if (CanHandleCallbacks)
            {
                callback(first, second, third);
            }
        };
    }
}
