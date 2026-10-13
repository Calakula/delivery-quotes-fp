using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DeliveryQuotes.Core;

/// <summary>
/// Application boundary: turns raw CSV text into validated <see cref="DeliveryRequest"/> values.
/// Nothing here reads files or the console; the caller supplies the lines.
/// </summary>
public static class RequestParser
{
    public const string Header = "Id,Customer,DistanceKm,WeightKg";

    /// <summary>
    /// null lines  -> ArgumentNullException (missing input).
    /// header only -> valid, empty dataset.
    /// no header   -> reported as an error (not an empty dataset).
    /// </summary>
    public static ParseReport ParseLines(IEnumerable<string>? lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var requests = ImmutableArray.CreateBuilder<DeliveryRequest>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var lineNumber = 0;
        var headerSeen = false;

        foreach (var line in lines)
        {
            lineNumber++;

            if (!headerSeen)
            {
                if (!string.Equals((line ?? string.Empty).Trim(), Header, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Line {lineNumber}: expected header '{Header}'.");
                    return new ParseReport(requests.ToImmutable(), errors.ToImmutable());
                }
                headerSeen = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue; // blank lines carry no data
            }

            if (TryParseLine(line, out var request, out var error))
            {
                requests.Add(request);
            }
            else
            {
                errors.Add($"Line {lineNumber}: {error}");
            }
        }

        if (!headerSeen)
        {
            errors.Add("Input is empty: the header line is missing.");
        }

        return new ParseReport(requests.ToImmutable(), errors.ToImmutable());
    }

    /// <summary>TryParse-style validation of a single data line.</summary>
    public static bool TryParseLine(
        string? line,
        [NotNullWhen(true)] out DeliveryRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        if (line is null)
        {
            error = "line is missing (null).";
            return false;
        }

        var fields = line.Split(',');
        if (fields.Length != 4)
        {
            error = $"expected 4 fields but found {fields.Length}.";
            return false;
        }

        var id = fields[0].Trim();
        var customer = fields[1].Trim();

        if (id.Length == 0)
        {
            error = "Id is empty.";
            return false;
        }
        if (customer.Length == 0)
        {
            error = "Customer is empty.";
            return false;
        }
        if (!TryParseNumber(fields[2], out var distance))
        {
            error = $"DistanceKm '{fields[2].Trim()}' is not a number.";
            return false;
        }
        if (distance <= 0m)
        {
            error = $"DistanceKm must be greater than 0 but was {distance}.";
            return false;
        }
        if (!TryParseNumber(fields[3], out var weight))
        {
            error = $"WeightKg '{fields[3].Trim()}' is not a number.";
            return false;
        }
        if (weight < 0m)
        {
            error = $"WeightKg must not be negative but was {weight}."; // 0 is a legitimate weight (documents)
            return false;
        }

        request = new DeliveryRequest(id, customer, distance, weight);
        return true;
    }

    private static bool TryParseNumber(string text, out decimal value) =>
        decimal.TryParse(
            text.Trim(),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
}
