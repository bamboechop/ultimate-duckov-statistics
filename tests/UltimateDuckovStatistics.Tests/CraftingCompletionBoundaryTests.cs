using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class CraftingCompletionBoundaryTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SuccessfulTaskCompletionPublishesOneActionAndItsDeclaredBatchQuantity()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence("451", "Physical Cash", "cash_bundle", 25));

        Assert.Equal(1, boundary.PendingCount);
        Assert.True(boundary.TryComplete(token, "generation-1", Now, out var mutation));

        var row = Assert.Single(mutation.Rows);
        Assert.Equal("generation-1", mutation.SaveGenerationId);
        Assert.Equal("451", row.OutputItemId);
        Assert.Equal("Physical Cash", row.OutputDisplayName);
        Assert.Equal("cash_bundle", row.RecipeId);
        Assert.Equal(1, row.CompletionActions);
        Assert.Equal(25, row.ProducedQuantity);
        Assert.Equal(1, row.BatchActions["25"]);
        Assert.Equal(0, boundary.PendingCount);
        Assert.Equal(1, boundary.OutstandingCount);
        Assert.True(boundary.FinishPublication(token));
        Assert.Equal(0, boundary.OutstandingCount);
    }

    [Fact]
    public void RequestDoesNotCountBeforeSuccessfulNativeTaskCompletion()
    {
        var boundary = new CraftingCompletionBoundary();

        _ = boundary.Begin(Evidence("100", "Bandage", "bandage", 1));

        Assert.Equal(1, boundary.PendingCount);
    }

    [Fact]
    public void CancellationFailureAndNullResultAbandonPendingRequestsWithoutCounting()
    {
        var boundary = new CraftingCompletionBoundary();
        var canceled = boundary.Begin(Evidence("100", "Bandage", "bandage", 1));
        var failed = boundary.Begin(Evidence("101", "Med Kit", "med_kit", 1));

        Assert.True(boundary.Abandon(canceled));
        Assert.True(boundary.Abandon(failed));
        Assert.False(boundary.Abandon(failed));
        Assert.Equal(0, boundary.PendingCount);
    }

    [Fact]
    public void OverlappingTasksCompleteOutOfOrderWithoutCrossingRecipeOrBatchEvidence()
    {
        var boundary = new CraftingCompletionBoundary();
        var first = boundary.Begin(Evidence("100", "Bandage", "bandage", 2));
        var second = boundary.Begin(Evidence("900001", "Modded Cell", "modded_cell", 7));

        Assert.True(boundary.TryComplete(second, "generation-1", Now, out var secondMutation));
        Assert.True(boundary.TryComplete(first, "generation-1", Now.AddSeconds(1), out var firstMutation));

        Assert.Equal("900001", Assert.Single(secondMutation.Rows).OutputItemId);
        Assert.Equal(7, Assert.Single(secondMutation.Rows).ProducedQuantity);
        Assert.Equal("100", Assert.Single(firstMutation.Rows).OutputItemId);
        Assert.Equal(2, Assert.Single(firstMutation.Rows).ProducedQuantity);
        Assert.True(boundary.FinishPublication(second));
        Assert.True(boundary.FinishPublication(first));
    }

    [Fact]
    public void CompletionUsesTheSaveGenerationObservedAfterNativeDelivery()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence("100", "Bandage", "bandage", 1));

        Assert.True(boundary.TryComplete(token, "generation-after-transition", Now, out var mutation));

        Assert.Equal("generation-after-transition", mutation.SaveGenerationId);
        Assert.True(boundary.FinishPublication(token));
    }

    [Fact]
    public void MissingCompletionGenerationDoesNotConsumeInFlightEvidence()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence("100", "Bandage", "bandage", 1));

        Assert.False(boundary.TryComplete(token, string.Empty, Now, out var mutation));

        Assert.True(mutation.IsEmpty);
        Assert.Equal(1, boundary.PendingCount);
        Assert.Equal(1, boundary.OutstandingCount);
        Assert.True(boundary.Abandon(token));
        Assert.Equal(0, boundary.OutstandingCount);
    }

    [Fact]
    public void DuplicateCompletionCallbackIsRejectedByInFlightTokenState()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence("100", "Bandage", "bandage", 1));

        Assert.True(boundary.TryComplete(token, "generation-1", Now, out _));
        Assert.False(boundary.TryComplete(token, "generation-1", Now, out var duplicate));
        Assert.True(duplicate.IsEmpty);
        Assert.True(boundary.FinishPublication(token));
        Assert.False(boundary.FinishPublication(token));
    }

    [Fact]
    public void UnknownAndModdedOutputIdentityIsPreservedInsteadOfDroppedOrClassified()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence("-77", "Unknown item -77", "mod:recipe/unknown", 3));

        Assert.True(boundary.TryComplete(token, "generation-1", Now, out var mutation));

        var row = Assert.Single(mutation.Rows);
        Assert.Equal("-77", row.OutputItemId);
        Assert.Equal("Unknown item -77", row.OutputDisplayName);
        Assert.Equal("mod:recipe/unknown", row.RecipeId);
        Assert.True(boundary.FinishPublication(token));
    }

    [Fact]
    public void ProvenCompletionRemainsOutstandingUntilAggregatePublicationFinishes()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence("100", "Bandage", "bandage", 1));

        Assert.True(boundary.TryComplete(token, "generation-1", Now, out _));

        Assert.Equal(0, boundary.PendingCount);
        Assert.Equal(1, boundary.OutstandingCount);
        Assert.True(boundary.FinishPublication(token));
        Assert.Equal(0, boundary.OutstandingCount);
    }

    [Fact]
    public void RestartDoesNotPromoteQueuedButIncompleteNativeWorkIntoHistory()
    {
        var beforeRestart = new CraftingCompletionBoundary();
        var staleToken = beforeRestart.Begin(Evidence("100", "Bandage", "bandage", 1));
        var afterRestart = new CraftingCompletionBoundary();

        Assert.False(afterRestart.TryComplete(staleToken, "generation-1", Now, out var mutation));
        Assert.True(mutation.IsEmpty);
    }

    private static CraftingCompletionEvidence Evidence(
        string outputItemId,
        string displayName,
        string recipeId,
        long producedQuantity) => new(
            outputItemId,
            displayName,
            recipeId,
            producedQuantity);
}
