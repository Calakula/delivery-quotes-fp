namespace DeliveryQuotes.Core;

/// <summary>Machine-readable reason of a failure. Domain failures and operational (file) failures are separate groups.</summary>
public enum ErrorCode
{
    // Expected DOMAIN failures (business rules)
    InvalidInput,          // a value is missing or not a number, a number is out of range
    InvalidRequest,        // the validated factory rejected the request
    NotFound,              // a required item does not exist
    InvalidState,          // the operation is not allowed in the current state
    OutsideServiceLimits,  // the order is outside the service limits
    DuplicateOrder,        // an order with this id already exists

    // OPERATIONAL failures (found at the file boundary)
    FileNotFound,          // the data file or its folder does not exist
    FileUnreadable,        // the file exists but cannot be read (access denied, I/O problem)
    MalformedContent,      // the file was read but its content is invalid CSV/JSON/data
    FileNotWritable,       // an output file could not be written (access denied, target is a folder, ...)

    // NATIVE library problems (found at the adapter boundary)
    NativeLibraryUnavailable, // the library file is missing, wrong architecture, or cannot be loaded
    NativeCallFailed,         // the library loaded but the export is missing or the call rejected its input

    // A case that should never happen; reported instead of being treated as success
    Unexpected,
}

/// <summary>What went wrong: a code for programs and a message that names the rule, item or file involved.</summary>
public sealed record Error(ErrorCode Code, string Message);
