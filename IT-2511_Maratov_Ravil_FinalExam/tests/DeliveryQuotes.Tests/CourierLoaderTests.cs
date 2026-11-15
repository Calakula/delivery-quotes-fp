using System.Collections.Immutable;
using DeliveryQuotes.Core;
using Xunit;

namespace DeliveryQuotes.Tests;

// Member B: loading couriers from JSON (file boundary). Temporary folders are removed after each test.
public class CourierLoaderTests
{
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dq-cour-" + Guid.NewGuid().ToString("N"));
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
    public void LoadCouriers_ValidFile_ReturnsValidatedCouriers()
    {
        using var dir = new TempDir();
        var file = dir.Write("couriers.json", "[ {\"id\": \"c-1\", \"Name\": \"Aibek\", \"Capacity\": 300}, {\"Id\": \"C-2\", \"Name\": \"Bota\", \"Capacity\": 0} ]");

        var couriers = Assert.IsType<Result<ImmutableArray<Courier>>.Ok>(DataLoader.LoadCouriers(file)).Value;

        Assert.Equal(new[] { "C-1", "C-2" }, couriers.Select(c => c.Id));
        Assert.Equal(new[] { 300, 0 }, couriers.Select(c => c.Capacity));   // property names are case-insensitive; zero capacity is valid
    }

    [Fact]
    public void LoadCouriers_EmptyArray_IsAValidEmptyDataset()
    {
        using var dir = new TempDir();

        var result = DataLoader.LoadCouriers(dir.Write("empty.json", "[]"));

        Assert.Empty(Assert.IsType<Result<ImmutableArray<Courier>>.Ok>(result).Value);
    }

    [Fact]
    public void LoadCouriers_MissingFile_IsReportedWithItsPath()
    {
        using var dir = new TempDir();

        var failure = Assert.IsType<Result<ImmutableArray<Courier>>.Failure>(DataLoader.LoadCouriers(Path.Combine(dir.Path, "none.json")));

        Assert.Equal(ErrorCode.FileNotFound, failure.Error.Code);
        Assert.Contains("none.json", failure.Error.Message);
    }

    [Fact]
    public void LoadCouriers_MalformedContent_BecomesAClearFailure_WithoutAPartialList()
    {
        using var dir = new TempDir();
        var files = new[]
        {
            dir.Write("broken.json", "[ { \"Id\": \"C-1\", "),                                                        // invalid JSON
            dir.Write("object.json", "{ \"Id\": \"C-1\" }"),                                                          // wrong shape
            dir.Write("text.json", "[ { \"Id\": \"C-1\", \"Name\": \"A\", \"Capacity\": \"many\" } ]"),              // wrong type
            dir.Write("fraction.json", "[ { \"Id\": \"C-1\", \"Name\": \"A\", \"Capacity\": 1.5 } ]"),               // not an integer
            dir.Write("null.json", "null"),
            dir.Write("entry-null.json", "[ null ]"),
            dir.Write("negative.json", "[ { \"Id\": \"C-1\", \"Name\": \"A\", \"Capacity\": 10 }, { \"Id\": \"C-2\", \"Name\": \"B\", \"Capacity\": -5 } ]"),
            dir.Write("duplicate.json", "[ { \"Id\": \"C-1\", \"Name\": \"A\", \"Capacity\": 10 }, { \"Id\": \"c-1\", \"Name\": \"B\", \"Capacity\": 20 } ]"),
        };

        foreach (var file in files)
        {
            var failure = Assert.IsType<Result<ImmutableArray<Courier>>.Failure>(DataLoader.LoadCouriers(file));
            Assert.Equal(ErrorCode.MalformedContent, failure.Error.Code);
            Assert.Contains(Path.GetFileName(file), failure.Error.Message);
        }

        var negative = Assert.IsType<Result<ImmutableArray<Courier>>.Failure>(DataLoader.LoadCouriers(files[6]));
        Assert.Contains("entry 2", negative.Error.Message);   // the message names the bad entry
    }

    [Fact]
    public void SampleCourierFile_LoadsFromTheDataFolder()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "couriers.json");

        var couriers = Assert.IsType<Result<ImmutableArray<Courier>>.Ok>(DataLoader.LoadCouriers(path)).Value;

        Assert.Equal(new[] { 300, 200, 150 }, couriers.Select(c => c.Capacity));
    }
}
