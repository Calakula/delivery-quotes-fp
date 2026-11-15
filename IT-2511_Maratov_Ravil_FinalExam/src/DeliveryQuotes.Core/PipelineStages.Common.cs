using System.Collections.Immutable;

namespace DeliveryQuotes.Core;

/// <summary>
/// The three pure transformation stages (one file per stage, same static class).
/// No Console, no files, no clock, no randomness, no mutable global state,
/// and no mutation of the arguments: every stage returns new values.
/// </summary>
public static partial class PipelineStages
{
    private static void RequireInitialized<T>(ImmutableArray<T> array, string name)
    {
        if (array.IsDefault)
        {
            throw new ArgumentException("Input collection is missing (default ImmutableArray). Use ImmutableArray<T>.Empty for an empty dataset.", name);
        }
    }
}
