# Courier Delivery Quotes and Dispatch - Final Project

Introduction to Functional Programming, Astana IT University, 2026-2027. One C# console application that prices, confirms, reports and plans courier deliveries, built with the functional techniques of the course. It is the integrated result of Assignments 5 to 8, extended with a courier model and a fifth workflow (courier assignment).

## 1. Project information

- **Title:** Courier Delivery Quotes and Dispatch.
- **Team:** Maratov Ravil (IT-2511), Zhaksylyk Aruzhan (IT-2511), Neda Farhat (IT-2511).
- **Problem:** a small courier service must decide which requests it can deliver, what each costs, which ones go out first, how much courier effort they need, and which courier takes which order, without hidden call orders or unexplained failures.
- **Intended users:** dispatchers (quotes, dispatch list, assignment) and planners (workload per customer).
- **Goals:** correct, explainable results; explicit success and failure outcomes; a pure, testable core with all input and output at the edges.

## 2. Submitted version

- Repository: https://github.com/Calakula/delivery-quotes-fp
- Branch: `main`
- Final commit identifier of the submitted code: `4cb5b0c5e0a6ff7f2c02d31bf2c367055472a1f4`

(A commit cannot contain its own identifier: the id above is the commit of the code, and the commit after it only records this id in `README.md` and `GIT_REPOSITORY.txt`. The archive uploaded to Moodle contains exactly this code.)

## 3. Domain models, workflows, reports

**Domain models (related):** `DeliveryRequest` (id, customer, distance, weight; validated factory) and `Courier` (id, name, capacity in effort points; validated factory). They meet in the assignment workflow. Supporting models: `OrderState` (Submitted, Quoted, Confirmed), `Result<T>`, `Option<T>`, `DiscountTable`, reports and plan records.

**Five meaningful workflows** (each: input, business rules, result)

| # | Workflow | Input | Rules | Result | Command |
|---|---|---|---|---|---|
| 1 | Delivery eligibility and cost | orders CSV, price policy | limits 30 km / 20 kg; Standard 600 + 120/km, Express 1000 + 180/km; +50 per kg above 5 kg | quote list with total, number not eligible | `<csv> <standard\|express\|both>` |
| 2 | Order confirmation with safe states | orders CSV, policy, order id | Submitted -> Quoted -> Confirmed; confirm needs a quote; repeat rules; optional customer discount | order state or a clear rejection | `lifecycle ...`, `process ...` |
| 3 | Instant quote with customer discount | typed id, customer, km, kg, optional discounts JSON | validated request, limits, price, discount lookup (0 % is a record) | quote with price and discount info | `quote ...` |
| 4 | Dispatch list and workload reports | orders CSV, policy, score configuration | eligible only; score ranking; native effort points per order | two reports + exported JSON | `reports ...` |
| 5 | Courier assignment | orders CSV, couriers JSON, courier policy, score configuration | orders in dispatch order; a courier needs enough capacity left; at least one courier | assigned / unassigned orders, load per courier, exported JSON | `assign ...` |

**Two reports (`Reports.cs`):** Dispatch list (Where, lazy pricing, Select, OrderByDescending + ThenBy, ranking, one materialisation) and Customer workload (GroupBy, Count, Sum, Aggregate, ordering with tie-break). Both handle empty input, no matching records and ties.

## 4. Architecture and data flow

```
files (CSV / JSON)  --DataLoader-->  validated values  --pure core-->  results  --Program / ReportExporter-->  console / JSON file
                                          ^                 |
                              native C library (via adapter, called from the workflow)
```

| Layer | Files | Side effects |
|---|---|---|
| Pure domain | `DeliveryRequest.cs`, `Courier.cs`, `RequestValidation.cs`, `OrderState.cs`, `TransitionResult.cs`, `OrderWorkflow.cs`, `OrderBook.cs`, `Assignment.cs`, `PipelineStages.*.cs`, `PricingPolicies.cs`, `Scoring.cs`, `Reports.cs`, `QuoteStream.cs`, `Extensions.cs`, `Result.cs`, `Option.cs`, `ResultExtensions.cs`, `DiscountTable.cs` | none |
| Workflows (compose boundaries and pure parts) | `DeliveryPipeline.cs`, `OrderProcessor.cs`, `OrderService.cs`, `QuoteWorkflow.cs`, `ReportingWorkflow.cs`, `AssignmentWorkflow.cs` | only through the boundary classes |
| Boundaries | `DataLoader.cs` (files in), `ReportExporter.cs` (files out), `LineStreaming.cs`, `NativeEffortCalculator.cs` + `NativeMethods.cs` (native library) | file system, native code |
| Shell | `src/DeliveryQuotes.App/Program.cs` | console, exit codes |

## 5. Mapping of every technical requirement

| Requirement | Where |
|---|---|
| Pure calculations, validation, report logic; immutable core values | all "pure domain" files; `record` / `ImmutableArray`; `DeliveryRequest`, `Courier` are get-only classes |
| Pattern matching for domain decisions | `OrderWorkflow.Quote/Confirm/Describe`, `Assigner.Step`, `AssignmentPlan.ToExport`, `ResultExtensions`, `OrderBook.Apply` |
| Pipeline with 3 named stages | `PipelineStages.Stage1Select.cs` `SelectEligible` -> `Stage2Price.cs` `PriceRequests` -> `Stage3Summary.cs` `BuildSummary`, composed in `DeliveryPipeline.Run` |
| Higher-order function with a delegate, two policies | `PriceRequests(.., Func<DeliveryRequest,decimal> policy)`: `Standard` vs `Express`; `Assigner.Plan(.., policy)`: `FirstFit` vs `BestFit` |
| Validated factories, no unchecked construction | `DeliveryRequest.Create`, `Courier.Create`, `DiscountTable.Create` (private constructors, get-only properties) |
| Distinct cases handled by pattern matching | `RequestValidation`, `OrderState`, `TransitionResult`, `Result<T>`, `Option<T>`, `AssignmentOutcome` |
| Invalid sequence rejected, no partial state | `OrderWorkflow.Confirm` before quote is refused; `OrderBook` returns the same book on refusal; exports are atomic |
| `Result` for 3+ domain failures | `InvalidRequest`, `InvalidInput`, `NotFound`, `InvalidState`, `OutsideServiceLimits`, `DuplicateOrder` (`Error.cs`) |
| `Map` and `Bind` in a real workflow | `ResultExtensions.cs`; chains in `QuoteWorkflow.QuoteOne`, `OrderService.Process`, `ReportingWorkflow.Run`, `AssignmentWorkflow.Run` |
| Missing lookups explicit | `OrderBook.Find`, `PricingPolicies.Find`, `DiscountTable.Find`, `ScoreConfig.Find`, `AssignmentPolicies.Find` return `Option<T>` |
| File and parsing exceptions at the boundary | `DataLoader`, `ReportExporter` (`FileNotFound`, `FileUnreadable`, `MalformedContent`, `FileNotWritable`) |
| 2+ custom extension methods, used | `Extensions.cs`: `OrderByDescendingThenById`, `CollectResults`, `Curry` |
| 2 reports (filter, projection, ordering + tie-break, GroupBy, aggregate) | `Reports.DispatchList`, `Reports.CustomerWorkload` |
| Curried function and two partially applied functions | `Scoring.CreateScore` with `ScoreConfig.DistanceFirst` / `HeavyFirst` |
| `yield return`, deferred execution, materialisation | `QuoteStream.PriceLines`, `LineStreaming.ReadLines`; `ToImmutableArray` in `Reports.DispatchList`; `ToArray` in `DataLoader` |
| Load JSON/CSV, save or export | CSV orders, JSON discounts and couriers in; JSON report and plan out (`data/`, `out/`) |
| Resource disposal and ownership | section 8 |
| Native C library, P/Invoke, adapter, real test and failure test | section 9 |

## 6. Run instructions

Prerequisite: **.NET SDK 8.0** (checked with 8.0.133). No C compiler is needed (the native binaries are included). Working directory: the folder that contains `FinalProject.sln`.

```
dotnet build
dotnet test
dotnet run --project src/DeliveryQuotes.App -- reports data/deliveries.csv standard distance-first out/reports.json
```

`tools/demo.sh` (Git Bash, Linux, macOS) or `tools\demo.bat` (Windows) run every workflow and every expected failure once. All paths are command-line arguments (relative to the working directory); sample files are in `data/`; the output folder `out/` is created when needed.

**Commands**

| Command (prefix `dotnet run --project src/DeliveryQuotes.App --`) | Workflow |
|---|---|
| `data/requests.csv both` | 1 |
| `lifecycle data/requests.csv standard`, `process data/requests.csv standard R-001 full data/discounts.json` | 2 |
| `quote standard R-001 Aigerim 2 1 data/discounts.json` | 3 |
| `reports data/deliveries.csv standard distance-first out/reports.json` | 4 |
| `assign data/deliveries.csv data/couriers.json first-fit distance-first out/plan.json` | 5 |

**Expected results (sample data).** Quote summary `data/requests.csv`: Standard total 8130, Express 12520. Dispatch list (`distance-first`): D-05, D-06, D-10, D-02, D-04, D-03, D-01, D-08. Workload: Dana 452 points, Aigerim 189, Madina 144, Nurlan 144, Timur 24. Assignment (couriers 300, 200, 150 points): D-05 (332 points) is never assigned; `first-fit` loads 288 / 189 / 144 (2, 3, 2 orders), `best-fit` loads 288 / 189 / 144 (4, 2, 1 orders), so D-06 goes to C-1 with first-fit and to C-3 with best-fit.

**Failure demonstrations** (exit codes: 0 ok, 1 file missing / unreadable / not writable, 2 malformed content, 3 domain failure, 4 native library problem, 64 bad arguments, 70 unexpected)

| Failure | Command | Result |
|---|---|---|
| invalid quantity | `quote standard R-001 Aigerim -3 1` | `Rejected [InvalidRequest]` (3) |
| missing lookup | `process data/requests.csv standard R-777 full` | `Rejected [NotFound]` (3) |
| invalid sequence | `process data/requests.csv standard R-001 confirm` | `Rejected [InvalidState]` (3) |
| missing file | `reports data/nope.csv standard distance-first out/x.json` | `File problem [FileNotFound]` (1) |
| malformed content | `reports data/requests_invalid.csv ...` or `assign ... data/couriers_invalid.json ...` | `File problem [MalformedContent]` (2) |
| native library missing | delete the `runtimes` folder next to the built `DeliveryQuotes.App.dll`, run `reports ...` | `Native library problem [NativeLibraryUnavailable]` (4) |

## 7. Persistence, validation, errors

CSV orders (`Id,Customer,DistanceKm,WeightKg`) are parsed and validated line by line; a file with any bad line fails as a whole with every line number (no partial data). JSON couriers and discounts are validated by their factories (capacity 0 to 10000, percent 0 to 100, no duplicates). The loader catches `FileNotFoundException`, `DirectoryNotFoundException`, `IOException`, `UnauthorizedAccessException`, `JsonException` and returns a `Failure` naming the file; unexpected exceptions are not caught. Output files are written to a temporary file and moved over the target only when complete, so a failure keeps the old file.

## 8. Resource ownership, materialisation, retention

| Resource | Owner | Release point | On failure |
|---|---|---|---|
| `StreamReader` (orders) in `LineStreaming` | the iterator (`using`) | end of enumeration, exception, or early stop | disposed, asserted by tests |
| `StreamWriter` (export) in `ReportExporter` | `Export` (`using`) | end of the block | temp file removed, target unchanged |
| `File.ReadAllText` (JSON) | the .NET API (opens and closes internally) | before it returns | exception caught at the boundary |
| native library | the runtime, loaded on first call | process end | no native memory allocated |

Materialisation: file lines to `string[]` then `ImmutableArray<DeliveryRequest>` in `DataLoader`; each report once with `ToImmutableArray`. A deferred query (`QuoteStream.PriceLines`) captures its source and policy only while it is alive and never leaves `Reports.DispatchList`. No cache, no event subscription, no long-lived collection exists. Trade-off: streaming holds one line but keeps the file open and repeats work if enumerated again; a snapshot costs memory but is stable and frees the file immediately, so a snapshot is used (parsing must be all-or-nothing).

## 9. Native C library

- **Source:** `native/include/deliveryscore.h`, `native/src/deliveryscore.c`. **Function:** `int32_t dq_load_points(int32_t distance_m, int32_t weight_g)` returns `distance_m / 100 + weight_g / 250` (one point per full 100 m and per full 250 g, rounded down); valid range 0 to 100000 each (100 km, 100 kg); returns -1 outside the range; no memory is allocated. Manual check: `(2000, 1000) = 20 + 4 = 24`, `(30000, 8000) = 300 + 32 = 332`.
- **Used for:** the effort points of every eligible order in workflows 4 and 5 (workload report, courier capacity).
- **Binding and adapter:** `NativeMethods.cs` (DllImport, cdecl, plus a resolver that searches `DQ_NATIVE_DIR`, then `runtimes/<rid>/native`, then the application folder); `NativeEffortCalculator.cs` validates and converts units before the call and turns load, binding and rejection problems into `Result` failures.
- **Platform and compilers:** declared Windows x64 and Linux x64. Linux `.so`: gcc 13.3.0 (Ubuntu 24.04, x86-64). Windows `.dll`: mingw-w64 `x86_64-w64-mingw32-gcc` 13, statically linked (imports only KERNEL32.dll and msvcrt.dll). macOS: build with `native/build.sh`.
- **Build:** `native/build.sh` (Linux, macOS, MSYS2) or `native/build.bat` (Visual Studio). Linux command: `gcc -std=c99 -O2 -Wall -Wextra -fPIC -shared -Iinclude src/deliveryscore.c -o runtimes/linux-x64/native/libdeliveryscore.so`. Windows command: `gcc -std=c99 -O2 -Wall -Wextra -shared -static-libgcc -s -Iinclude src/deliveryscore.c -o runtimes/win-x64/native/deliveryscore.dll`.
- **Binary location:** `native/runtimes/<rid>/native/` (prebuilt); `DeliveryQuotes.Core.csproj` copies it to `runtimes/<rid>/native/` next to the built application.
- **Difficulty and resolution:** the first run with a plain DllImport did not find the library, because the default probing searches only the application folder, not `runtimes/<rid>/native` (that is only for NuGet packages). Fixed with `NativeLibrary.SetDllImportResolver`; the failure had been reported as a clear `NativeLibraryUnavailable` Result.
- **Tests:** a real call against hand values and a controlled and a real load failure (`NativeIntegrationTests`).

## 10. Tests

153 test methods (163 cases) in `tests/DeliveryQuotes.Tests`, using xUnit assertions: far more than the 12 required (8 pure and domain, 2 workflow, 2 file).

| Area | Tests |
|---|---|
| Pure functions and domain validation | `FactoryTests`, `CourierTests`, `ParserTests`, `AssignerTests`, `ReportTests`, `ScoringTests`, `ExtensionTests`, `OrderWorkflowTests`, `Stage1And2Tests`, `WorkflowTests` |
| Complete workflows | `ReportingWorkflowTests`, `AssignmentWorkflowTests`, `OrderServiceTests`, `LifecycleTests`, `QuoteWorkflowTests` |
| File handling (missing file, malformed content) | `DataLoaderTests`, `CourierLoaderTests`, workflow tests (missing, malformed, unwritable) |
| Safe operation order, state preservation | `OrderWorkflowTests`, `OrderBookTests` |
| Failure propagation (Map, Bind, CollectResults skip later steps) | `ResultTests`, `QuoteWorkflowTests`, `OrderServiceTests`, `AssignmentWorkflowTests` |
| Lazy processing, resource handling | `LazyProcessingTests`, `ResourceTests` |
| Native integration | `NativeIntegrationTests` (real call, controlled failure, real missing library) |
| Boundary, invalid, empty, missing cases | in every area, e.g. exact 30 km / 20 kg, capacity 0 and 10000, empty files, no eligible orders, unknown ids |

File tests use temporary folders created per test and deleted afterwards.

**Test outcome (2026-11-15):** `dotnet build`: 0 errors, 0 warnings. All 163 test cases (153 test methods) passed on Linux x64 with the real native library. This check ran the same test methods with a local assertion runner because the xUnit packages could not be downloaded in the environment where the check was made; `dotnet test` runs the same methods with xUnit.

**Defects found and fixed during development**

1. A `record` request could be copied with `with` and re-initialised, bypassing the factory. Fixed by making `DeliveryRequest` a get-only class (test `NoUncheckedConstruction_...`).
2. The native library was not found by default probing (section 9). Fixed with a resolver (test `TheNativeBinaryIsFoundInTheRuntimeFolder_...`).
3. An iterator method validates its arguments only at the first `MoveNext`, which hides errors. Fixed with a wrapper that checks at once (test `PriceLines_CreatingTheEnumerable_RunsNothing_ButChecksArgumentsAtOnce`).

## 11. Responsibilities

| Member | Group | Modules and features | Tests |
|---|---|---|---|
| Zhaksylyk Aruzhan | IT-2511 | `DeliveryRequest` + `RequestValidation` + `RequestParser` (factory, parsing), `Courier`, `Result` / `Option` / `Error` / `ResultExtensions` (Map, Bind), `Extensions` (3 extensions), `Scoring` (currying, partial application), `DiscountTable`, `QuoteWorkflow` | `FactoryTests`, `CourierTests`, `ParserTests`, `ResultTests`, `ExtensionTests`, `ScoringTests`, `QuoteWorkflowTests` |
| Neda Farhat | IT-2511 | pipeline stages 1-2 and `PricingPolicies`, `OrderState` + `TransitionResult` + `OrderWorkflow` (states, pattern matching), `DataLoader` (CSV, JSON, couriers), `LineStreaming`, `ReportExporter`, `Reports` (two LINQ reports), `QuoteStream` (yield return), `Assigner` + `AssignmentPolicies` | `Stage1And2Tests`, `OrderWorkflowTests`, `DataLoaderTests`, `CourierLoaderTests`, `ResourceTests`, `ReportTests`, `LazyProcessingTests`, `AssignerTests` |
| Maratov Ravil | IT-2511 | pipeline stage 3 and `DeliveryPipeline`, `OrderBook`, `OrderProcessor`, `OrderService`, native C library + `NativeMethods` + `NativeEffortCalculator`, `ReportingWorkflow`, `AssignmentWorkflow`, `Program` (console), build scripts, Git repository | `WorkflowTests`, `OrderBookTests`, `LifecycleTests`, `OrderServiceTests`, `NativeIntegrationTests`, `ReportingWorkflowTests`, `AssignmentWorkflowTests` |

## 12. Limitations, references

- **Limitations:** a console application only; native binaries are included for Windows x64 and Linux x64 (macOS must be built); the xUnit run on Windows was not performed in the environment where the project was checked, but the Windows DLL was loaded and called under Wine; the confirmation number in `process` is always 1 (single order); no database, login or web interface. No planned feature is presented as implemented.
- **External materials:** .NET 8 class library (System.Text.Json, System.Collections.Immutable, System.Runtime.InteropServices), xUnit, the Microsoft P/Invoke documentation (https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvokeTests), course lecture and assignment materials. No third-party libraries other than the test packages.
