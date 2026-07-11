using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class ComposerPackRoleValidator
{
    public static IReadOnlyList<BlueprintValidationIssue> Validate(UiBlueprint blueprint)
    {
        var errors = new List<BlueprintValidationIssue>();
        for (var index = 0; index < blueprint.Packs.Length; index++)
        {
            var role = blueprint.Packs[index].Role;
            if (!string.IsNullOrWhiteSpace(role) && !ComposerPackRoles.All.Contains(role))
            {
                errors.Add(new BlueprintValidationIssue(
                    $"$.packs[{index}].role",
                    "InvalidPackRole",
                    $"Pack role '{role}' is not supported.",
                    $"Use one of: {string.Join(", ", ComposerPackRoles.All.Order(StringComparer.Ordinal))}.",
                    [],
                    [],
                    null));
            }
        }

        return errors;
    }
}
