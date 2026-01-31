namespace ArrowCollection;

/// <summary>
/// Marks a class or struct as eligible for use with <see cref="ArrowCollection{T}"/>.
/// Only types decorated with this attribute can be used as the generic type argument
/// when creating Arrow collections.
/// </summary>
/// <remarks>
/// <para>
/// Both classes and structs (including readonly structs) are supported.
/// </para>
/// <para>
/// <strong>Important:</strong> ArrowCollection is a frozen collection. Once built, the data
/// cannot be modified. Items are reconstructed from columnar storage on each enumeration.
/// Modifying the original source data or the reconstructed items has no effect on the
/// collection's stored data.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ArrowRecordAttribute : Attribute
{
}
