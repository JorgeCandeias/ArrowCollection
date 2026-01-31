# ArrowCollection - Frozen Collection with Apache Arrow Compression

ArrowCollection is a .NET library that implements a frozen generic collection with columnar compression using Apache Arrow. It's designed for scenarios where you need significant in-memory compression savings for massive datasets, while accepting the performance trade-off of reconstructing items on-the-fly during enumeration.

## Features

- **Immutable/Frozen**: Once created, the collection cannot be modified
- **Columnar Compression**: Uses Apache Arrow format for efficient compression
- **Type-Safe**: Strongly typed generic collection
- **Simple API**: Easy to use with the `.ToArrowCollection()` extension method
- **Property-Based**: Automatically captures all public instance properties

## Installation

Add the ArrowCollection library to your project:

```bash
dotnet add reference path/to/ArrowCollection/ArrowCollection.csproj
```

## Usage

### Basic Example

```csharp
using ArrowCollection;

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    public DateTime BirthDate { get; set; }
}

// Create your data
var people = new[]
{
    new Person { Id = 1, Name = "Alice", Age = 30, BirthDate = new DateTime(1994, 5, 15) },
    new Person { Id = 2, Name = "Bob", Age = 25, BirthDate = new DateTime(1999, 8, 22) },
    new Person { Id = 3, Name = "Charlie", Age = 35, BirthDate = new DateTime(1989, 3, 10) }
};

// Convert to ArrowCollection (frozen collection)
var collection = people.ToArrowCollection();

// Enumerate the collection (items are materialized on-the-fly)
foreach (var person in collection)
{
    Console.WriteLine($"{person.Name} is {person.Age} years old");
}

// Get count
Console.WriteLine($"Total people: {collection.Count}");
```

### Large Dataset Example

```csharp
// Create a large dataset
var largeDataset = Enumerable.Range(1, 1_000_000)
    .Select(i => new Person 
    { 
        Id = i, 
        Name = $"Person {i}", 
        Age = 20 + (i % 60),
        BirthDate = DateTime.Now.AddYears(-(20 + (i % 60)))
    });

// Convert to ArrowCollection - data is compressed using Apache Arrow columnar format
var collection = largeDataset.ToArrowCollection();

// The data is now stored in a compressed columnar format
// Items are reconstructed on-the-fly during enumeration
var adults = collection.Where(p => p.Age >= 18).Take(10);
```

### Supported Data Types

ArrowCollection supports the following property types:

- Integers: `int`, `long`, `short`, `sbyte`, `uint`, `ulong`, `ushort`, `byte`
- Floating Point: `float`, `double`
- Boolean: `bool`
- String: `string`
- DateTime: `DateTime`
- Nullable versions of all the above

### Working with Nullable Properties

```csharp
public class OptionalData
{
    public int? OptionalId { get; set; }
    public string? OptionalName { get; set; }
    public DateTime? OptionalDate { get; set; }
}

var data = new[]
{
    new OptionalData { OptionalId = 1, OptionalName = "Test", OptionalDate = DateTime.Now },
    new OptionalData { OptionalId = null, OptionalName = null, OptionalDate = null },
};

var collection = data.ToArrowCollection();
```

## Performance Characteristics

### Advantages
- **Memory Efficiency**: Significant compression for large datasets using Apache Arrow's columnar format
- **Multiple Enumerations**: Can enumerate the collection multiple times
- **Immutability**: Thread-safe for reading

### Trade-offs
- **Enumeration Cost**: Items are reconstructed on-the-fly, which is slower than in-memory objects
- **Not for Frequent Access**: Best suited for scenarios where data is enumerated infrequently but needs to be kept in memory
- **Property Limitation**: Only works with types that have public instance properties with getters and setters

## Use Cases

ArrowCollection is ideal for:

- **Caching large datasets** that are infrequently accessed
- **In-memory analytics** where memory is constrained
- **Reference data** that needs to be kept in memory but rarely accessed
- **Historical data** that must be available but isn't frequently queried

## Requirements

- .NET 10.0 or later
- Apache.Arrow NuGet package (automatically included)

## License

See LICENSE file for details.
