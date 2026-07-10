namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class ElementSearchAnalyzer
{
    private static bool MatchesType(
        Type actualType,
        string? requestedType,
        string[]? requestedTypes,
        string typeMatchMode)
    {
        if (!string.Equals(typeMatchMode, "assignable", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesString(actualType.Name, requestedType, requestedTypes, "exact");
        }

        var candidates = !string.IsNullOrWhiteSpace(requestedType)
            ? [requestedType]
            : requestedTypes;
        if (candidates is not { Length: > 0 })
        {
            return true;
        }

        return candidates.Any(candidate => MatchesAssignableType(actualType, candidate));
    }

    private static bool MatchesAssignableType(Type actualType, string? requestedType)
    {
        if (string.IsNullOrWhiteSpace(requestedType))
        {
            return true;
        }

        for (var current = actualType; current is not null; current = current.BaseType)
        {
            if (MatchesTypeName(current, requestedType))
            {
                return true;
            }
        }

        return actualType.GetInterfaces().Any(type => MatchesTypeName(type, requestedType));
    }

    private static bool MatchesTypeName(Type actualType, string requestedType)
        => string.Equals(actualType.Name, requestedType, StringComparison.Ordinal)
           || string.Equals(actualType.FullName, requestedType, StringComparison.Ordinal);
}
