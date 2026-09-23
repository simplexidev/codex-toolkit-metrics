using Baseline;

namespace Baseline.Tests;

public sealed class CalculatorTests
{
    [Fact]
    public void Add_combines_two_positive_values() => Assert.Equal(5, Calculator.Add(2, 3));
}

