using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace FrozenArrow;

/// <summary>
/// Delegate for setting a field value on a struct by reference.
/// </summary>
/// <typeparam name="TDeclaring">The struct type that declares the field.</typeparam>
/// <typeparam name="TField">The field type.</typeparam>
/// <param name="instance">A reference to the struct instance.</param>
/// <param name="value">The value to set.</param>
public delegate void RefFieldSetter<TDeclaring, TField>(ref TDeclaring instance, TField value);

/// <summary>
/// Provides high-performance field access through IL-emitted delegates.
/// This class creates and caches optimized getter and setter delegates for field access,
/// bypassing reflection overhead after initial setup.
/// </summary>
/// <remarks>
/// <para>
/// Inspired by the Orleans serialization framework's approach to field access.
/// </para>
/// <para>
/// For struct types, use <see cref="GetRefSetter{TDeclaring, TField}"/> to avoid copying
/// the struct when setting field values.
/// </para>
/// </remarks>
public static class FieldAccessor
{
    private static readonly ConcurrentDictionary<FieldInfo, Delegate> _getters = new();
    private static readonly ConcurrentDictionary<FieldInfo, Delegate> _setters = new();
    private static readonly ConcurrentDictionary<FieldInfo, Delegate> _refSetters = new();

    /// <summary>
    /// Gets a typed getter delegate for the specified field.
    /// </summary>
    /// <typeparam name="TDeclaring">The type that declares the field.</typeparam>
    /// <typeparam name="TField">The field type.</typeparam>
    /// <param name="field">The field to create a getter for.</param>
    /// <returns>A delegate that gets the field value from an instance.</returns>
    public static Func<TDeclaring, TField> GetGetter<TDeclaring, TField>(FieldInfo field)
    {
        return (Func<TDeclaring, TField>)_getters.GetOrAdd(field, f => CreateGetter<TDeclaring, TField>(f));
    }

    /// <summary>
    /// Gets a typed setter delegate for the specified field.
    /// For class types, this is the preferred setter method.
    /// </summary>
    /// <typeparam name="TDeclaring">The type that declares the field.</typeparam>
    /// <typeparam name="TField">The field type.</typeparam>
    /// <param name="field">The field to create a setter for.</param>
    /// <returns>A delegate that sets the field value on an instance.</returns>
    /// <remarks>
    /// For struct types, consider using <see cref="GetRefSetter{TDeclaring, TField}"/> instead
    /// to avoid copying the struct.
    /// </remarks>
    public static Action<TDeclaring, TField> GetSetter<TDeclaring, TField>(FieldInfo field)
    {
        return (Action<TDeclaring, TField>)_setters.GetOrAdd(field, f => CreateSetter<TDeclaring, TField>(f));
    }

    /// <summary>
    /// Gets a ref-based setter delegate for the specified field.
    /// This is the preferred setter for struct types as it avoids copying.
    /// </summary>
    /// <typeparam name="TDeclaring">The struct type that declares the field. Must be a value type.</typeparam>
    /// <typeparam name="TField">The field type.</typeparam>
    /// <param name="field">The field to create a setter for.</param>
    /// <returns>A delegate that sets the field value on a struct instance by reference.</returns>
    /// <remarks>
    /// This method generates IL that takes the struct by reference, allowing field modification
    /// without copying the entire struct. This is essential for readonly structs and for
    /// performance-critical scenarios with mutable structs.
    /// </remarks>
    public static RefFieldSetter<TDeclaring, TField> GetRefSetter<TDeclaring, TField>(FieldInfo field)
        where TDeclaring : struct
    {
        return (RefFieldSetter<TDeclaring, TField>)_refSetters.GetOrAdd(field, f => CreateRefSetter<TDeclaring, TField>(f));
    }

    private static Func<TDeclaring, TField> CreateGetter<TDeclaring, TField>(FieldInfo field)
    {
        var declaringType = field.DeclaringType ?? throw new ArgumentException("Field must have a declaring type.", nameof(field));
        
        var method = new DynamicMethod(
            name: $"Get_{declaringType.Name}_{field.Name}",
            returnType: typeof(TField),
            parameterTypes: [typeof(TDeclaring)],
            owner: typeof(FieldAccessor),
            skipVisibility: true);

        var il = method.GetILGenerator();
        
        // For value types, we need to load the address to access the field
        if (typeof(TDeclaring).IsValueType)
        {
            il.Emit(OpCodes.Ldarga_S, 0);
            il.Emit(OpCodes.Ldfld, field);
        }
        else
        {
            // Load the instance
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
        }
        
        il.Emit(OpCodes.Ret);

        return method.CreateDelegate<Func<TDeclaring, TField>>();
    }

    private static Action<TDeclaring, TField> CreateSetter<TDeclaring, TField>(FieldInfo field)
    {
        var declaringType = field.DeclaringType ?? throw new ArgumentException("Field must have a declaring type.", nameof(field));
        
        var method = new DynamicMethod(
            name: $"Set_{declaringType.Name}_{field.Name}",
            returnType: typeof(void),
            parameterTypes: [typeof(TDeclaring), typeof(TField)],
            owner: typeof(FieldAccessor),
            skipVisibility: true);

        var il = method.GetILGenerator();
        
        // Load the instance
        il.Emit(OpCodes.Ldarg_0);
        
        // Load the value
        il.Emit(OpCodes.Ldarg_1);
        
        // Store the field
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);

        return method.CreateDelegate<Action<TDeclaring, TField>>();
    }

    private static RefFieldSetter<TDeclaring, TField> CreateRefSetter<TDeclaring, TField>(FieldInfo field)
        where TDeclaring : struct
    {
        var declaringType = field.DeclaringType ?? throw new ArgumentException("Field must have a declaring type.", nameof(field));
        
        var method = new DynamicMethod(
            name: $"SetRef_{declaringType.Name}_{field.Name}",
            returnType: typeof(void),
            parameterTypes: [typeof(TDeclaring).MakeByRefType(), typeof(TField)],
            owner: typeof(FieldAccessor),
            skipVisibility: true);

        var il = method.GetILGenerator();
        
        // Load the address of the struct (arg 0 is already a ref/pointer)
        il.Emit(OpCodes.Ldarg_0);
        
        // Load the value
        il.Emit(OpCodes.Ldarg_1);
        
        // Store the field
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);

        return method.CreateDelegate<RefFieldSetter<TDeclaring, TField>>();
    }
}
