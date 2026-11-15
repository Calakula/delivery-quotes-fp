using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace DeliveryQuotes.Core;

/// <summary>Everything the reports produce in one value (also the content of the exported file).</summary>
public sealed record ReportBundle(QuoteSummary Summary, ImmutableArray<DispatchLine> Dispatch, ImmutableArray<WorkloadLine> Workload);

/// <summary>
/// EXPORT BOUNDARY. Writes the bundle as one JSON file.
/// The text is built first (pure), then written to a temporary file next to the target, and only a fully written file
/// replaces the target (File.Move with overwrite). OWNER of the StreamWriter: this method, released by "using" on success and on failure.
/// If anything fails, the temporary file is removed and an existing target file is left exactly as it was.
/// </summary>
public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToJson(ReportBundle bundle) => ToJson<ReportBundle>(bundle);

    public static string ToJson<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static Result<string> Export(string? path, ReportBundle bundle) => ExportJson(path, bundle);

    /// <summary>Atomic JSON export of any plain data value (reports, assignment plans).</summary>
    public static Result<string> ExportJson<T>(string? path, T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Failure<string>(ErrorCode.InvalidInput, "No output path was given for the report file.");
        }

        var json = ToJson(value);
        string target;
        string temp;
        try
        {
            target = Path.GetFullPath(path);
            temp = target + ".tmp";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result.Failure<string>(ErrorCode.FileNotWritable, $"The output path '{path}' is not valid: {ex.Message}");
        }

        try
        {
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                writer.Write(json);
            }

            File.Move(temp, target, overwrite: true);
            return Result.Success(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            var cleanup = RemoveTemp(temp);
            return Result.Failure<string>(ErrorCode.FileNotWritable,
                $"The report could not be written to '{target}': {ex.Message}{cleanup}");
        }
    }

    // Only a file created by this export is removed; a failure to remove it is reported, not hidden.
    private static string RemoveTemp(string temp)
    {
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
            return string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $" (the temporary file '{temp}' could not be removed: {ex.Message})";
        }
    }
}
