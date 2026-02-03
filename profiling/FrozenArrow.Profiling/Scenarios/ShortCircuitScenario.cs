using FrozenArrow.Query;

namespace FrozenArrow.Profiling.Scenarios;

/// <summary>
/// Profiles short-circuit operations (Any, First, FirstOrDefault) that can benefit
/// from streaming predicate evaluation rather than full bitmap materialization.
/// </summary>
/// <remarks>
/// Short-circuit optimization benefits:
/// - Any(): Returns immediately on first match (O(k) where k = position of first match)
/// - First(): Returns immediately on first match
/// - With zone maps: Can skip entire chunks that cannot contain matches
/// 
/// The improvement is most dramatic when:
/// - Matches are found early in the data
/// - Zone maps can eliminate large portions of the data
/// - The dataset is large (millions of rows)
/// 
/// Expected speedups:
/// - Match in first 1% of data: ~100x faster
/// - Match in first 10% of data: ~10x faster
/// - No matches or match at end: Similar to bitmap (slightly slower due to row-by-row)
/// </remarks>
public sealed class ShortCircuitScenario : BaseScenario
{
    public override string Name => "ShortCircuit";
    public override string Description => "Any/First operations with early-exit optimization";

    // Results
    private bool _anyEarlyMatch;
    private bool _anyLateMatch;
    private bool _anyNoMatch;
    private ProfilingRecord? _firstEarlyMatch;
    private ProfilingRecord? _firstOrDefaultNoMatch;

    public override object? RunIteration()
    {
        // Test 1: Any() with match at beginning (Age > 19 will match row 0)
        // Expected: Nearly instant - match found immediately
        _anyEarlyMatch = Data.AsQueryable().Where(x => x.Age > 19).Any();

        // Test 2: Any() with restrictive filter (Age > 60, ~10% of data matches)
        // Expected: Find match relatively early
        _anyLateMatch = Data.AsQueryable().Where(x => x.Age > 60).Any();

        // Test 3: Any() with no match (Age > 100 - impossible value)
        // Expected: Full scan time (no short-circuit possible)
        _anyNoMatch = Data.AsQueryable().Where(x => x.Age > 100).Any();

        // Test 4: First() with match at beginning (Age > 19)
        // Expected: Nearly instant
        _firstEarlyMatch = Data.AsQueryable().Where(x => x.Age > 19).First();

        // Test 5: FirstOrDefault() with no match
        // Expected: Full scan time but no exception
        _firstOrDefaultNoMatch = Data.AsQueryable().Where(x => x.Age > 100).FirstOrDefault();

        return _anyEarlyMatch && _anyLateMatch && !_anyNoMatch && _firstEarlyMatch != null;
    }

    public override (object? Result, Dictionary<string, double> PhaseMicroseconds) RunIterationWithPhases()
    {
        CurrentPhases.Clear();

        // Phase 1: Any with early match (best case for short-circuit)
        // Age > 19 matches almost all rows, so first row is likely a match
        StartPhase("AnyEarlyMatch");
        _anyEarlyMatch = Data.AsQueryable().Where(x => x.Age > 19).Any();
        EndPhase("AnyEarlyMatch");

        // Phase 2: Any with restrictive filter (~10% selectivity, Age > 60)
        StartPhase("AnyRestrictiveMatch");
        _anyLateMatch = Data.AsQueryable().Where(x => x.Age > 60).Any();
        EndPhase("AnyRestrictiveMatch");

        // Phase 3: Any with no match (worst case - full scan)
        StartPhase("AnyNoMatch");
        _anyNoMatch = Data.AsQueryable().Where(x => x.Age > 100).Any();
        EndPhase("AnyNoMatch");

        // Phase 4: First with early match
        StartPhase("FirstEarlyMatch");
        _firstEarlyMatch = Data.AsQueryable().Where(x => x.Age > 19).First();
        EndPhase("FirstEarlyMatch");

        // Phase 5: FirstOrDefault with no match
        StartPhase("FirstOrDefaultNoMatch");
        _firstOrDefaultNoMatch = Data.AsQueryable().Where(x => x.Age > 100).FirstOrDefault();
        EndPhase("FirstOrDefaultNoMatch");

        // Phase 6: Any with multi-predicate (Age > 30 && Salary > 100000)
        StartPhase("AnyMultiPredicate");
        var anyMulti = Data.AsQueryable().Where(x => x.Age > 30 && x.Salary > 100000).Any();
        EndPhase("AnyMultiPredicate");

        return (
            _anyEarlyMatch && _anyLateMatch && !_anyNoMatch && _firstEarlyMatch != null,
            new Dictionary<string, double>(CurrentPhases)
        );
    }

    public override Dictionary<string, string> GetMetadata()
    {
        return new Dictionary<string, string>
        {
            ["AnyEarlyMatch"] = _anyEarlyMatch.ToString(),
            ["AnyLateMatch"] = _anyLateMatch.ToString(),
            ["AnyNoMatch"] = _anyNoMatch.ToString(),
            ["FirstEarlyMatch_Id"] = _firstEarlyMatch?.Id.ToString() ?? "null",
            ["FirstOrDefaultNoMatch"] = _firstOrDefaultNoMatch == null ? "null" : "unexpected"
        };
    }
}
