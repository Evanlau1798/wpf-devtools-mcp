using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Catalog;

internal static class UiComposerCompositionExamples
{
    private static readonly JsonElement MultipleCardsFragment = JsonSerializer.Deserialize<JsonElement>("""
        {
          "kind": "stack",
          "slots": {
            "stack": [
              {
                "kind": "wpfui.card",
                "slots": {
                  "content": [
                    { "kind": "text", "properties": { "value": "First card" } }
                  ]
                }
              },
              {
                "kind": "wpfui.card",
                "slots": {
                  "content": [
                    { "kind": "text", "properties": { "value": "Second card" } }
                  ]
                }
              }
            ]
          }
        }
        """);

    internal static object[] ForPackScope(IReadOnlyCollection<string>? packIds)
    {
        if (packIds is { Count: > 0 }
            && !packIds.Contains("wpfui", StringComparer.Ordinal))
        {
            return [];
        }

        return
        [
            new
            {
                id = "core.stack.multiple-cards",
                purpose = "Place multiple ordered pack blocks in a slot that accepts the core stack kind.",
                placementGuidance = "Use this fragment as a slot child, then replace sample text and card content with supported catalog blocks.",
                fragment = MultipleCardsFragment
            }
        ];
    }
}
