namespace UltimateDuckovStatistics.Core.Compatibility;

public interface IRetryableCleanup
{
    bool TryCleanup();
}

public sealed class RetryableCleanupOwner<T>
    where T : class, IRetryableCleanup
{
    public T? Value { get; private set; }

    public void Assign(T value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (Value != null)
        {
            throw new InvalidOperationException("Owned cleanup must succeed before assigning a replacement.");
        }

        Value = value;
    }

    public bool TryCleanup()
    {
        if (Value == null)
        {
            return true;
        }

        if (!Value.TryCleanup())
        {
            return false;
        }

        Value = null;
        return true;
    }
}

public sealed class ProcessLifetimeCleanupOwner<T>
    where T : class, IRetryableCleanup
{
    private static readonly object Sync = new();
    private static readonly RetryableCleanupOwner<T> SharedOwner = new();
    private static object? ownerToken;
    private static bool cleanupPending;
    private static Action? pendingCleanupCompletion;
    private readonly object token = new();

    public T? OwnedValue
    {
        get
        {
            lock (Sync)
            {
                return ReferenceEquals(ownerToken, token) ? SharedOwner.Value : null;
            }
        }
    }

    public bool HasValue
    {
        get
        {
            lock (Sync)
            {
                return SharedOwner.Value != null;
            }
        }
    }

    public bool HasPendingCleanup
    {
        get
        {
            lock (Sync)
            {
                return cleanupPending;
            }
        }
    }

    public void Assign(T value)
    {
        lock (Sync)
        {
            SharedOwner.Assign(value);
            ownerToken = token;
            cleanupPending = false;
            pendingCleanupCompletion = null;
        }
    }

    public bool TryCleanupOwned() => TryCleanupOwned(null);

    public bool TryCleanupOwned(Action? completeAfterRetriedCleanup)
    {
        lock (Sync)
        {
            if (!ReferenceEquals(ownerToken, token))
            {
                return true;
            }

            cleanupPending = true;
            if (!SharedOwner.TryCleanup())
            {
                pendingCleanupCompletion ??= completeAfterRetriedCleanup;
                return false;
            }

            CompleteSharedCleanup();
            return true;
        }
    }

    public bool TryCleanupPending()
    {
        lock (Sync)
        {
            if (!cleanupPending || !SharedOwner.TryCleanup())
            {
                return false;
            }

            CompleteSharedCleanup();
            return true;
        }
    }

    private static void CompleteSharedCleanup()
    {
        var completion = pendingCleanupCompletion;
        pendingCleanupCompletion = null;
        ownerToken = null;
        cleanupPending = false;
        completion?.Invoke();
    }
}
