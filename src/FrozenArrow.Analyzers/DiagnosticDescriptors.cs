using Microsoft.CodeAnalysis;

namespace FrozenArrow.Analyzers;

/// <summary>
/// Diagnostic descriptors for ArrowQuery analyzers.
/// </summary>
public static class DiagnosticDescriptors
{
    private const string Category = "FrozenArrow.Query";
    private const string PerformanceCategory = "FrozenArrow.Performance";
    private const string UsageCategory = "FrozenArrow.Usage";

    /// <summary>
    /// ARROWQUERY001: Using Enumerable.Where on ArrowQuery bypasses optimization.
    /// </summary>
    public static readonly DiagnosticDescriptor WrongWhereMethod = new(
        id: "ARROWQUERY001",
        title: "Inefficient Where() usage on ArrowQuery",
        messageFormat: "Using Enumerable.Where() on ArrowQuery<T> bypasses column filtering and causes full materialization. Use Queryable.Where() instead.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When using LINQ on ArrowQuery<T>, the Where method should come from System.Linq.Queryable, not System.Linq.Enumerable. " +
                     "Using Enumerable.Where causes the entire collection to be materialized before filtering, negating the performance benefits of columnar storage. " +
                     "Ensure your source is IQueryable<T> by calling .AsQueryable() before applying Where clauses.");

    /// <summary>
    /// ARROWQUERY002: Unsupported LINQ method on ArrowQuery.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedLinqMethod = new(
        id: "ARROWQUERY002",
        title: "Unsupported LINQ method on ArrowQuery",
        messageFormat: "'{0}' is not supported by ArrowQuery<T>. Supported methods: Where, Select, GroupBy, OrderBy, OrderByDescending, ThenBy, ThenByDescending, Take, Skip, First, FirstOrDefault, Single, SingleOrDefault, Any, All, Count, LongCount, Sum, Average, Min, Max, ToList, ToArray.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ArrowQuery supports a subset of LINQ methods that can be efficiently executed against columnar data. " +
                     "Methods like Join, GroupJoin, Distinct, Union, Intersect, Except, Zip, Reverse, SequenceEqual, and Aggregate " +
                     "are not supported and will cause a compile-time error.");

    /// <summary>
    /// ARROWQUERY003: Complex predicate may cause partial materialization.
    /// </summary>
    public static readonly DiagnosticDescriptor ComplexPredicateWarning = new(
        id: "ARROWQUERY003",
        title: "Complex predicate may cause partial materialization",
        messageFormat: "Predicate contains '{0}' which cannot be pushed to column filtering. Consider extracting this logic before the query or using a simpler predicate.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ArrowQuery can push simple predicates (comparisons, equality checks, string operations) directly to column-level evaluation. " +
                     "Complex predicates containing method calls or computed expressions require row materialization. " +
                     "For best performance, extract complex logic into variables before the query.");

    /// <summary>
    /// ARROWQUERY004: Unsupported GroupBy projection.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedGroupByProjection = new(
        id: "ARROWQUERY004",
        title: "Unsupported GroupBy projection",
        messageFormat: "GroupBy projection contains '{0}' which is not a supported aggregate. Supported aggregates: Key, Sum(), Count(), Average(), Min(), Max().",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ArrowQuery GroupBy projections must use only the group Key and supported aggregate functions. " +
                     "Operations like ToList(), First(), or custom computations on group elements are not supported " +
                     "because they would require materializing all elements in each group.");

    /// <summary>
    /// ARROWQUERY005: Mixing LINQ providers may cause unexpected behavior.
    /// </summary>
    public static readonly DiagnosticDescriptor MixedLinqProviders = new(
        id: "ARROWQUERY005",
        title: "Mixing LINQ providers",
        messageFormat: "Query mixes ArrowQuery with another IQueryable source ('{0}'). This may cause unexpected materialization.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Combining ArrowQuery with other LINQ providers (like Entity Framework or another IQueryable) " +
                     "can cause one or both sources to materialize unexpectedly. Consider separating the queries.");

    /// <summary>
    /// ARROWQUERY006: Query is fully optimized.
    /// </summary>
    public static readonly DiagnosticDescriptor QueryFullyOptimized = new(
        id: "ARROWQUERY006",
        title: "Query uses optimized column access",
        messageFormat: "Query will execute using optimized column-only access. Columns accessed: {0}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false, // Disabled by default to reduce noise
        description: "This informational diagnostic indicates that the query has been analyzed and can be fully optimized. " +
                     "Only the listed columns will be accessed during query execution.");

    /// <summary>
    /// ARROWQUERY007: Predicate uses OR which may reduce optimization.
    /// </summary>
    public static readonly DiagnosticDescriptor OrPredicateWarning = new(
        id: "ARROWQUERY007",
        title: "OR predicate reduces optimization",
        messageFormat: "Predicate uses OR (||) which cannot be fully pushed to column filtering. Consider using separate queries with Union or restructuring the predicate.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "OR predicates in Where clauses are more complex to optimize than AND predicates. " +
                     "While simple OR predicates may still be partially optimized, complex OR expressions " +
                     "may cause row-level evaluation. For best performance, use AND predicates when possible.");

    /// <summary>
    /// ARROWQUERY008: Calling ToList/ToArray materializes all results.
    /// </summary>
    public static readonly DiagnosticDescriptor MaterializationWarning = new(
        id: "ARROWQUERY008",
        title: "Query materializes all results",
        messageFormat: "Calling {0}() will materialize all {1} results into memory. Consider using Take() to limit results or streaming with foreach.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false, // Informational only
        description: "Methods like ToList() and ToArray() materialize all query results into memory. " +
                     "For large result sets, consider using Take() to limit results, or iterate with foreach " +
                     "to process items one at a time without holding all results in memory.");
}
