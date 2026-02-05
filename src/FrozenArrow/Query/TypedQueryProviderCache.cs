using System.Collections.Concurrent;
using System.Linq.Expressions;
using Apache.Arrow;

namespace FrozenArrow.Query;

/// <summary>
/// Cache for typed delegates to eliminate reflection overhead in ArrowQueryProvider.
/// This allows us to call generic methods without MakeGenericMethod + Invoke.
/// </summary>
internal static class TypedQueryProviderCache
{
    // Cache for ExtractSourceData<T> delegates
    private static readonly ConcurrentDictionary<Type, Delegate> ExtractSourceDataCache = new();
    
    // Cache for CreateQuery<T> delegates  
    private static readonly ConcurrentDictionary<Type, Func<ArrowQueryProvider, Expression, IQueryable>> CreateQueryCache = new();
    
    // Cache for Execute<T> delegates
    private static readonly ConcurrentDictionary<Type, Func<ArrowQueryProvider, Expression, object?>> ExecuteCache = new();

    /// <summary>
    /// Gets or creates a typed ExtractSourceData delegate for the specified element type.
    /// </summary>
    public static (RecordBatch, int, Dictionary<string, int>, Func<RecordBatch, int, object>, ZoneMap?, QueryPlanCache) 
        ExtractSourceData(Type elementType, object source)
    {
        var del = ExtractSourceDataCache.GetOrAdd(elementType, static t =>
        {
            var method = typeof(TypedQueryProviderCache)
                .GetMethod(nameof(ExtractSourceDataTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(t);
            
            return method.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(object), method.ReturnType));
        });

        var typedDelegate = (Func<object, (RecordBatch, int, Dictionary<string, int>, Func<RecordBatch, int, object>, ZoneMap?, QueryPlanCache)>)del;
        return typedDelegate(source);
    }

    /// <summary>
    /// Generic helper to extract data from FrozenArrow&lt;T&gt; using internal accessors.
    /// This is called via delegate (one-time reflection to create delegate, then fast calls).
    /// </summary>
    private static (RecordBatch, int, Dictionary<string, int>, Func<RecordBatch, int, object>, ZoneMap?, QueryPlanCache) 
        ExtractSourceDataTyped<T>(object source)
    {
        var typedSource = (FrozenArrow<T>)source;
        
        // Direct access to internal members - no reflection overhead
        var recordBatch = typedSource.RecordBatch;
        var count = typedSource.Count;
        
        // Build column index map from schema
        var columnIndexMap = new Dictionary<string, int>();
        var schema = recordBatch.Schema;
        for (int i = 0; i < schema.FieldsList.Count; i++)
        {
            columnIndexMap[schema.FieldsList[i].Name] = i;
        }
        
        // Direct method delegate - no reflection invoke
        Func<RecordBatch, int, object> createItem = (batch, index) => typedSource.CreateItemInternal(batch, index)!;
        
        // Build zone maps for the RecordBatch
        var zoneMap = ZoneMap.BuildFromRecordBatch(recordBatch, chunkSize: ParallelQueryOptions.Default.ChunkSize);
        
        // Get the shared query plan cache from the source
        var queryPlanCache = typedSource.QueryPlanCache;
        
        return (recordBatch, count, columnIndexMap, createItem, zoneMap, queryPlanCache);
    }

    /// <summary>
    /// Gets or creates a typed CreateQuery delegate for the specified element type.
    /// </summary>
    public static IQueryable CreateQuery(ArrowQueryProvider provider, Type elementType, Expression expression)
    {
        var del = CreateQueryCache.GetOrAdd(elementType, static t =>
        {
            // Create delegate for CreateQuery<T>(expression)
            return (provider, expr) =>
            {
                var method = typeof(ArrowQueryProvider)
                    .GetMethod(nameof(ArrowQueryProvider.CreateQuery), 1, [typeof(Expression)])!
                    .MakeGenericMethod(t);
                return (IQueryable)method.Invoke(provider, [expr])!;
            };
        });

        return del(provider, expression);
    }

    /// <summary>
    /// Gets or creates a typed Execute delegate for the specified element type.
    /// </summary>
    public static object? Execute(ArrowQueryProvider provider, Type elementType, Expression expression)
    {
        var del = ExecuteCache.GetOrAdd(elementType, static t =>
        {
            // Create delegate for Execute<T>(expression)
            return (provider, expr) =>
            {
                var method = typeof(ArrowQueryProvider)
                    .GetMethod(nameof(ArrowQueryProvider.Execute), 1, [typeof(Expression)])!
                    .MakeGenericMethod(t);
                return method.Invoke(provider, [expr]);
            };
        });

        return del(provider, expression);
    }
}
