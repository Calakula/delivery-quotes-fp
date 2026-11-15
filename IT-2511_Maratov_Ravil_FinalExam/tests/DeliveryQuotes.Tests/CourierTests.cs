using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member A: the second domain model (Courier) and its validated factory.
public class CourierTests
{
    [Fact]
    public void Create_ValidInput_ReturnsANormalisedCourier()
    {
        var courier = Assert.IsType<Result<Courier>.Ok>(Courier.Create("  c-1 ", " Aibek ", 300)).Value;

        Assert.Equal("C-1", courier.Id);
        Assert.Equal("Aibek", courier.Name);
        Assert.Equal(300, courier.Capacity);
    }

    [Fact]
    public void Create_EachRuleRejects_AndAllReasonsAreReportedTogether()
    {
        Assert.Contains("Courier id is empty", Assert.IsType<Result<Courier>.Failure>(Courier.Create(" ", "Aibek", 10)).Error.Message);
        Assert.Contains("Courier name is empty", Assert.IsType<Result<Courier>.Failure>(Courier.Create("C-1", null, 10)).Error.Message);
        Assert.Contains("between 0 and 10000", Assert.IsType<Result<Courier>.Failure>(Courier.Create("C-1", "Aibek", -1)).Error.Message);

        var all = Assert.IsType<Result<Courier>.Failure>(Courier.Create("", "", -5));
        Assert.Equal(ErrorCode.InvalidInput, all.Error.Code);
        Assert.Contains("id is empty", all.Error.Message);
        Assert.Contains("name is empty", all.Error.Message);
        Assert.Contains("capacity", all.Error.Message);
    }

    [Fact]
    public void Create_CapacityBoundaries_ZeroAndMaximumAreValid_OneOverIsNot()
    {
        Assert.Equal(0, Assert.IsType<Result<Courier>.Ok>(Courier.Create("C-1", "Aibek", 0)).Value.Capacity);        // zero is legitimate
        Assert.Equal(10_000, Assert.IsType<Result<Courier>.Ok>(Courier.Create("C-1", "Aibek", 10_000)).Value.Capacity);
        Assert.IsType<Result<Courier>.Failure>(Courier.Create("C-1", "Aibek", 10_001));
        Assert.IsType<Result<Courier>.Failure>(Courier.Create("C-1", "Aibek", -1));
    }

    [Fact]
    public void Courier_CannotBeBuiltWithoutTheFactory()
    {
        Assert.Empty(typeof(Courier).GetConstructors());
        Assert.All(typeof(Courier).GetProperties(), p => Assert.Null(p.SetMethod));
    }
}
