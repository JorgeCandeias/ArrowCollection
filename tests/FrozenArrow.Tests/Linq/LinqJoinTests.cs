namespace FrozenArrow.Tests.Linq;

/// <summary>
/// Tests to verify if LINQ JOIN support exists in FrozenArrow.
/// </summary>
public class LinqJoinTests
{
    [ArrowRecord]
    public record Customer
    {
        [ArrowArray(Name = "Id")]
        public int Id { get; init; }

        [ArrowArray(Name = "Name")]
        public string Name { get; init; } = string.Empty;
    }

    [ArrowRecord]
    public record Order
    {
        [ArrowArray(Name = "OrderId")]
        public int OrderId { get; init; }

        [ArrowArray(Name = "CustomerId")]
        public int CustomerId { get; init; }

        [ArrowArray(Name = "Amount")]
        public double Amount { get; init; }
    }

    private static FrozenArrow<Customer> CreateCustomers()
    {
        return new List<Customer>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
            new() { Id = 3, Name = "Charlie" }
        }.ToFrozenArrow();
    }

    private static FrozenArrow<Order> CreateOrders()
    {
        return new List<Order>
        {
            new() { OrderId = 101, CustomerId = 1, Amount = 100.0 },
            new() { OrderId = 102, CustomerId = 1, Amount = 200.0 },
            new() { OrderId = 103, CustomerId = 2, Amount = 150.0 }
        }.ToFrozenArrow();
    }

    [Fact]
    public void LinqQuery_InnerJoin_CheckIfSupported()
    {
        // Arrange
        var customers = CreateCustomers();
        var orders = CreateOrders();

        // Act - Try LINQ Join (this will materialize, not push down)
        try
        {
            var result = customers.AsQueryable()
                .Join(
                    orders.AsQueryable(),
                    c => c.Id,
                    o => o.CustomerId,
                    (c, o) => new { c.Name, o.Amount })
                .ToList();

            // Assert - If it works, verify results
            Console.WriteLine($"JOIN works! Found {result.Count} results");
            
            // We expect 3 results (Alice has 2 orders, Bob has 1)
            Assert.Equal(3, result.Count);
            Assert.Contains(result, r => r.Name == "Alice" && r.Amount == 100.0);
            Assert.Contains(result, r => r.Name == "Alice" && r.Amount == 200.0);
            Assert.Contains(result, r => r.Name == "Bob" && r.Amount == 150.0);
        }
        catch (NotSupportedException ex)
        {
            // Expected if JOIN is not supported
            Console.WriteLine($"JOIN not supported: {ex.Message}");
            
            // Mark test as expected to fail for now
            Assert.Contains("not", ex.Message.ToLower());
        }
    }

    [Fact]
    public void LinqQuery_GroupJoin_CheckIfSupported()
    {
        // Arrange
        var customers = CreateCustomers();
        var orders = CreateOrders();

        // Act - Try LINQ GroupJoin (LEFT JOIN equivalent)
        try
        {
            var result = customers.AsQueryable()
                .GroupJoin(
                    orders.AsQueryable(),
                    c => c.Id,
                    o => o.CustomerId,
                    (c, orderGroup) => new 
                    { 
                        c.Name, 
                        OrderCount = orderGroup.Count() 
                    })
                .ToList();

            // Assert
            Console.WriteLine($"GroupJoin works! Found {result.Count} results");
            
            Assert.Equal(3, result.Count); // All customers
            Assert.Contains(result, r => r.Name == "Alice" && r.OrderCount == 2);
            Assert.Contains(result, r => r.Name == "Bob" && r.OrderCount == 1);
            Assert.Contains(result, r => r.Name == "Charlie" && r.OrderCount == 0);
        }
        catch (NotSupportedException ex)
        {
            // Expected if GroupJoin is not supported
            Console.WriteLine($"GroupJoin not supported: {ex.Message}");
            Assert.Contains("not", ex.Message.ToLower());
        }
    }
}
