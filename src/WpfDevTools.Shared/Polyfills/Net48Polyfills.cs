#if NET48
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill for init-only properties in .NET Framework 4.8
/// </summary>
internal static class IsExternalInit
{
}

/// <summary>
/// Polyfill for required members in .NET Framework 4.8
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute
{
}

/// <summary>
/// Polyfill for compiler feature required attribute in .NET Framework 4.8
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
    public CompilerFeatureRequiredAttribute(string featureName)
    {
        FeatureName = featureName;
    }

    public string FeatureName { get; }
    public bool IsOptional { get; init; }
}
#endif
