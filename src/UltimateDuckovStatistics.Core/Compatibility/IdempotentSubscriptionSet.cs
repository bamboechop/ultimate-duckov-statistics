namespace UltimateDuckovStatistics.Core.Compatibility;

public sealed class SubscriptionBinding
{
    public SubscriptionBinding(Action subscribe, Action unsubscribe)
    {
        Subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
        Unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
    }

    internal Action Subscribe { get; }

    internal Action Unsubscribe { get; }
}

public sealed class IdempotentSubscriptionSet
{
    private readonly List<SubscriptionBinding> registered = new();

    public bool IsActive { get; private set; }

    public bool HasPendingCleanup => registered.Count > 0 && !IsActive;

    public bool Activate(IEnumerable<SubscriptionBinding> bindings)
    {
        if (bindings == null)
        {
            throw new ArgumentNullException(nameof(bindings));
        }

        if (IsActive)
        {
            return false;
        }

        if (registered.Count > 0)
        {
            throw new InvalidOperationException("Subscription cleanup must succeed before reactivation.");
        }

        try
        {
            foreach (var binding in bindings)
            {
                binding.Subscribe();
                registered.Add(binding);
            }

            IsActive = true;
            return true;
        }
        catch (Exception activationException)
        {
            try
            {
                Deactivate();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Subscription activation and rollback both failed.",
                    activationException,
                    cleanupException);
            }

            throw;
        }
    }

    public bool Deactivate()
    {
        if (registered.Count == 0)
        {
            IsActive = false;
            return false;
        }

        IsActive = false;
        var failures = new List<Exception>();
        var retained = new List<SubscriptionBinding>();
        for (var index = registered.Count - 1; index >= 0; index--)
        {
            var binding = registered[index];
            try
            {
                binding.Unsubscribe();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                retained.Add(binding);
            }
        }

        registered.Clear();
        registered.AddRange(retained);
        if (failures.Count > 0)
        {
            throw new AggregateException("One or more subscriptions could not be removed.", failures);
        }

        return true;
    }
}
