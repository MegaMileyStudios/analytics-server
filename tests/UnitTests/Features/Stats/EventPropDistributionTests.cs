using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Stats;

namespace Aptabase.UnitTests.Features.Stats;

public class EventPropDistributionTests
{
    private static EventPropValueRow Row(string value, int events) => new() { Value = value, Events = events };
    private static EventPropValueRow Undefined(int events) => new() { Undefined = 1, Events = events };

    private static EventPropValueRow[] Values(int count)
        => Enumerable.Range(1, count).Select(i => Row($"value-{i:D2}", 100 - i)).ToArray();

    [Fact]
    public void Should_keep_all_values_when_there_are_less_than_ten()
    {
        var result = EventPropDistribution.FromRows(Values(4));

        result.Type.Should().Be("string");
        result.Items.Select(x => x.Name).Should().Equal("value-01", "value-02", "value-03", "value-04");
        result.Items.Should().OnlyContain(x => !x.IsOther && !x.IsUndefined);
    }

    [Fact]
    public void Should_show_the_tenth_value_by_name_when_there_are_exactly_ten()
    {
        var result = EventPropDistribution.FromRows(Values(10));

        result.Items.Should().HaveCount(10);
        result.Items.Last().Name.Should().Be("value-10");
        result.Items.Should().OnlyContain(x => !x.IsOther);
    }

    [Fact]
    public void Should_group_everything_after_the_top_nine_as_other()
    {
        var result = EventPropDistribution.FromRows(Values(12));

        result.Items.Should().HaveCount(10);
        result.Items.Take(9).Select(x => x.Name).Should().Equal(Enumerable.Range(1, 9).Select(i => $"value-{i:D2}"));
        result.Items[9].Name.Should().Be("Other");
        result.Items[9].IsOther.Should().BeTrue();
        result.Items[9].Value.Should().Be(90 + 89 + 88);
    }

    [Fact]
    public void Should_order_values_by_count()
    {
        var result = EventPropDistribution.FromRows(new[] { Row("a", 1), Row("b", 5), Row("c", 3) });

        result.Items.Select(x => x.Name).Should().Equal("b", "c", "a");
    }

    [Fact]
    public void Should_leave_out_undefined_for_strings()
    {
        var result = EventPropDistribution.FromRows(new[] { Row("forest", 3), Undefined(7) });

        result.Type.Should().Be("string");
        result.Items.Should().ContainSingle().Which.Name.Should().Be("forest");
    }

    [Fact]
    public void Should_map_booleans_to_true_false_and_undefined()
    {
        var result = EventPropDistribution.FromRows(new[] { Row("false", 2), Undefined(4), Row("true", 5) });

        result.Type.Should().Be("boolean");
        result.Items.Select(x => (x.Name, x.Value)).Should().Equal(("True", 5), ("False", 2), ("Undefined", 4));
        result.Items[2].IsUndefined.Should().BeTrue();
    }

    [Fact]
    public void Should_include_zero_counts_for_booleans_with_one_value()
    {
        var result = EventPropDistribution.FromRows(new[] { Row("true", 3) });

        result.Type.Should().Be("boolean");
        result.Items.Select(x => x.Value).Should().Equal(3, 0, 0);
    }

    [Fact]
    public void Should_treat_a_mix_of_booleans_and_other_strings_as_string()
    {
        var result = EventPropDistribution.FromRows(new[] { Row("true", 3), Row("maybe", 1) });

        result.Type.Should().Be("string");
        result.Items.Select(x => x.Name).Should().Equal("true", "maybe");
    }

    [Fact]
    public void Should_be_an_empty_string_distribution_without_rows()
    {
        var result = EventPropDistribution.FromRows(Array.Empty<EventPropValueRow>());

        result.Type.Should().Be("string");
        result.Items.Should().BeEmpty();
    }
}
