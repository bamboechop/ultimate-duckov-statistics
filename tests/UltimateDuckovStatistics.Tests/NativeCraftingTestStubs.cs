using System.Runtime.CompilerServices;
using Duckov.Economy;
using ItemStatsSystem;

#pragma warning disable CA1000, CA1050, CA1051, CA1711, CA1822, CA2211 // Stubs mirror installed global native contracts.

public struct CraftingFormula
{
    public struct ItemEntry
    {
        public int id;
        public int amount;
    }

    public string id;
    public ItemEntry result;
    public Cost cost;
}

public sealed class CraftingManager
{
    public static Action<CraftingFormula, Item>? OnItemCrafted;

    private Cysharp.Threading.Tasks.UniTask<List<Item>> Craft(CraftingFormula formula) =>
        Cysharp.Threading.Tasks.UniTask<List<Item>>.FromResult(new List<Item>());

    public Cysharp.Threading.Tasks.UniTask<List<Item>> Craft(string formulaId) =>
        Cysharp.Threading.Tasks.UniTask<List<Item>>.FromResult(new List<Item>());
}

public sealed class ItemMetaData
{
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public static class ItemAssetsCollection
{
    public static ItemMetaData GetMetaData(int itemTypeId) => new()
    {
        DisplayName = "Item " + itemTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Name = itemTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}

#pragma warning restore CA1050, CA1051

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(AsyncUniTaskMethodBuilder))]
    public readonly struct UniTask
    {
        private readonly Task? task;

        internal UniTask(Task task) => this.task = task;

        public static UniTask CompletedTask => new(Task.CompletedTask);

        public TaskAwaiter GetAwaiter() => (task ?? Task.CompletedTask).GetAwaiter();
    }

    public struct AsyncUniTaskMethodBuilder
    {
        private AsyncTaskMethodBuilder builder;

        public static AsyncUniTaskMethodBuilder Create() => new()
        {
            builder = AsyncTaskMethodBuilder.Create()
        };

        public readonly UniTask Task => new(builder.Task);
        public void SetResult() => builder.SetResult();
        public void SetException(Exception exception) => builder.SetException(exception);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => builder.SetStateMachine(stateMachine);
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => builder.Start(ref stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine => builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine => builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    [AsyncMethodBuilder(typeof(AsyncUniTaskMethodBuilder<>))]
    public readonly struct UniTask<T>
    {
        private readonly Task<T>? task;

        internal UniTask(Task<T> task) => this.task = task;

        public static UniTask<T> FromResult(T value) => new(System.Threading.Tasks.Task.FromResult(value));

        public TaskAwaiter<T> GetAwaiter() => (task ?? System.Threading.Tasks.Task.FromResult(default(T)!)).GetAwaiter();
    }

    public struct AsyncUniTaskMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;

        public static AsyncUniTaskMethodBuilder<T> Create() => new()
        {
            builder = AsyncTaskMethodBuilder<T>.Create()
        };

        public readonly UniTask<T> Task => new(builder.Task);
        public void SetResult(T result) => builder.SetResult(result);
        public void SetException(Exception exception) => builder.SetException(exception);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => builder.SetStateMachine(stateMachine);
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => builder.Start(ref stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine => builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine => builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}
