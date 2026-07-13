namespace WpfDevTools.Mcp.Server.Composer.Drafts;

internal static class BlueprintInputResolver
{
    public static BlueprintDraftStore Store { get; } = new();

    public static BlueprintInputResolution Resolve(string blueprintJsonOrDraftRef)
    {
        if (!blueprintJsonOrDraftRef.StartsWith(BlueprintDraftStore.ReferencePrefix, StringComparison.Ordinal))
        {
            return new BlueprintInputResolution(
                true,
                false,
                string.Empty,
                blueprintJsonOrDraftRef,
                null);
        }

        var draft = Store.Resolve(blueprintJsonOrDraftRef);
        return draft.Success
            ? new BlueprintInputResolution(true, true, draft.DraftRef, draft.BlueprintJson, null)
            : new BlueprintInputResolution(false, true, draft.DraftRef, string.Empty, draft.Error);
    }
}
