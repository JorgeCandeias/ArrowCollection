using Microsoft.CodeAnalysis;

namespace FrozenArrow.Generators;

/// <summary>
/// Diagnostic descriptors for the FrozenArrow source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor NoArrowArrayProperties = new(
        id: "ARROWCOL001",
        title: "No ArrowArray properties",
        messageFormat: "Type '{0}' marked with [ArrowRecord] has no properties marked with [ArrowArray]",
        category: "FrozenArrow",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "ARROWCOL002",
        title: "Unsupported property type",
        messageFormat: "Property '{0}' on type '{1}' has unsupported type '{2}'. Supported types: int, long, short, sbyte, uint, ulong, ushort, byte, float, double, bool, string, DateTime and their nullable variants.",
        category: "FrozenArrow",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingParameterlessConstructor = new(
        id: "ARROWCOL003",
        title: "Missing parameterless constructor",
        messageFormat: "Type '{0}' marked with [ArrowRecord] must have a public parameterless constructor",
        category: "FrozenArrow",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ManualPropertyNotSupported = new(
        id: "ARROWCOL004",
        title: "Manual property not supported",
        messageFormat: "ArrowArrayAttribute on property '{0}' is not supported because it is not an auto-property. Use the attribute on a field instead.",
        category: "FrozenArrow",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FieldWithoutSerializationName = new(
        id: "ARROWCOL005",
        title: "Field without serialization name",
        messageFormat: "Field '{0}' is marked with [ArrowArray] but has no Name specified. Consider adding Name = \"...\" to ensure stable serialization, as the field name '{0}' will be used as the column name.",
        category: "FrozenArrow",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
