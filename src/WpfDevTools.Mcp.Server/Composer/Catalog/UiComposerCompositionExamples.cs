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
                    {
                      "kind": "stack",
                      "slots": {
                        "stack": [
                          { "kind": "wpfui.textBlock", "properties": { "text": "First card" } }
                        ]
                      }
                    }
                  ]
                }
              },
              {
                "kind": "wpfui.card",
                "slots": {
                  "content": [
                    {
                      "kind": "stack",
                      "slots": {
                        "stack": [
                          { "kind": "wpfui.textBlock", "properties": { "text": "Second card" } }
                        ]
                      }
                    }
                  ]
                }
              }
            ]
          }
        }
        """);

    internal static object[] ForResolvedBlockKinds(IReadOnlyCollection<string> blockKinds)
    {
        if (!blockKinds.Contains("wpfui.card", StringComparer.Ordinal)
            || !blockKinds.Contains("wpfui.textBlock", StringComparer.Ordinal))
        {
            return [];
        }

        return
        [
            new
            {
                id = "core.stack.multiple-cards",
                purpose = "Place multiple ordered pack blocks in a slot that accepts the core stack kind.",
                placementGuidance = "Use this directly renderable fragment as a slot child, then replace the sample text with application content.",
                fragment = MultipleCardsFragment
            }
        ];
    }
}
