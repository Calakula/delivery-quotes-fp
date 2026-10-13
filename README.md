# Courier Delivery Quotes - Assignment 5 (Composing Functions Using Pipelining)

Introduction to Functional Programming, 2026-2027. This is the first working module of the final project and will be extended in Assignments 6-8.

## 1. Workflow (Task 1)

- **Topic:** price delivery requests for a city courier service.
- **Intended users:** dispatchers who need a quote list for the day's requests.
- **Planned team:** Maratov Ravil, Zhaksylyk Aruzhan, Neda Farhat (see "Team contributions"). Team size does not change the workflow.
- **Input** (CSV, header `Id,Customer,DistanceKm,WeightKg`): request id, customer name, distance in km (> 0), parcel weight in kg (>= 0; 0 is valid, e.g. documents).
- **Business rules**
  1. Service limits (Stage 1): distance <= 30 km and weight <= 20 kg. Limits are inclusive.
  2. Price (Stage 2, chosen by a policy): `Standard = 600 + 120/km`, `Express = 1000 + 180/km`; both add 50 per kg above 5 kg (exactly 5 kg is free); rounded to whole tenge.
  3. Result (Stage 3): quotes ordered by price descending (ties by Id), total price, number of requests skipped as not eligible.
- **Expected result:** a quote list and total per policy; invalid input is rejected with line-numbered messages and no quotes.

### Dataset and expected results (written before running)

`data/requests.csv`:

| Id | Customer | km | kg | Why it is in the data | Eligible | Standard | Express |
|---|---|---|---|---|---|---|---|
| R-001 | Aigerim | 2 | 1 | normal | yes | 840 | 1360 |
| R-002 | Dana | 10 | 5 | exactly 5 kg, no surcharge | yes | 1800 | 2800 |
| r-003 | Nurlan | 30 | 8 | exactly 30 km (boundary), lower-case id normalised | yes | 4350 | 6550 |
| R-004 | Aruzhan | 31 | 2 | just over distance limit | no | - | - |
| R-005 | Timur | 4.5 | 0 | zero weight is valid | yes | 1140 | 1810 |
| R-006 | Madina | 6 | 25 | over weight limit | no | - | - |

Totals: **Standard 8130**, **Express 12520** (difference 4390), 2 skipped. Order for both: R-003, R-002, R-005, R-001.

Other files: `data/requests_empty.csv` (header only, valid empty dataset -> 0 quotes, total 0) and `data/requests_invalid.csv` (4 bad lines -> rejected, exit code 2).

## 2. Inputs and validation (Task 2)

- Immutable `record` models (`Models.cs`); collections are `ImmutableArray<T>`, which cannot be changed by callers.
- Parsing and validation happen only in `RequestParser` (the boundary). Raw text never reaches the stages.
- Missing vs empty vs zero: `null` lines -> `ArgumentNullException`; no header line -> error; header only -> valid empty dataset; weight 0 -> valid.
- Invalid fields give a message with the line number; nothing is replaced by a default and no `!` operator is used on parsed data.

## 3. Pipeline stages (Task 3)

`DeliveryPipeline.Run` composes them visibly: `SelectEligible -> PriceRequests -> BuildSummary`.

| Stage | Method | Input -> Output | Rule | Example |
|---|---|---|---|---|
| 1 | `PipelineStages.SelectEligible` | `ImmutableArray<DeliveryRequest>` + `EligibilityRules` -> `ImmutableArray<DeliveryRequest>` | normalise Id (trim, upper-case) and customer; keep distance <= max and weight <= max | `r-003, 30 km, 8 kg` -> `R-003` kept; `31 km` dropped |
| 2 | `PipelineStages.PriceRequests` | `ImmutableArray<DeliveryRequest>` + `Func<DeliveryRequest, decimal>` -> `ImmutableArray<PricedRequest>` | price each request with the supplied policy | Standard, 2 km, 1 kg -> 840 |
| 3 | `PipelineStages.BuildSummary` | `ImmutableArray<PricedRequest>` + skipped count -> `QuoteSummary` | order by price desc then Id; sum total | `[R-001:840, R-002:1800]` -> `[R-002, R-001]`, total 2640 |

Trace of `r-003,Nurlan,30,8` (Standard): parsed to `DeliveryRequest("r-003","Nurlan",30,8)` -> Stage 1 `DeliveryRequest("R-003","Nurlan",30,8)` -> Stage 2 `PricedRequest(..., 4350)` (600 + 3600 + 3 kg * 50) -> Stage 3 `Quote("R-003","Nurlan",4350)`, first in the list.

## 4. Configurable rule (Task 4)

- **Higher-order function:** `PipelineStages.PriceRequests(eligible, Func<DeliveryRequest, decimal> policy)`; the delegate is invoked in `policy(r)` once per eligible request.
- **Two policies:** `PricingPolicies.Standard` and `PricingPolicies.Express`.
- The policy is selected in `Program.cs` (`PricingPolicies.TryGetByName`, from the command-line argument) and passed into `DeliveryPipeline.Run`. No mutable global setting exists. Limits are passed in as an `EligibilityRules` value.
- Same dataset, same pipeline: total 8130 vs 12520.

## 5. Purity (Task 5)

- Core stages never read the console, write files, or use clock/random/global state, and never modify their arguments (they return new arrays and records via `with`).
- All I/O is in `src/DeliveryQuotes.App/Program.cs`, after the result is computed.

## Requirement map

| Requirement | Where |
|---|---|
| Models | `src/DeliveryQuotes.Core/Models.cs` |
| Parsing and validation | `RequestParser.ParseLines`, `RequestParser.TryParseLine` |
| 3 stages | `PipelineStages.Stage1Select.cs`, `PipelineStages.Stage2Price.cs`, `PipelineStages.Stage3Summary.cs` (shared helper in `PipelineStages.Common.cs`) |
| Composition | `DeliveryPipeline.cs` |
| Higher-order function and policies | `PipelineStages.PriceRequests`, `PricingPolicies.cs` |
| Console, file I/O | `src/DeliveryQuotes.App/Program.cs` |
| Tests | `tests/DeliveryQuotes.Tests/ParserTests.cs`, `Stage1And2Tests.cs`, `WorkflowTests.cs` |

## Build, run, test

Prerequisite: .NET SDK 8.0 (checked with 8.0.131). Run all commands from the folder that contains `Assignment5.sln`.

```
dotnet build
dotnet run --project src/DeliveryQuotes.App -- data/requests.csv standard
dotnet run --project src/DeliveryQuotes.App -- data/requests.csv express
dotnet run --project src/DeliveryQuotes.App -- data/requests.csv both
dotnet run --project src/DeliveryQuotes.App -- data/requests_empty.csv both
dotnet run --project src/DeliveryQuotes.App -- data/requests_invalid.csv standard
dotnet test
```

Exit codes: 0 ok, 1 file unreadable, 2 invalid data, 64 bad arguments.

## Tests (29 xUnit cases in 19 test methods/theories)

| Required case | Tests |
|---|---|
| Normal workflow | `WorkflowTests.NormalWorkflow_StandardPolicy_...` (8130), `NormalWorkflow_SampleFileFromDataFolder_...` (12520) |
| Empty dataset | `WorkflowTests.EmptyDataset_HeaderOnly_...` |
| Boundary value | `Stage1And2Tests.Boundary_ExactLimitsAreIncluded_...`, `Boundary_FiveKgHasNoSurcharge_...` |
| Invalid and missing input | `ParserTests.InvalidLine_...` (8 cases), `MissingInput_...`, `InvalidFile_...`, `WorkflowTests.MissingCollectionOrPolicy_...` |
| Two policies | `WorkflowTests.TwoPolicies_SameInput_...`, `Stage1And2Tests.TwoPolicies_SameRequest_...`, `HigherOrderStage_InvokesPolicyDelegate_...` |
| Purity and stages | `Stage1And2Tests.Stage1_DoesNotChangeCallerData_...`, `Stage2_IsDeterministic_...`, `WorkflowTests.Stage3_OrdersByPriceThenId_AndKeepsInputUnchanged` |

**Latest test outcome:** TODO: paste the result of your own `dotnet test` run here before submitting.

## Team contributions

| Member | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Zhaksylyk Aruzhan | TODO group | `Models.cs`, `RequestParser.cs`, `data/*.csv` | `ParserTests.cs` | README sections 1-2 |
| Neda Farhat | TODO group | `PipelineStages.Common.cs`, `Stage1Select.cs`, `Stage2Price.cs`, `PricingPolicies.cs` | `Stage1And2Tests.cs` | README sections 3 (stages 1-2) and 4 |
| Maratov Ravil | IT-2511 | `Stage3Summary.cs`, `DeliveryPipeline.cs`, `Program.cs`, solution/project files | `WorkflowTests.cs` | README sections 3 (stage 3), 5, build/run/test, requirement map |
