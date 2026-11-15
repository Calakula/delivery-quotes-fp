using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member A: input boundary (parsing and validation).
public class ParserTests
{
    [Theory]
    [InlineData("R-1,Dana,-3,5", "DistanceKm must be greater than 0")]
    [InlineData("R-1,Dana,0,5", "DistanceKm must be greater than 0")]
    [InlineData("R-1,Dana,abc,5", "not a number")]
    [InlineData("R-1,Dana,4,-1", "WeightKg must not be negative")]
    [InlineData("R-1,Dana,4,heavy", "not a number")]
    [InlineData("R-1,,4,2", "Customer is empty")]
    [InlineData(",Dana,4,2", "Id is empty")]
    [InlineData("R-1,Dana,4", "expected 4 fields")]
    public void InvalidLine_IsRejectedWithClearMessage(string line, string expectedMessagePart)
    {
        var ok = RequestParser.TryParseLine(line, out var request, out var error);

        Assert.False(ok);
        Assert.Null(request);
        Assert.Contains(expectedMessagePart, error);
    }

    [Fact]
    public void MissingInput_IsRejected_NotTurnedIntoEmptyResult()
    {
        Assert.False(RequestParser.TryParseLine(null, out var request, out var error));
        Assert.Null(request);
        Assert.Contains("missing", error);

        Assert.Throws<ArgumentNullException>(() => RequestParser.ParseLines(null));

        var noLines = RequestParser.ParseLines(Array.Empty<string>()); // no header: not the same as an empty dataset
        Assert.False(noLines.IsValid);

        var wrongHeader = RequestParser.ParseLines(new[] { "Foo,Bar", "R-1,Dana,4,2" });
        Assert.False(wrongHeader.IsValid);
        Assert.Empty(wrongHeader.Requests);
    }

    [Fact]
    public void InvalidFile_ReportsEveryBadLine_WithLineNumbers()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "data", "requests_invalid.csv"));
        var report = RequestParser.ParseLines(lines);

        Assert.False(report.IsValid);
        Assert.Equal(4, report.Errors.Length);
        Assert.StartsWith("Line 3:", report.Errors[0]);
        Assert.Single(report.Requests); // only R-001 was valid
    }

    [Fact]
    public void ZeroWeight_IsLegitimateValue_AndSampleFileParses()
    {
        Assert.True(RequestParser.TryParseLine("R-1,Dana,4,0", out var request, out _));
        Assert.Equal(0m, request.WeightKg);

        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "data", "requests.csv"));
        var report = RequestParser.ParseLines(lines);
        Assert.True(report.IsValid);
        Assert.Equal(6, report.Requests.Length);
    }

    [Fact]
    public void Parser_UsesFactory_SoValuesAreNormalised()
    {
        Assert.True(RequestParser.TryParseLine("  r-9 , Dana ,4,1", out var request, out _));
        Assert.Equal("R-9", request.Id);
        Assert.Equal("Dana", request.Customer);
    }

    [Fact]
    public void Parser_ReportsAllFactoryReasonsOfOneLine()
    {
        Assert.False(RequestParser.TryParseLine(" , ,4,2", out var request, out var error));
        Assert.Null(request);
        Assert.Contains("Id is empty", error);
        Assert.Contains("Customer is empty", error);
    }
}
