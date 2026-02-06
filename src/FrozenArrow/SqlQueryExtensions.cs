using FrozenArrow.Query;
using FrozenArrow.Query.Sql;

namespace FrozenArrow;

/// <summary>
/// Extension methods for SQL query support (Phase 8).
/// </summary>
public static class SqlQueryExtensions
{
    /// <summary>
    /// Executes a SQL query against the FrozenArrow collection and returns dynamic results.
    /// Phase 8: SQL queries use the same logical plan optimization pipeline as LINQ.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The FrozenArrow collection.</param>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>Query results as dynamic objects.</returns>
    /// <example>
    /// var results = data.ExecuteSql("SELECT * FROM data WHERE Age > 30");
    /// foreach (var row in results) 
    /// {
    ///     Console.WriteLine($"Name: {row.Name}, Age: {row.Age}"); // Dynamic access!
    /// }
    /// </example>
    public static IEnumerable<dynamic> ExecuteSql<T>(this FrozenArrow<T> source, string sql)
        where T : notnull
    {
        var queryable = source.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        // Enable logical plans and caching for SQL (best performance)
        provider.UseLogicalPlanExecution = true;
        provider.UseLogicalPlanCache = true;

        return provider.ExecuteSqlQueryDynamic(sql);
    }

    /// <summary>
    /// Executes a SQL query and projects results onto a specified type.
    /// Phase 8: Strongly-typed SQL results with automatic mapping.
    /// </summary>
    /// <typeparam name="T">The source element type.</typeparam>
    /// <typeparam name="TResult">The result type to project to.</typeparam>
    /// <param name="source">The FrozenArrow collection.</param>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>Query results as strongly-typed objects.</returns>
    /// <example>
    /// public record PersonDto(string Name, int Age);
    /// 
    /// var results = data.ExecuteSql&lt;Person, PersonDto&gt;("SELECT Name, Age FROM data WHERE Age > 30");
    /// foreach (var person in results) 
    /// {
    ///     Console.WriteLine($"{person.Name} is {person.Age} years old");
    /// }
    /// </example>
    public static IEnumerable<TResult> ExecuteSql<T, TResult>(this FrozenArrow<T> source, string sql)
        where T : notnull
    {
        var queryable = source.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        provider.UseLogicalPlanExecution = true;
        provider.UseLogicalPlanCache = true;

        return provider.ExecuteSqlQuery<TResult>(sql);
    }

    /// <summary>
    /// Executes a SQL query and returns a single scalar result.
    /// </summary>
    /// <example>
    /// var count = data.ExecuteSqlScalar&lt;Person, int&gt;("SELECT COUNT(*) FROM data WHERE Age > 30");
    /// var totalSales = data.ExecuteSqlScalar&lt;Order, decimal&gt;("SELECT SUM(Amount) FROM data");
    /// </example>
    public static TResult ExecuteSqlScalar<T, TResult>(this FrozenArrow<T> source, string sql)
        where T : notnull
    {
        var queryable = source.AsQueryable();
        var provider = (ArrowQueryProvider)queryable.Provider;

        provider.UseLogicalPlanExecution = true;
        provider.UseLogicalPlanCache = true;

        return provider.ExecuteSqlQueryScalar<TResult>(sql);
    }
}
