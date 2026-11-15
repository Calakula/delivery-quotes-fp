@echo off
rem Runs every workflow once (success and expected failures). Needs the .NET SDK 8.
cd /d "%~dp0\.."
dotnet build --nologo -v q || exit /b 1
if not exist out mkdir out
set RUN=dotnet run --no-build --project src\DeliveryQuotes.App --
echo. & echo ---- workflows ----
%RUN% data\requests.csv both
%RUN% lifecycle data\requests.csv standard
%RUN% process data\requests.csv standard R-001 full data\discounts.json
%RUN% quote standard R-001 Aigerim 2 1 data\discounts.json
%RUN% reports data\deliveries.csv standard distance-first out\reports.json
%RUN% assign data\deliveries.csv data\couriers.json first-fit distance-first out\plan_first_fit.json
%RUN% assign data\deliveries.csv data\couriers.json best-fit distance-first out\plan_best_fit.json
echo. & echo ---- expected failures ----
%RUN% quote standard R-001 Aigerim -3 1
%RUN% process data\requests.csv standard R-777 full
%RUN% process data\requests.csv standard R-001 confirm
%RUN% reports data\nope.csv standard distance-first out\x.json
%RUN% reports data\requests_invalid.csv standard distance-first out\x.json
%RUN% assign data\deliveries.csv data\couriers_invalid.json first-fit distance-first out\x.json
