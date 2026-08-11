using System.Globalization;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class WeaponFireEventIdSource
{
    private readonly Func<string> seedFactory;
    private string seed;
    private long sequence;

    public WeaponFireEventIdSource(Func<string> seedFactory)
    {
        this.seedFactory = seedFactory ?? throw new ArgumentNullException(nameof(seedFactory));
        seed = CreateSeed();
    }

    public string NextEventId()
    {
        if (sequence == long.MaxValue)
        {
            seed = CreateSeed();
            sequence = 0;
        }

        sequence++;
        return string.Concat(seed, "-", sequence.ToString("x16", CultureInfo.InvariantCulture));
    }

    private string CreateSeed()
    {
        var value = seedFactory();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Firing event ID seed factory returned an empty value.");
        }

        return value;
    }
}
