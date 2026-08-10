namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class ReferenceSubjectGate<T>
    where T : class
{
    private T? current;

    public T? Current => current;

    public bool Replace(T? subject)
    {
        if (ReferenceEquals(current, subject))
        {
            return false;
        }

        current = subject;
        return true;
    }

    public bool Accepts(T? subject) => subject != null && ReferenceEquals(current, subject);

    public void Clear() => current = null;
}
