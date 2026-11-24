using GpsdExporter.Connection;

namespace GpsdExporter.Tests.Core.Connection;

public sealed class ReconnectBackoffTests
{
    private static readonly double[] Schedule = [1, 2, 4, 8, 16, 30, 30, 30];

    private static ReconnectBackoff Create(Func<double> random) =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), random);

    [Fact]
    public void without_jitter_the_schedule_doubles_to_the_cap()
    {
        var backoff = Create(() => 0.5);

        Schedule.Select(_ => backoff.Next().TotalSeconds).ShouldBe(Schedule);
    }

    [Theory]
    [InlineData(0.0, 0.8)]
    [InlineData(0.999_999_999, 1.2)]
    public void jitter_is_plus_or_minus_20_percent(double random, double factor)
    {
        var backoff = Create(() => random);

        foreach (var expected in Schedule)
        {
            backoff.Next().TotalSeconds.ShouldBe(expected * factor, tolerance: 1e-6);
        }
    }

    [Fact]
    public void random_jitter_stays_within_bounds()
    {
        var random = new Random(1234);
        var backoff = new ReconnectBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), random.NextDouble);

        for (var round = 0; round < 50; round++)
        {
            foreach (var expected in Schedule)
            {
                backoff.Next().TotalSeconds.ShouldBeInRange(expected * 0.8, expected * 1.2);
            }

            backoff.Reset();
        }
    }

    [Fact]
    public void reset_starts_the_schedule_again()
    {
        var backoff = Create(() => 0.5);
        for (var i = 0; i < 6; i++)
        {
            backoff.Next();
        }

        backoff.Reset();

        backoff.Next().ShouldBe(TimeSpan.FromSeconds(1));
        backoff.Next().ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Rejects_a_nonsense_schedule()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ReconnectBackoff(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Should.Throw<ArgumentOutOfRangeException>(() => new ReconnectBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }
}
