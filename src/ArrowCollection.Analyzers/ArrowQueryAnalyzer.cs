using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArrowCollection.Analyzers;

/// <summary>
/// Roslyn analyzer that detects inefficient or unsupported query patterns on ArrowQuery.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArrowQueryAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> SupportedQueryableMethods = new()
    {
        "Where", "Select", "GroupBy", "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending", "Take", "Skip", "First",
        "FirstOrDefault", "Single", "SingleOrDefault", "Any", "All",
        "Count", "LongCount", "Sum", "Average", "Min", "Max",
        "ToList", "ToArray"
    };

    private static readonly HashSet<string> SupportedAggregates = new()
    {
        "Sum", "Count", "Average", "Min", "Max", "LongCount"
    };

    private static readonly HashSet<string> SafePredicateMethods = new()
    {
        "System.String.Equals",
        "System.String.Contains",
        "System.String.StartsWith",
        "System.String.EndsWith",
        "System.String.IsNullOrEmpty",
        "System.String.IsNullOrWhiteSpace",
        "System.Object.Equals",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.WrongWhereMethod,
            DiagnosticDescriptors.UnsupportedLinqMethod,
            DiagnosticDescriptors.ComplexPredicateWarning,
            DiagnosticDescriptors.UnsupportedGroupByProjection,
            DiagnosticDescriptors.OrPredicateWarning);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a method call (e.g., .Where(...))
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol is null)
            return;

        // Check if the receiver is ArrowQuery<T> or IQueryable<T> backed by ArrowQuery
        var receiverType = GetReceiverType(memberAccess, context.SemanticModel);
        if (!IsArrowQueryType(receiverType))
            return;

        var methodName = methodSymbol.Name;
        var containingType = methodSymbol.ContainingType?.ToDisplayString();

        // ARROWQUERY001: Enumerable.Where on ArrowQuery
        if (IsEnumerableMethod(containingType, methodName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.WrongWhereMethod,
                invocation.GetLocation()));
            return;
        }

        // ARROWQUERY002: Unsupported LINQ method
        if (containingType == "System.Linq.Queryable")
        {
            if (!SupportedQueryableMethods.Contains(methodName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedLinqMethod,
                    invocation.GetLocation(),
                    methodName));
                return;
            }
        }

        // ARROWQUERY003: Complex predicate analysis for Where
        if (methodName == "Where" && invocation.ArgumentList.Arguments.Count > 0)
        {
            AnalyzeWherePredicateComplexity(context, invocation);
        }

        // ARROWQUERY004: GroupBy projection analysis
        if (methodName == "Select" && IsAfterGroupBy(memberAccess, context.SemanticModel))
        {
            AnalyzeGroupByProjection(context, invocation);
        }
    }

    private static bool IsEnumerableMethod(string? containingType, string methodName)
    {
        // Check if it's a LINQ method from Enumerable that should be from Queryable
        return containingType == "System.Linq.Enumerable" &&
               (methodName == "Where" || methodName == "Select" || methodName == "OrderBy" ||
                methodName == "OrderByDescending" || methodName == "GroupBy" ||
                methodName == "Take" || methodName == "Skip");
    }

    private void AnalyzeWherePredicateComplexity(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument?.Expression is not LambdaExpressionSyntax lambda)
            return;

        // Check for OR expressions
        var orExpressions = lambda.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Where(b => b.IsKind(SyntaxKind.LogicalOrExpression))
            .ToList();

        if (orExpressions.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.OrPredicateWarning,
                orExpressions[0].GetLocation()));
        }

        // Find method calls that aren't simple property access
        var methodCalls = lambda.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();

        foreach (var call in methodCalls)
        {
            var symbol = context.SemanticModel.GetSymbolInfo(call).Symbol as IMethodSymbol;
            if (symbol is null)
                continue;

            var fullName = $"{symbol.ContainingType?.ToDisplayString()}.{symbol.Name}";
            if (IsSafePredicateMethod(fullName))
                continue;

            // Skip lambda parameters (they're not external method calls)
            if (IsLambdaParameterAccess(call, lambda))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ComplexPredicateWarning,
                call.GetLocation(),
                symbol.Name));
        }
    }

    private void AnalyzeGroupByProjection(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument?.Expression is not LambdaExpressionSyntax lambda)
            return;

        // Analyze the projection body
        var body = lambda.Body;

        // If it's an anonymous type creation, check each member
        if (body is AnonymousObjectCreationExpressionSyntax anonymousType)
        {
            foreach (var initializer in anonymousType.Initializers)
            {
                if (!IsValidGroupByProjectionMember(initializer.Expression, context.SemanticModel))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.UnsupportedGroupByProjection,
                        initializer.GetLocation(),
                        initializer.Expression.ToString()));
                }
            }
        }
    }

    private bool IsValidGroupByProjectionMember(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        // Allow: g.Key
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text == "Key")
                return true;
        }

        // Allow: g.Sum(...), g.Count(), etc.
        if (expression is InvocationExpressionSyntax invocation)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is not null && SupportedAggregates.Contains(symbol.Name))
                return true;
        }

        return false;
    }

    private static bool IsSafePredicateMethod(string fullName)
    {
        return SafePredicateMethods.Contains(fullName);
    }

    private static bool IsLambdaParameterAccess(
        InvocationExpressionSyntax call,
        LambdaExpressionSyntax lambda)
    {
        // Check if the method call is on a lambda parameter (e.g., x.Property.Contains("value"))
        if (call.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var current = memberAccess.Expression;
            while (current is MemberAccessExpressionSyntax nestedMember)
            {
                current = nestedMember.Expression;
            }

            if (current is IdentifierNameSyntax identifier)
            {
                // Check if it's a lambda parameter
                var parameterNames = lambda switch
                {
                    SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter.Identifier.Text },
                    ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters
                        .Select(p => p.Identifier.Text).ToArray(),
                    _ => Array.Empty<string>()
                };

                return parameterNames.Contains(identifier.Identifier.Text);
            }
        }

        return false;
    }

    private static ITypeSymbol? GetReceiverType(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        return semanticModel.GetTypeInfo(memberAccess.Expression).Type;
    }

    private static bool IsArrowQueryType(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        // Check if it's ArrowQuery<T>
        var typeName = type.ToDisplayString();
        if (typeName.StartsWith("ArrowCollection.Query.ArrowQuery<"))
            return true;

        // Check if it's IQueryable<T> that might be backed by ArrowQuery
        // We check the interfaces and base types
        foreach (var iface in type.AllInterfaces)
        {
            var ifaceName = iface.ToDisplayString();
            if (ifaceName.StartsWith("System.Linq.IQueryable<") ||
                ifaceName.StartsWith("System.Linq.IOrderedQueryable<"))
            {
                // For IQueryable, we need to trace back to see if source is ArrowQuery
                // This is a heuristic - check if the chain includes ArrowQuery
                return typeName.Contains("ArrowQuery") ||
                       typeName.Contains("ArrowCollection");
            }
        }

        return false;
    }

    private static bool IsAfterGroupBy(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is null)
            return false;

        var typeName = receiverType.ToDisplayString();
        
        // Check for IGrouping<TKey, TElement>
        if (typeName.Contains("IGrouping<"))
            return false; // This is accessing a single group, not the grouped query

        // Check if it's IQueryable<IGrouping<...>>
        if (typeName.Contains("IQueryable") && typeName.Contains("IGrouping"))
            return true;

        return false;
    }
}
