using System.Text.Json;
using DeliveryQuotes.Core;
using Xunit;
using static DeliveryQuotes.Tests.TestData;

namespace DeliveryQuotes.Tests;

// Member B: resource ownership and release (readers, files, export). Temporary folders are removed after each test.
public class ResourceTests
{
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dq-res-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // A reader that records whether it was disposed; it can also fail on the second line.
    private sealed class TrackingReader : StringReader
    {
        private readonly bool _failOnSecondLine;
        private int _lines;

        public TrackingReader(string text, bool failOnSecondLine = false)
            : base(text)
        {
            _failOnSecondLine = failOnSecondLine;
        }

        public bool Disposed { get; private set; }

        public override string? ReadLine()
        {
            _lines++;
            if (_failOnSecondLine && _lines == 2)
            {
                throw new IOException("simulated read failure");
            }
            return base.ReadLine();
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void ReadLines_NothingIsOpened_UntilEnumerationStarts()
    {
        var opened = 0;

        var lines = LineStreaming.ReadLines(() =>
        {
            opened++;
            return new StringReader("a\nb");
        });

        Assert.Equal(0, opened);
        Assert.Equal(new[] { "a", "b" }, lines.ToArray());
        Assert.Equal(1, opened);
    }

    // RESOURCE LIFETIME: success, failure, early stop
    [Fact]
    public void ReadLines_AfterSuccess_TheReaderIsDisposed()
    {
        var reader = new TrackingReader("one\ntwo\nthree");

        var all = LineStreaming.ReadLines(() => reader).ToArray();

        Assert.Equal(new[] { "one", "two", "three" }, all);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public void ReadLines_AfterAFailureInTheMiddle_TheReaderIsStillDisposed()
    {
        var reader = new TrackingReader("one\ntwo\nthree", failOnSecondLine: true);
        var seen = new List<string>();

        var failure = Record.Exception(() =>
        {
            foreach (var line in LineStreaming.ReadLines(() => reader))
            {
                seen.Add(line);
            }
        });

        Assert.IsType<IOException>(failure);
        Assert.Equal(new[] { "one" }, seen);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public void ReadLines_WhenTheConsumerStopsEarly_TheReaderIsDisposed()
    {
        var reader = new TrackingReader("one\ntwo\nthree");

        var first = LineStreaming.ReadLines(() => reader).First();
        Assert.Equal("one", first);
        Assert.True(reader.Disposed);

        var second = new TrackingReader("one\ntwo\nthree");
        foreach (var line in LineStreaming.ReadLines(() => second))
        {
            break;
        }
        Assert.True(second.Disposed);
    }

    [Fact]
    public void ReadLines_File_IsHeldWhileEnumerating_AndReleasedAfterAnEarlyStop()
    {
        using var dir = new TempDir();
        var path = dir.File("orders.csv");
        File.WriteAllText(path, "a\nb\nc\n");

        using (var enumerator = LineStreaming.ReadLines(path).GetEnumerator())
        {
            Assert.True(enumerator.MoveNext());
            // while the enumeration is open the file is held: an exclusive open is refused
            Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
        }

        // disposing the enumerator (an early stop) released the file: an exclusive open now works
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    [Fact]
    public void DataLoader_ReadsASnapshot_AndLeavesTheFileFree()
    {
        using var dir = new TempDir();
        var path = dir.File("orders.csv");
        File.WriteAllText(path, "Id,Customer,DistanceKm,WeightKg\nR-1,Dana,4,1\n");

        var result = DataLoader.LoadRequests(path);

        Assert.Single(Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<DeliveryRequest>>.Ok>(result).Value);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // no handle is left open
        Assert.True(exclusive.CanRead);
    }

    private static ReportBundle SampleBundle()
    {
        var requests = Deliveries();
        var rules = EligibilityRules.Default;
        var score = Scoring.CreateScore(ScoreConfig.DistanceFirst);
        var dispatch = Reports.DispatchList(requests, rules, PricingPolicies.Standard, score);
        var workload = Reports.CustomerWorkload(requests.Where(rules.Allows).Select(r => new EffortItem(r, HandPoints[r.Id])));
        return new ReportBundle(DeliveryPipeline.Run(requests, rules, PricingPolicies.Standard), dispatch, workload);
    }

    // EXPORT: success leaves a complete file and no temporary file
    [Fact]
    public void Export_Success_WritesTheCompleteFile_AndRemovesTheTemporaryFile()
    {
        using var dir = new TempDir();
        var path = dir.File("out", "reports.json");

        var result = ReportExporter.Export(path, SampleBundle());

        Assert.Equal(Path.GetFullPath(path), Assert.IsType<Result<string>.Ok>(result).Value);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(8, document.RootElement.GetProperty("Dispatch").GetArrayLength());
        Assert.Equal(452, document.RootElement.GetProperty("Workload")[0].GetProperty("TotalPoints").GetInt32());
        Assert.False(File.Exists(path + ".tmp"));
    }

    // EXPORT: failure keeps the previous file unchanged and reports the problem
    [Fact]
    public void Export_Failure_LeavesTheExistingFileUnchanged_AndReportsTheProblem()
    {
        using var dir = new TempDir();
        var path = dir.File("reports.json");
        File.WriteAllText(path, "OLD CONTENT");
        Directory.CreateDirectory(path + ".tmp");   // a folder where the temporary file must be created: the write fails

        var result = ReportExporter.Export(path, SampleBundle());

        var failure = Assert.IsType<Result<string>.Failure>(result);
        Assert.Equal(ErrorCode.FileNotWritable, failure.Error.Code);
        Assert.Contains("reports.json", failure.Error.Message);
        Assert.Equal("OLD CONTENT", File.ReadAllText(path));
    }

    [Fact]
    public void Export_TargetIsAFolder_FailsAndRemovesItsTemporaryFile()
    {
        using var dir = new TempDir();
        var target = dir.File("reports.json");
        Directory.CreateDirectory(target);   // the target name is already a folder

        var result = ReportExporter.Export(target, SampleBundle());

        Assert.Equal(ErrorCode.FileNotWritable, Assert.IsType<Result<string>.Failure>(result).Error.Code);
        Assert.False(File.Exists(target + ".tmp"));   // no half-written leftover
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Export_EmptyPath_IsRejected()
    {
        Assert.Equal(ErrorCode.InvalidInput, Assert.IsType<Result<string>.Failure>(ReportExporter.Export("  ", SampleBundle())).Error.Code);
    }
}
