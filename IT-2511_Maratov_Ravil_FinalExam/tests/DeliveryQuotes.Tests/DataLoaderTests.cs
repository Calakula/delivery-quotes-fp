using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member B: the exception boundary (files) and the discount table.
public class DataLoaderTests
{
    // Controlled temporary folder, removed after each test.
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dq-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, string content)
        {
            var file = System.IO.Path.Combine(Path, name);
            File.WriteAllText(file, content);
            return file;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void LoadRequests_ValidFile_ReturnsTheValidatedRequests()
    {
        using var dir = new TempDir();
        var file = dir.Write("orders.csv", "Id,Customer,DistanceKm,WeightKg\nr-1,Dana,4,0\nR-2,Timur,30,20\n");

        var result = DataLoader.LoadRequests(file);

        var requests = Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Ok>(result).Value;
        Assert.Equal(new[] { "R-1", "R-2" }, requests.Select(r => r.Id));
        Assert.Equal(0m, requests[0].WeightKg);
    }

    [Fact]
    public void LoadRequests_HeaderOnlyFile_IsAValidEmptyDataset()
    {
        using var dir = new TempDir();
        var file = dir.Write("empty.csv", RequestParser.Header + "\n");

        var result = DataLoader.LoadRequests(file);

        Assert.Empty(Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Ok>(result).Value);
    }

    // MISSING FILE
    [Fact]
    public void MissingFile_IsReportedWithItsPath_AndNothingIsCreated()
    {
        using var dir = new TempDir();
        var missing = Path.Combine(dir.Path, "no-such-file.csv");

        var requests = DataLoader.LoadRequests(missing);
        var discounts = DataLoader.LoadDiscounts(Path.Combine(dir.Path, "no-such-file.json"));
        var noFolder = DataLoader.LoadRequests(Path.Combine(dir.Path, "no-folder", "orders.csv"));

        var failure = Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Failure>(requests);
        Assert.Equal(ErrorCode.FileNotFound, failure.Error.Code);
        Assert.Contains("no-such-file.csv", failure.Error.Message);
        Assert.Equal(ErrorCode.FileNotFound, Assert.IsType<Result<DiscountTable>.Failure>(discounts).Error.Code);
        Assert.Equal(ErrorCode.FileNotFound, Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Failure>(noFolder).Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));   // the problem did not create or change any file
    }

    [Fact]
    public void UnreadablePath_AndEmptyPath_AreReportedClearly()
    {
        using var dir = new TempDir();

        var directoryAsFile = DataLoader.LoadRequests(dir.Path);   // a folder is not a readable data file
        var emptyPath = DataLoader.LoadRequests("  ");

        Assert.Equal(ErrorCode.FileUnreadable, Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Failure>(directoryAsFile).Error.Code);
        Assert.Equal(ErrorCode.InvalidInput, Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Failure>(emptyPath).Error.Code);
    }

    // MALFORMED FILE: no partial result
    [Fact]
    public void MalformedCsv_BecomesOneFailure_WithoutAPartialDataset()
    {
        using var dir = new TempDir();
        var file = dir.Write("bad.csv", "Id,Customer,DistanceKm,WeightKg\nR-1,Dana,4,1\nR-2,Timur,abc,2\nR-3,,5,1\nR-4,Aida,7\n");

        var result = DataLoader.LoadRequests(file);

        var failure = Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Failure>(result);
        Assert.Equal(ErrorCode.MalformedContent, failure.Error.Code);
        Assert.Contains("bad.csv", failure.Error.Message);
        Assert.Contains("Line 3", failure.Error.Message);    // the good first line is not returned on its own
        Assert.Contains("Line 5", failure.Error.Message);
    }

    [Fact]
    public void MalformedCsv_MissingHeader_IsMalformedNotEmpty()
    {
        using var dir = new TempDir();
        var file = dir.Write("noheader.csv", "R-1,Dana,4,1\n");

        Assert.Equal(ErrorCode.MalformedContent,
            Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Failure>(DataLoader.LoadRequests(file)).Error.Code);
    }

    [Fact]
    public void MalformedJson_BecomesAFailure_WithTheFileName()
    {
        using var dir = new TempDir();
        var broken = dir.Write("broken.json", "{ \"Dana\": 10, ");
        var wrongShape = dir.Write("array.json", "[1, 2, 3]");
        var textValue = dir.Write("text.json", "{ \"Dana\": \"ten\" }");
        var jsonNull = dir.Write("null.json", "null");
        var outOfRange = dir.Write("range.json", "{ \"Dana\": 150 }");
        var duplicate = dir.Write("dup.json", "{ \"Dana\": 5, \"dana\": 6 }");

        foreach (var file in new[] { broken, wrongShape, textValue, jsonNull, outOfRange, duplicate })
        {
            var failure = Assert.IsType<Result<DiscountTable>.Failure>(DataLoader.LoadDiscounts(file));
            Assert.Equal(ErrorCode.MalformedContent, failure.Error.Code);
            Assert.Contains(Path.GetFileName(file), failure.Error.Message);
        }
    }

    // LOOKUP PRESENT AND ABSENT with a primitive: 0 percent is a record, not absence
    [Fact]
    public void DiscountLookup_PresentZeroAndAbsent_AreDifferentCases()
    {
        using var dir = new TempDir();
        var file = dir.Write("discounts.json", "{ \"Aigerim\": 10, \"Dana\": 0 }");

        var table = Assert.IsType<Result<DiscountTable>.Ok>(DataLoader.LoadDiscounts(file)).Value;

        Assert.Equal(10m, Assert.IsType<Option<decimal>.Some>(table.Find("aigerim")).Value);   // case-insensitive
        Assert.Equal(0m, Assert.IsType<Option<decimal>.Some>(table.Find(" Dana ")).Value);      // zero is present
        Assert.IsType<Option<decimal>.None>(table.Find("Timur"));                                // absent
        Assert.IsType<Option<decimal>.None>(table.Find(null));
        Assert.IsType<Option<decimal>.None>(DiscountTable.Empty.Find("Dana"));
    }

    [Fact]
    public void DiscountTable_Create_RejectsBadEntries_AndNoTableIsBuilt()
    {
        var empty = DiscountTable.Create(new[] { new KeyValuePair<string, decimal>(" ", 5m) });
        var negative = DiscountTable.Create(new[] { new KeyValuePair<string, decimal>("Dana", -1m) });
        var boundary = DiscountTable.Create(new[] { new KeyValuePair<string, decimal>("A", 0m), new KeyValuePair<string, decimal>("B", 100m) });

        Assert.IsType<Result<DiscountTable>.Failure>(empty);
        Assert.Contains("between 0 and 100", Assert.IsType<Result<DiscountTable>.Failure>(negative).Error.Message);
        Assert.Equal(2, Assert.IsType<Result<DiscountTable>.Ok>(boundary).Value.Count);   // 0 and 100 are allowed
    }
}
