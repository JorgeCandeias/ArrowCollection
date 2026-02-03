namespace FrozenArrow.Benchmarks;

/// <summary>
/// Standard benchmark item with 10 columns representing realistic data distribution.
/// Used across all benchmark files for consistency.
/// </summary>
[ArrowRecord]
public class QueryBenchmarkItem
{
    [ArrowArray] public int Id { get; set; }
    [ArrowArray] public string Name { get; set; } = string.Empty;
    [ArrowArray] public int Age { get; set; }
    [ArrowArray] public decimal Salary { get; set; }
    [ArrowArray] public bool IsActive { get; set; }
    [ArrowArray] public string Category { get; set; } = string.Empty;
    [ArrowArray] public string Department { get; set; } = string.Empty;
    [ArrowArray] public DateTime HireDate { get; set; }
    [ArrowArray] public double PerformanceScore { get; set; }
    [ArrowArray] public string Region { get; set; } = string.Empty;
}

/// <summary>
/// Factory to generate benchmark items with realistic data distribution.
/// </summary>
public static class QueryBenchmarkItemFactory
{
    private static readonly string[] Categories = 
        ["Engineering", "Marketing", "Sales", "HR", "Finance", "Operations", "Executive", "Support"];
    
    private static readonly string[] Departments =
        ["Dept_A", "Dept_B", "Dept_C", "Dept_D", "Dept_E"];
    
    private static readonly string[] Regions =
        ["North", "South", "East", "West", "Central"];

    public static List<QueryBenchmarkItem> Generate(int count, int seed = 42)
    {
        var random = new Random(seed);
        var items = new List<QueryBenchmarkItem>(count);
        var baseDate = new DateTime(2020, 1, 1);

        for (int i = 0; i < count; i++)
        {
            // Age: uniform distribution 20-60
            var age = 20 + random.Next(41);
            
            // IsActive: 70% active
            var isActive = random.NextDouble() < 0.7;
            
            // Salary: correlated with age, range 40K-150K
            var baseSalary = 40000 + (age - 20) * 1000;
            var salary = baseSalary + random.Next(-5000, 15000);
            
            // Category: weighted distribution (Engineering most common)
            var categoryIndex = random.NextDouble() switch
            {
                < 0.35 => 0, // Engineering 35%
                < 0.50 => 1, // Marketing 15%
                < 0.65 => 2, // Sales 15%
                < 0.75 => 3, // HR 10%
                < 0.85 => 4, // Finance 10%
                < 0.92 => 5, // Operations 7%
                < 0.97 => 6, // Executive 5%
                _ => 7       // Support 3%
            };

            items.Add(new QueryBenchmarkItem
            {
                Id = i,
                Name = $"Person_{i}",
                Age = age,
                Salary = salary,
                IsActive = isActive,
                Category = Categories[categoryIndex],
                Department = Departments[random.Next(Departments.Length)],
                HireDate = baseDate.AddDays(random.Next(1500)),
                PerformanceScore = Math.Round(1.0 + random.NextDouble() * 4.0, 2),
                Region = Regions[random.Next(Regions.Length)]
            });
        }

        return items;
    }
}
