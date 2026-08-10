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
