using System.Globalization;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeItemUseAdapter : IDisposable
{
    internal const string AdapterVersion = "native-item-use/2.3.30";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(2);
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Action<ItemUseCompletion> completionHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly ItemUseCorrelator correlator;
    private readonly SubscriptionGate subscriptionGate = new();
    private readonly NativeRaidContext raidContext = new();
    private DateTime nextExpiryUtc = DateTime.MinValue;

    public NativeItemUseAdapter(
        Func<string> saveGenerationIdProvider,
        Action<ItemUseCompletion> completionHandler,
        Action<string> diagnosticHandler)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider
            ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.completionHandler = completionHandler ?? throw new ArgumentNullException(nameof(completionHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        correlator = new ItemUseCorrelator(() => Guid.NewGuid().ToString("N"));
    }

    public bool IsSubscribed => subscriptionGate.IsActive;

    public void Subscribe()
    {
        if (!subscriptionGate.TryActivate())
        {
            diagnosticHandler("Duplicate item-use subscription request ignored.");
            return;
        }

        Item.onUseStatic += OnUseStarted;
        UsageUtilities.OnItemUsedStaticEvent += OnUsageSucceeded;
        CA_UseItem.OnItemUsedByPlayer += OnMainPlayerUseCompleted;
        raidContext.Subscribe();
        nextExpiryUtc = DateTime.UtcNow.AddSeconds(30);
        diagnosticHandler("Native item-use hooks subscribed.");
    }

    public void Tick(DateTime nowUtc)
    {
        if (!subscriptionGate.IsActive || nowUtc < nextExpiryUtc)
        {
            return;
        }

        var expired = correlator.ExpireBefore(nowUtc.Subtract(PendingLifetime));
        if (expired > 0)
        {
            diagnosticHandler($"Expired {expired} incomplete item-use correlation(s) without counting them.");
        }

        nextExpiryUtc = nowUtc.AddSeconds(30);
    }

    public void ResetPending()
    {
        correlator.Clear();
        diagnosticHandler("Pending item-use correlations cleared for a profile transition.");
    }

    public void Dispose()
    {
        if (!subscriptionGate.TryDeactivate())
        {
            return;
        }

        Item.onUseStatic -= OnUseStarted;
        UsageUtilities.OnItemUsedStaticEvent -= OnUsageSucceeded;
        CA_UseItem.OnItemUsedByPlayer -= OnMainPlayerUseCompleted;
        raidContext.Dispose();
        correlator.Clear();
        diagnosticHandler("Native item-use hooks unsubscribed.");
    }

    private void OnUseStarted(Item item, object user)
    {
        try
        {
            if (ReferenceEquals(item, null)
                || user is not CharacterMainControl character
                || !character.IsMainCharacter)
            {
                return;
            }

            var generationId = saveGenerationIdProvider();
            if (string.IsNullOrWhiteSpace(generationId))
            {
                diagnosticHandler("Item use ignored because no save generation is active.");
                return;
            }

            var typeId = item.TypeID;
            var displayName = ReadDisplayName(item, typeId);
            correlator.Begin(new ItemUseSnapshot
            {
                RuntimeItemId = item.GetInstanceID(),
                ItemId = $"duckov:item:{typeId.ToString(CultureInfo.InvariantCulture)}",
                DisplayName = displayName,
                Classification = NativeItemClassifier.Describe(item),
                Stackable = item.Stackable,
                StackCount = item.StackCount,
                UsesDurability = item.UseDurability,
                Durability = item.Durability,
                TimestampUtc = DateTime.UtcNow,
                SaveGenerationId = generationId,
                RunId = raidContext.CurrentRunId,
                MapId = NativeRaidContext.GetMapId(),
                GameVersion = Application.version ?? string.Empty,
                GameBuild = "24013657",
                GameplayContext = NativeRaidContext.GetGameplayContext(),
                IntegrityTags = NativeIntegrityProbe.Read(),
                AdapterCapability = AdapterCapabilityState.Supported,
                AdapterVersion = AdapterVersion
            });
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to begin item-use correlation: {exception.GetType().Name}");
        }
    }

    private void OnUsageSucceeded(Item item)
    {
        try
        {
            if (ReferenceEquals(item, null))
            {
                return;
            }

            if (!correlator.MarkSuccessful(item.GetInstanceID(), item.Durability))
            {
                diagnosticHandler("Successful item-use hook had no main-player pre-use correlation; ignored.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to mark item use successful: {exception.GetType().Name}");
        }
    }

    private void OnMainPlayerUseCompleted(Item item)
    {
        try
        {
            if (ReferenceEquals(item, null))
            {
                return;
            }

            var result = correlator.CompleteByMainPlayer(
                item.GetInstanceID(),
                TryReadFinalStack(item),
                TryReadFinalDurability(item),
                DateTime.UtcNow);
            completionHandler(result);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to complete item-use correlation: {exception.GetType().Name}");
        }
    }

    private static int? TryReadFinalStack(Item item)
    {
        try
        {
            return item.StackCount;
        }
        catch
        {
            return null;
        }
    }

    private static double? TryReadFinalDurability(Item item)
    {
        try
        {
            return item.Durability;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadDisplayName(Item item, int typeId)
    {
        try
        {
            var displayName = item.DisplayName;
            return string.IsNullOrWhiteSpace(displayName)
                ? $"Unknown item {typeId.ToString(CultureInfo.InvariantCulture)}"
                : displayName;
        }
        catch
        {
            return $"Unknown item {typeId.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
