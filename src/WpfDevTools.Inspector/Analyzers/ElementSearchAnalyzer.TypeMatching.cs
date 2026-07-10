namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class ElementSearchAnalyzer
{
    private static bool MatchesType(
        Type actualType,
        string? requestedType,
        string[]? requestedTypes,
        string matchMode,
        string typeMatchMode)
    {
        if (!string.Equals(typeMatchMode, "assignable", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesString(actualType.Name, requestedType, requestedTypes, matchMode);
        }

        var candidates = !string.IsNullOrWhiteSpace(requestedType)
            ? [requestedType!]
            : requestedTypes;
        if (candidates is not { Length: > 0 })
        {
            return true;
        }

        return candidates.Any(candidate => MatchesAssignableType(actualType, candidate, matchMode));
    }

    private static bool MatchesAssignableType(Type actualType, string? requestedType, string matchMode)
    {
        if (string.IsNullOrWhiteSpace(requestedType))
        {
            return true;
        }

        for (var current = actualType; current is not null; current = current.BaseType)
        {
            if (MatchesTypeName(current, requestedType!, matchMode))
            {
                return true;
            }
        }

        return actualType.GetInterfaces().Any(type => MatchesTypeName(type, requestedType!, matchMode));
    }

    private static bool MatchesTypeName(Type actualType, string requestedType, string matchMode)
        => MatchesValue(actualType.Name, requestedType, matchMode)
           || MatchesValue(actualType.FullName, requestedType, matchMode);
}
