namespace UltimateDuckovStatistics.Adapters;

internal sealed class EconomyHoldingsObservationGate
{
    private long tickSequence;
    private long moneyDueTick;
    private long cashDueTick;

    public bool MoneyDirty { get; private set; }
    public bool CashDirty { get; private set; }
    public bool HasPending => MoneyDirty || CashDirty;

    public void Advance() => tickSequence = checked(tickSequence + 1);

    public void SignalMoney()
    {
        MoneyDirty = true;
        moneyDueTick = checked(tickSequence + 1);
    }

    public void SignalCash()
    {
        CashDirty = true;
        cashDueTick = checked(tickSequence + 1);
    }

    public bool IsMoneyDue(bool force) => MoneyDirty && (force || tickSequence >= moneyDueTick);
    public bool IsCashDue(bool force) => CashDirty && (force || tickSequence >= cashDueTick);
    public void ClearMoney() => MoneyDirty = false;
    public void ClearCash() => CashDirty = false;

    public void Reset()
    {
        MoneyDirty = false;
        CashDirty = false;
    }
}
