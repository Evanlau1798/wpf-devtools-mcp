namespace WpfDevTools.Inspector.Analyzers;

internal enum SceneTraversalDepthMode
{
    Visual = 0,
    Semantic = 1
}

internal static class SceneTraversalDepthModes
{
    internal static bool TryParse(string? value, out SceneTraversalDepthMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = SceneTraversalDepthMode.Semantic;
            return true;
        }

        if (string.Equals(value, "visual", StringComparison.OrdinalIgnoreCase))
        {
            mode = SceneTraversalDepthMode.Visual;
            return true;
        }

        if (string.Equals(value, "semantic", StringComparison.OrdinalIgnoreCase))
        {
            mode = SceneTraversalDepthMode.Semantic;
            return true;
        }

        mode = SceneTraversalDepthMode.Semantic;
        return false;
    }

    internal static string ToContractValue(SceneTraversalDepthMode mode)
    {
        return mode == SceneTraversalDepthMode.Semantic ? "semantic" : "visual";
    }
}
