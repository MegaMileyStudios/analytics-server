using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Stats;

namespace Aptabase.UnitTests.Features.Stats;

public class EventPropDistributionSeriesTests
{
    private static readonly DateTime Day1 = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);
    private static readonly DateTime Day3 = Day1.AddDays(2);

    private static EventPropPeriodicValueRow Row(DateTime period, string value, int events)
        => new() { Period = period, Value = value, Events = events };

    private static EventPropPeriodicValueRow Undefined(DateTime period, int events)
        => new() { Period = period, Undefined = 1, Events = events };

    // What WITH FILL adds for a period without events
    private static EventPropPeriodicValueRow Empty(DateTime period) => new() { Period = period };

    [Fact]
    public void Should_split_values_per_period_and_fill_empty_periods_with_zero()
    {
        var result = EventPropDistributionSeries.FromRows(new[]
        {
            Row(Day1, "forest", 3), Row(Day1, "cave", 1), Empty(Day2), Row(Day3, "cave", 4),
        });

        result.Type.Should().Be("string");
        result.Periods.Should().Equal(Day1, Day2, Day3);
        result.Series.Select(x => x.Name).Should().Equal("cave", "forest");
        result.Series[0].Values.Should().Equal(1, 0, 4);
        result.Series[1].Values.Should().Equal(3, 0, 0);
    }

    [Fact]
    public void Should_pick_the_top_values_over_the_whole_range_and_count_the_rest_as_other_per_period()
    {
        // 11 values, "value-11" and "value-10" are the least used overall, so they end up in Other
        var rows = Enumerable.Range(1, 9).Select(i => Row(Day1, $"value-{i:D2}", 10)).ToList();
        rows.Add(Row(Day1, "value-10", 1));
        rows.Add(Row(Day2, "value-11", 2));
        rows.Add(Row(Day2, "value-01", 5));

        var result = EventPropDistributionSeries.FromRows(rows);

        result.Series.Should().HaveCount(10);
        result.Series[0].Name.Should().Be("value-01");
        result.Series[0].Values.Should().Equal(10, 5);
        result.Series[9].IsOther.Should().BeTrue();
        result.Series[9].Values.Should().Equal(1, 2);
    }

    [Fact]
    public void Should_keep_a_value_named_other_apart_from_the_other_bucket()
    {
        var rows = Enumerable.Range(1, 10).Select(i => Row(Day1, $"value-{i:D2}", 10)).ToList();
        rows.Add(Row(Day1, "Other", 50));

        var result = EventPropDistributionSeries.FromRows(rows);

        result.Series[0].Should().Match<EventPropDistributionSeriesItem>(x => x.Name == "Other" && !x.IsOther);
        result.Series[0].Values.Should().Equal(50);
        result.Series[9].IsOther.Should().BeTrue();
        result.Series[9].Values.Should().Equal(10 + 10);
    }

    [Fact]
    public void Should_map_booleans_per_period()
    {
        var result = EventPropDistributionSeries.FromRows(new[]
        {
            Row(Day1, "true", 2), Row(Day1, "false", 1), Undefined(Day2, 3), Row(Day2, "true", 1),
        });

        result.Type.Should().Be("boolean");
        result.Series.Select(x => x.Name).Should().Equal("True", "False", "Undefined");
        result.Series[0].Values.Should().Equal(2, 1);
        result.Series[1].Values.Should().Equal(1, 0);
        result.Series[2].Values.Should().Equal(0, 3);
    }

    [Fact]
    public void Should_leave_out_undefined_for_strings()
    {
        var result = EventPropDistributionSeries.FromRows(new[] { Row(Day1, "forest", 2), Undefined(Day1, 5) });

        result.Series.Should().ContainSingle().Which.Values.Should().Equal(2);
    }
}
