@echo off
REM Benchmark Execution Script for FrozenArrow
REM Date: February 8, 2026
REM Runs all benchmark categories and saves results

echo ========================================
echo FrozenArrow Benchmark Execution
echo ========================================
echo Start Time: %date% %time%
echo.

cd /d "%~dp0"

REM Create results directory
if not exist "results-2026-02" mkdir "results-2026-02"

echo Running SQL Benchmarks...
dotnet run -c Release -- --filter "*Sql*" --exporters json --artifacts "results-2026-02/sql"
echo.

echo Running Advanced Feature Benchmarks...
dotnet run -c Release -- --filter "*AdvancedFeature*" --exporters json --artifacts "results-2026-02/advanced"
echo.

echo Running Caching Benchmarks...
dotnet run -c Release -- --filter "*Caching*" --exporters json --artifacts "results-2026-02/caching"
echo.

echo Running Filter Benchmarks...
dotnet run -c Release -- --filter "*Filter*" --exporters json --artifacts "results-2026-02/filter"
echo.

echo Running Aggregation Benchmarks...
dotnet run -c Release -- --filter "*Aggregation*" --exporters json --artifacts "results-2026-02/aggregation"
echo.

echo Running GroupBy Benchmarks...
dotnet run -c Release -- --filter "*GroupBy*" --exporters json --artifacts "results-2026-02/groupby"
echo.

echo Running Pagination Benchmarks...
dotnet run -c Release -- --filter "*Pagination*" --exporters json --artifacts "results-2026-02/pagination"
echo.

echo Running Serialization Benchmarks...
dotnet run -c Release -- --filter "*Serialization*" --exporters json --artifacts "results-2026-02/serialization"
echo.

echo Running Construction Benchmarks...
dotnet run -c Release -- --filter "*Construction*" --exporters json --artifacts "results-2026-02/construction"
echo.

echo Running Enumeration Benchmarks...
dotnet run -c Release -- --filter "*Enumeration*" --exporters json --artifacts "results-2026-02/enumeration"
echo.

echo ========================================
echo All Benchmarks Complete!
echo End Time: %date% %time%
echo ========================================
echo.
echo Results saved to: results-2026-02/
echo.
pause
