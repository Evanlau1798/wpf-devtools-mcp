using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintSlotCardinalityValidator
{
    public static void AddIssues(
        UiBlueprintNode node,
        string nodePath,
        UiBlockDefinition block,
        List<BlueprintValidationIssue> errors)
    {
        foreach (var (slotName, slot) in block.Slots)
        {
            var count = node.Slots.TryGetValue(slotName, out var children) ? children.Length : 0;
            var slotPath = $"{nodePath}.slots.{slotName}";
            if (count < slot.MinItems)
            {
                errors.Add(Issue(
                    slotPath,
                    "SlotMinimumItemsNotMet",
                    $"Slot '{slotName}' requires at least {slot.MinItems} items but contains {count}.",
                    $"Add {slot.MinItems - count} allowed item(s) to slot '{slotName}'.",
                    slotName,
                    slot));
            }

            if (slot.MaxItems is int maximum && count > maximum)
            {
                errors.Add(Issue(
                    slotPath,
                    "SlotMaximumItemsExceeded",
                    $"Slot '{slotName}' allows at most {maximum} items but contains {count}.",
                    $"Remove {count - maximum} item(s) from slot '{slotName}'.",
                    slotName,
                    slot));
            }
        }
    }

    private static BlueprintValidationIssue Issue(
        string path,
        string code,
        string message,
        string repair,
        string slotName,
        UiBlockSlot slot)
        => new(path, code, message, repair, slot.AllowedKinds, [], slotName);
}
