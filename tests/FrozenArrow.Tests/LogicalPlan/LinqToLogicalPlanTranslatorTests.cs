using System.Linq.Expressions;
using FrozenArrow.Query;
using FrozenArrow.Query.LogicalPlan;

namespace FrozenArrow.Tests.LogicalPlan;

/// <summary>
/// Tests for LINQ to Logical Plan translation.
/// These tests verify basic translator functionality without complex expression tree building.
/// </summary>
public class LinqToLogicalPlanTranslatorTests
{
    private static readonly Dictionary<string, Type> TestSchema = new()
    {
        ["Id"] = typeof(int),
        ["Name"] = typeof(string),
        ["Age"] = typeof(int),
        ["Category"] = typeof(string),
        ["Sales"] = typeof(double)
    };

    private static readonly Dictionary<string, int> TestColumnIndexMap = new()
    {
        ["Id"] = 0,
        ["Name"] = 1,
        ["Age"] = 2,
        ["Category"] = 3,
        ["Sales"] = 4
    };

    [Fact]
    public void Translate_SimpleScan_CreatesScanPlan()
    {
        // Arrange
        var source = new object();
        var translator = new LinqToLogicalPlanTranslator(
            source, typeof(TestRecord), TestSchema, TestColumnIndexMap, 1_000);
        
        // Create a constant expression (represents the initial query)
        var expression = Expression.Constant(source);

        // Act
        var plan = translator.Translate(expression);

        // Assert
        Assert.IsType<ScanPlan>(plan);
        var scan = (ScanPlan)plan;
        Assert.Equal(1_000, scan.EstimatedRowCount);
    }

    [Fact]
    public void Constructor_ValidParameters_DoesNotThrow()
    {
        // Arrange & Act
        var translator = new LinqToLogicalPlanTranslator(
            new object(),
            typeof(TestRecord),
            TestSchema,
            TestColumnIndexMap,
            1_000);

        // Assert
        Assert.NotNull(translator);
    }

    [Fact]
    public void Translate_ConstantExpression_CreatesScanPlan()
    {
        // Arrange
        var source = new object();
        var translator = new LinqToLogicalPlanTranslator(
            source, typeof(TestRecord), TestSchema, TestColumnIndexMap, 5_000);
        
        var expression = Expression.Constant(source);

        // Act
        var plan = translator.Translate(expression);

        // Assert - Should create a ScanPlan with correct row count
        var scan = Assert.IsType<ScanPlan>(plan);
        Assert.Equal(5_000, scan.EstimatedRowCount);
        Assert.Equal(TestSchema, scan.OutputSchema);
    }

    private class TestRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Category { get; set; } = string.Empty;
        public double Sales { get; set; }
    }
}

