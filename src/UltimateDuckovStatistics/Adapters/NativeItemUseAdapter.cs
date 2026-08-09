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
    private readonly Func<ItemUseCompletion, bool> completionHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly IHealingAttributionObserver? healingObserver;
    private readonly ItemUseCorrelator correlator;
    private readonly SubscriptionGate subscriptionGate = new();
    private readonly NativeRaidContext raidContext = new();
    private DateTime nextExpiryUtc = DateTime.MinValue;

    public NativeItemUseAdapter(
        Func<string> saveGenerationIdProvider,
        Func<ItemUseCompletion, bool> completionHandler,
        Action<string> diagnosticHandler,
        IHealingAttributionObserver? healingObserver = null)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider
            ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.completionHandler = completionHandler ?? throw new ArgumentNullException(nameof(completionHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        this.healingObserver = healingObserver;
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

        CharacterMainControl.OnMainCharacterStartUseItem += OnMainPlayerUseStarted;
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
        var expiredHealing = healingObserver?.ExpirePendingBefore(nowUtc.Subtract(PendingLifetime)) ?? 0;
        if (expired > 0)
        {
            diagnosticHandler($"Expired {expired} incomplete item-use correlation(s) without counting them.");
        }

        if (expiredHealing > 0 && expiredHealing != expired)
        {
            diagnosticHandler($"Expired {expiredHealing} incomplete healing-attribution context(s).");
        }

        nextExpiryUtc = nowUtc.AddSeconds(30);
    }

    public void ResetPending()
    {
        correlator.Clear();
        healingObserver?.Reset();
        diagnosticHandler("Pending item-use correlations cleared for a profile transition.");
    }

    public void Dispose()
    {
        if (!subscriptionGate.TryDeactivate())
        {
            return;
        }

        CharacterMainControl.OnMainCharacterStartUseItem -= OnMainPlayerUseStarted;
        Item.onUseStatic -= OnUseStarted;
        UsageUtilities.OnItemUsedStaticEvent -= OnUsageSucceeded;
        CA_UseItem.OnItemUsedByPlayer -= OnMainPlayerUseCompleted;
        raidContext.Dispose();
        correlator.Clear();
        healingObserver?.Reset();
        diagnosticHandler("Native item-use hooks unsubscribed.");
    }

    private void OnMainPlayerUseStarted(Item item)
    {
        try
        {
            if (ReferenceEquals(item, null))
            {
                return;
            }

            BeginUse(item, replaceExisting: true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to prepare item-use correlation: {exception.GetType().Name}");
        }
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

            BeginUse(item, replaceExisting: false);
            healingObserver?.BeginApplication(item.GetInstanceID());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to begin item-use correlation: {exception.GetType().Name}");
        }
    }

    private void OnUsageSucceeded(Item item)
    {
        int? runtimeItemId = null;
        try
        {
            if (ReferenceEquals(item, null))
            {
                return;
            }

            runtimeItemId = item.GetInstanceID();
            if (!correlator.MarkSuccessful(runtimeItemId.Value, item.Durability))
            {
                diagnosticHandler("Successful item-use hook had no main-player pre-use correlation; ignored.");
            }
            else
            {
                healingObserver?.MarkSuccessful(runtimeItemId.Value);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to mark item use successful: {exception.GetType().Name}");
        }
        finally
        {
            if (runtimeItemId.HasValue)
            {
                healingObserver?.EndApplication(runtimeItemId.Value);
            }
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

            var runtimeItemId = item.GetInstanceID();
            var result = correlator.CompleteByMainPlayer(
                runtimeItemId,
                TryReadFinalStack(item),
                TryReadFinalDurability(item),
                DateTime.UtcNow);
            var persisted = completionHandler(result);
            healingObserver?.CompleteUse(runtimeItemId, persisted ? result.NormalizedEvent : null);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            diagnosticHandler($"Failed to complete item-use correlation: {exception.GetType().Name}");
        }
    }

    private void BeginUse(Item item, bool replaceExisting)
    {
        var runtimeItemId = item.GetInstanceID();
        if (!replaceExisting && correlator.Contains(runtimeItemId))
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
        var snapshot = new ItemUseSnapshot
        {
            RuntimeItemId = runtimeItemId,
            ItemId = $"duckov:item:{typeId.ToString(CultureInfo.InvariantCulture)}",
            DisplayName = ReadDisplayName(item, typeId),
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
        };
        correlator.Begin(snapshot);
        healingObserver?.BeginUse(snapshot);
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
