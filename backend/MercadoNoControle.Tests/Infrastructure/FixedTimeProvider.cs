namespace MercadoNoControle.Tests.Infrastructure;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public FixedTimeProvider() : this(new DateTimeOffset(TestData.FixedNow))
    {
    }

    public override DateTimeOffset GetUtcNow() => now;
}
