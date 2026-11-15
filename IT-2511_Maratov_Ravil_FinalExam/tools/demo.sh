#!/usr/bin/env sh
# Runs every workflow once (success and expected failures). Run from any folder; needs the .NET SDK 8.
cd "$(dirname "$0")/.." || exit 1
dotnet build --nologo -v q || exit 1
run() { echo; echo "\$ dotnet run -- $*"; dotnet run --no-build --project src/DeliveryQuotes.App -- "$@"; echo "(exit code $?)"; }
mkdir -p out
run data/requests.csv both                                              # workflow 1: eligibility and cost pipeline
run lifecycle data/requests.csv standard                                # workflow 2: order states (submit, quote, confirm)
run process data/requests.csv standard R-001 full data/discounts.json   # workflow 2 with discount (Result chain)
run quote standard R-001 Aigerim 2 1 data/discounts.json                # workflow 3: instant quote with discount lookup
run reports data/deliveries.csv standard distance-first out/reports.json   # workflow 4: two LINQ reports + native call + export
run assign data/deliveries.csv data/couriers.json first-fit distance-first out/plan_first_fit.json   # workflow 5: courier assignment
run assign data/deliveries.csv data/couriers.json best-fit distance-first out/plan_best_fit.json     # same input, second policy
echo; echo "---- expected failures ----"
run quote standard R-001 Aigerim -3 1                                   # domain failure: invalid quantity
run process data/requests.csv standard R-777 full                       # domain failure: missing order (lookup)
run process data/requests.csv standard R-001 confirm                    # domain failure: invalid sequence (no quote yet)
run reports data/nope.csv standard distance-first out/x.json            # file failure: missing file
run reports data/requests_invalid.csv standard distance-first out/x.json   # file failure: malformed content
run assign data/deliveries.csv data/couriers_invalid.json first-fit distance-first out/x.json   # malformed JSON content
