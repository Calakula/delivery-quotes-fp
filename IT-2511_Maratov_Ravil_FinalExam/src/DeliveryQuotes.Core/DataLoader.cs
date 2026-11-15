using System.Collections.Immutable;
using System.Text.Json;

namespace DeliveryQuotes.Core;

/// <summary>
/// THE EXCEPTION BOUNDARY. This is the only place that touches the file system and the only place that catches
/// operational exceptions. Every expected problem becomes a failure with a code and a message naming the file:
///   missing file or folder  -> FileNotFound
///   cannot be read          -> FileUnreadable
///   invalid CSV / JSON      -> MalformedContent
/// Nothing is returned (and nothing is partly built) when a problem is found. Unexpected exceptions are not caught.
/// Resource ownership: the orders file is read through <see cref="LineStreaming"/> inside a bounded scope: its reader is
/// owned by that iterator and released when ToArray() finishes or fails, and only a snapshot (string[]) leaves this scope.
/// The discounts file is read with File.ReadAllText, a convenience API that opens and closes the file internally.
/// </summary>
public static class DataLoader
{
    public static Result<ImmutableArray<DeliveryRequest>> LoadRequests(string? path) =>
        ReadFile(path, "orders file", p => LineStreaming.ReadLines(p).ToArray())
            .Bind(lines =>
            {
                var report = RequestParser.ParseLines(lines);
                return report.IsValid
                    ? Result.Success(report.Requests)
                    : Result.Failure<ImmutableArray<DeliveryRequest>>(ErrorCode.MalformedContent,
                        $"Orders file '{path}' is malformed: {string.Join(" | ", report.Errors)}");
            });

    public static Result<DiscountTable> LoadDiscounts(string? path) =>
        ReadFile(path, "discounts file", File.ReadAllText)
            .Bind(text => ParseDiscounts(path, text));

    /// <summary>Couriers from a JSON array: [{"Id": "C-1", "Name": "Aibek", "Capacity": 300}, ...]. Every entry is validated by the factory.</summary>
    public static Result<ImmutableArray<Courier>> LoadCouriers(string? path) =>
        ReadFile(path, "couriers file", File.ReadAllText)
            .Bind(text => ParseCouriers(path, text));

    internal sealed record CourierRecord(string? Id, string? Name, int Capacity);

    private static readonly JsonSerializerOptions CourierJson = new() { PropertyNameCaseInsensitive = true };

    private static Result<ImmutableArray<Courier>> ParseCouriers(string? path, string text)
    {
        List<CourierRecord?>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<CourierRecord?>>(text, CourierJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure<ImmutableArray<Courier>>(ErrorCode.MalformedContent,
                $"Couriers file '{path}' is not a valid JSON array of couriers: {ex.Message}");
        }

        if (records is null)
        {
            return Result.Failure<ImmutableArray<Courier>>(ErrorCode.MalformedContent, $"Couriers file '{path}' contains JSON null instead of an array.");
        }

        return records
            .Select((record, index) => (Record: record, Number: index + 1))
            .CollectResults(entry => CreateCourier(path, entry.Record, entry.Number))
            .Bind(couriers => RejectDuplicateIds(path, couriers));
    }

    private static Result<Courier> CreateCourier(string? path, CourierRecord? record, int number)
    {
        if (record is null)
        {
            return Result.Failure<Courier>(ErrorCode.MalformedContent, $"Couriers file '{path}', entry {number} is null.");
        }

        return Courier.Create(record.Id, record.Name, record.Capacity) switch
        {
            Result<Courier>.Ok ok => Result.Success(ok.Value),
            Result<Courier>.Failure failure => Result.Failure<Courier>(ErrorCode.MalformedContent, $"Couriers file '{path}', entry {number}: {failure.Error.Message}"),
            _ => Result.Failure<Courier>(ErrorCode.Unexpected, "Unknown result case."),
        };
    }

    private static Result<ImmutableArray<Courier>> RejectDuplicateIds(string? path, ImmutableArray<Courier> couriers)
    {
        var duplicate = couriers.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        return duplicate is null
            ? Result.Success(couriers)
            : Result.Failure<ImmutableArray<Courier>>(ErrorCode.MalformedContent, $"Couriers file '{path}': courier id '{duplicate.Key}' appears more than once.");
    }

    private static Result<DiscountTable> ParseDiscounts(string? path, string text)
    {
        Dictionary<string, decimal>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(text);
        }
        catch (JsonException ex)
        {
            return Result.Failure<DiscountTable>(ErrorCode.MalformedContent,
                $"Discounts file '{path}' is not valid JSON of the form {{\"customer\": percent}}: {ex.Message}");
        }

        if (raw is null)
        {
            return Result.Failure<DiscountTable>(ErrorCode.MalformedContent, $"Discounts file '{path}' contains JSON null instead of an object.");
        }

        return DiscountTable.Create(raw) switch
        {
            Result<DiscountTable>.Ok ok => Result.Success(ok.Value),
            Result<DiscountTable>.Failure failure => Result.Failure<DiscountTable>(ErrorCode.MalformedContent, $"Discounts file '{path}': {failure.Error.Message}"),
            _ => Result.Failure<DiscountTable>(ErrorCode.Unexpected, "Unknown result case."),
        };
    }

    private static Result<T> ReadFile<T>(string? path, string what, Func<string, T> read) where T : notnull
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Failure<T>(ErrorCode.InvalidInput, $"No path was given for the {what}.");
        }

        try
        {
            return Result.Success(read(path));
        }
        catch (FileNotFoundException)
        {
            return Result.Failure<T>(ErrorCode.FileNotFound, $"The {what} '{path}' was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Failure<T>(ErrorCode.FileNotFound, $"The folder of the {what} '{path}' was not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Result.Failure<T>(ErrorCode.FileUnreadable, $"The {what} '{path}' could not be read: {ex.Message}");
        }
    }
}
