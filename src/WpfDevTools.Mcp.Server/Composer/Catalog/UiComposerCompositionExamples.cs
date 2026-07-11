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

    private static readonly JsonElement MultipleCardsWrapperBlueprint = JsonSerializer.SerializeToElement(new
    {
        schemaVersion = "wpfdevtools.ui-blueprint.v1",
        name = "CatalogCompositionExample",
        packs = new[]
        {
            new { id = "wpfui", version = "0.1.0", required = true, role = "primary" }
        },
        primaryPack = "wpfui",
        layout = new
        {
            kind = "wpfui.card",
            slots = new { content = new[] { MultipleCardsFragment } }
        }
    });

    internal static object[] ForResolvedComposableBlockKinds(IReadOnlyCollection<string> blockKinds)
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
                placementMode = "slotChild",
                compatibleParentSlots = new[] { "wpfui.card.content" },
                fragment = MultipleCardsFragment,
                wrapperBlueprint = MultipleCardsWrapperBlueprint
            }
        ];
    }
}
