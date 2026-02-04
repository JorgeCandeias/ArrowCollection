using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace FrozenArrow.Query;

/// <summary>
/// Configuration options for query plan caching.
/// </summary>
public sealed class QueryPlanCacheOptions
{
    /// <summary>
    /// Default cache options.
    /// </summary>
    public static QueryPlanCacheOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets whether query plan caching is enabled.
    /// Default: true.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of cached query plans.
    /// When exceeded, oldest entries are evicted.
    /// Default: 256 (sufficient for most applications).
    /// </summary>
    public int MaxCacheSize { get; set; } = 256;
}

/// <summary>
/// Caches analyzed query plans to avoid repeated expression tree analysis.
/// </summary>
/// <remarks>
/// Query plan caching provides significant performance improvements for repeated queries:
/// - Eliminates ~2-3ms expression analysis overhead per query
/// - Particularly beneficial for short-circuit operations (Any, First)
/// - Thread-safe for concurrent query execution
/// 
/// The cache key is based on the structural representation of the expression tree,
/// so queries with identical structure will share the same cached plan.
/// 
/// Cache entries include the full plan with constant values, so queries like
/// "Age > 30" and "Age > 40" will have separate cache entries.
/// </remarks>
internal sealed class QueryPlanCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly QueryPlanCacheOptions _options;
    private long _accessCounter;

    /// <summary>
    /// Gets the number of cached query plans.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets cache hit statistics for diagnostics.
    /// </summary>
    public CacheStatistics Statistics { get; } = new();

    public QueryPlanCache(QueryPlanCacheOptions? options = null)
    {
        _options = options ?? QueryPlanCacheOptions.Default;
    }

    /// <summary>
    /// Tries to get a cached query plan for the given expression.
    /// </summary>
    /// <param name="expression">The LINQ expression to look up.</param>
    /// <param name="plan">The cached plan if found.</param>
    /// <returns>True if a cached plan was found, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPlan(Expression expression, out QueryPlan? plan)
    {
        if (!_options.EnableCaching)
        {
            plan = null;
            return false;
        }

        var key = ComputeCacheKey(expression);
        
        if (_cache.TryGetValue(key, out var entry))
        {
            // Update access time for LRU eviction
            entry.LastAccess = Interlocked.Increment(ref _accessCounter);
            Statistics.RecordHit();
            plan = entry.Plan;
            return true;
        }

        Statistics.RecordMiss();
        plan = null;
        return false;
    }

    /// <summary>
    /// Caches a query plan for the given expression.
    /// </summary>
    /// <param name="expression">The LINQ expression.</param>
    /// <param name="plan">The analyzed query plan to cache.</param>
    public void CachePlan(Expression expression, QueryPlan plan)
    {
        if (!_options.EnableCaching)
            return;

        var key = ComputeCacheKey(expression);
        var entry = new CacheEntry(plan, Interlocked.Increment(ref _accessCounter));

        _cache.TryAdd(key, entry);

        // Evict if over capacity (simple LRU approximation)
        if (_cache.Count > _options.MaxCacheSize)
        {
            EvictOldestEntries();
        }
    }

    /// <summary>
    /// Clears all cached query plans.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        Statistics.Reset();
    }

    /// <summary>
    /// Computes a cache key from an expression tree.
    /// </summary>
    /// <remarks>
    /// Uses a custom expression visitor to build a structural key that includes:
    /// - Method names and types
    /// - Member access paths
    /// - Constant values
    /// - Lambda parameter bindings
    /// 
    /// This ensures that structurally identical expressions produce the same key,
    /// while different expressions produce different keys.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ComputeCacheKey(Expression expression)
    {
        // Use a custom visitor for more accurate structural comparison
        // than Expression.ToString() which can miss some differences
        var keyBuilder = new ExpressionKeyBuilder();
        keyBuilder.Visit(expression);
        return keyBuilder.GetKey();
    }

    /// <summary>
    /// Evicts the oldest entries when cache is over capacity.
    /// </summary>
    private void EvictOldestEntries()
    {
        // Simple eviction: remove oldest 25% of entries
        var targetCount = _options.MaxCacheSize * 3 / 4;
        var entriesToRemove = _cache
            .OrderBy(kvp => kvp.Value.LastAccess)
            .Take(_cache.Count - targetCount)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in entriesToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private sealed class CacheEntry(QueryPlan plan, long lastAccess)
    {
        public QueryPlan Plan { get; } = plan;
        public long LastAccess { get; set; } = lastAccess;
    }
}

/// <summary>
/// Statistics for query plan cache performance monitoring.
/// </summary>
public sealed class CacheStatistics
{
    private long _hits;
    private long _misses;

    /// <summary>
    /// Gets the number of cache hits.
    /// </summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>
    /// Gets the number of cache misses.
    /// </summary>
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>
    /// Gets the cache hit rate (0.0 to 1.0).
    /// </summary>
    public double HitRate
    {
        get
        {
            var total = Hits + Misses;
            return total > 0 ? (double)Hits / total : 0.0;
        }
    }

    internal void RecordHit() => Interlocked.Increment(ref _hits);
    internal void RecordMiss() => Interlocked.Increment(ref _misses);

    internal void Reset()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    public override string ToString() =>
        $"Hits: {Hits}, Misses: {Misses}, HitRate: {HitRate:P1}";
}

/// <summary>
/// Builds a structural key from an expression tree for cache lookup.
/// </summary>
/// <remarks>
/// This visitor produces a deterministic string representation of an expression
/// that can be used as a cache key. It handles:
/// - Method calls with argument types
/// - Member access chains
/// - Constant values (with type information)
/// - Lambda expressions and parameters
/// - Binary and unary operators
/// 
/// The key is designed to be:
/// - Unique for structurally different expressions
/// - Identical for structurally equivalent expressions
/// - Fast to compute (single pass through expression tree)
/// </remarks>
internal sealed class ExpressionKeyBuilder : ExpressionVisitor
{
    private readonly System.Text.StringBuilder _builder = new(256);

    public string GetKey() => _builder.ToString();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        _builder.Append(node.Method.DeclaringType?.Name ?? "?");
        _builder.Append('.');
        _builder.Append(node.Method.Name);
        _builder.Append('(');

        // Visit object (for instance methods)
        if (node.Object != null)
        {
            Visit(node.Object);
            if (node.Arguments.Count > 0)
                _builder.Append(',');
        }

        // Visit arguments
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            Visit(node.Arguments[i]);
        }

        _builder.Append(')');
        return node;
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        _builder.Append("?(");
        for (int i = 0; i < node.Parameters.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            _builder.Append(node.Parameters[i].Name);
            _builder.Append(':');
            _builder.Append(node.Parameters[i].Type.Name);
        }
        _builder.Append(")=>");
        Visit(node.Body);
        return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        _builder.Append('(');
        Visit(node.Left);
        _builder.Append(GetOperatorSymbol(node.NodeType));
        Visit(node.Right);
        _builder.Append(')');
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        _builder.Append(GetUnaryOperatorSymbol(node.NodeType));
        _builder.Append('(');
        Visit(node.Operand);
        _builder.Append(')');
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value == null)
        {
            _builder.Append("null");
        }
        else if (node.Value is IQueryable)
        {
            // For queryable sources, use the type name (not the value)
            _builder.Append("Query<");
            _builder.Append(node.Type.GetGenericArguments().FirstOrDefault()?.Name ?? node.Type.Name);
            _builder.Append('>');
        }
        else
        {
            // Include type and value for accurate cache keys
            _builder.Append(node.Type.Name);
            _builder.Append(':');
            _builder.Append(node.Value.ToString());
        }
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != null)
        {
            Visit(node.Expression);
            _builder.Append('.');
        }
        _builder.Append(node.Member.Name);
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _builder.Append(node.Name ?? "$p");
        return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        _builder.Append("new ");
        _builder.Append(node.Type.Name);
        _builder.Append('(');
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            if (node.Members != null && i < node.Members.Count)
            {
                _builder.Append(node.Members[i].Name);
                _builder.Append('=');
            }
            Visit(node.Arguments[i]);
        }
        _builder.Append(')');
        return node;
    }

    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        Visit(node.NewExpression);
        _builder.Append('{');
        for (int i = 0; i < node.Bindings.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            var binding = node.Bindings[i];
            _builder.Append(binding.Member.Name);
            _builder.Append('=');
            if (binding is MemberAssignment assignment)
            {
                Visit(assignment.Expression);
            }
        }
        _builder.Append('}');
        return node;
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        _builder.Append('(');
        Visit(node.Test);
        _builder.Append('?');
        Visit(node.IfTrue);
        _builder.Append(':');
        Visit(node.IfFalse);
        _builder.Append(')');
        return node;
    }

    private static string GetOperatorSymbol(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Add => "+",
        ExpressionType.Subtract => "-",
        ExpressionType.Multiply => "*",
        ExpressionType.Divide => "/",
        ExpressionType.Modulo => "%",
        ExpressionType.Equal => "==",
        ExpressionType.NotEqual => "!=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.AndAlso => "&&",
        ExpressionType.OrElse => "||",
        ExpressionType.And => "&",
        ExpressionType.Or => "|",
        ExpressionType.ExclusiveOr => "^",
        ExpressionType.Coalesce => "??",
        _ => $"[{nodeType}]"
    };

    private static string GetUnaryOperatorSymbol(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Not => "!",
        ExpressionType.Negate => "-",
        ExpressionType.UnaryPlus => "+",
        ExpressionType.Convert => "cast",
        ExpressionType.ConvertChecked => "cast!",
        ExpressionType.Quote => "quote",
        _ => $"[{nodeType}]"
    };
}
