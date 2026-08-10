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
        }
    }

    public bool TryCleanupOwned()
    {
        lock (Sync)
        {
            if (!ReferenceEquals(ownerToken, token))
            {
                return true;
            }

            cleanupPending = true;
            return TryCleanupShared();
        }
    }

    public bool TryCleanupPending()
    {
        lock (Sync)
        {
            return cleanupPending && TryCleanupShared();
        }
    }

    private static bool TryCleanupShared()
    {
        if (!SharedOwner.TryCleanup())
        {
            return false;
        }

        ownerToken = null;
        cleanupPending = false;
        return true;
    }
}
