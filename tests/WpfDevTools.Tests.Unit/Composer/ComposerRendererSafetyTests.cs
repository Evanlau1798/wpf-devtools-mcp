using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRendererSafetyTests
{
    [Theory]
    [InlineData("<Grid x:Class=\"Unsafe.View\" />", "UnsafeXamlClass")]
    [InlineData("<x:Code>void Run() { }</x:Code>", "UnsafeXamlClass")]
    [InlineData("<Button Click=\"Run\" />", "UnsafeEventHandlerAttribute")]
    [InlineData("<Button MouseEnter=\"Run\" />", "UnsafeEventHandlerAttribute")]
    [InlineData("<TextBox PasswordChanged=\"Run\" />", "UnsafeEventHandlerAttribute")]
    [InlineData("<Hyperlink RequestNavigate=\"Run\" />", "UnsafeEventHandlerAttribute")]
    [InlineData("<Button Button.Click=\"Run\" />", "UnsafeEventHandlerAttribute")]
    [InlineData("<Grid xmlns:evil=\"clr-namespace:Unsafe\" />", "UnsafeXmlNamespace")]
    [InlineData("<ObjectDataProvider MethodName=\"Start\" />", "UnsafeExecutableObject")]
    [InlineData("<XmlDataProvider Source=\"https://controlled.invalid/data.xml\" />", "UnsafePreviewIoObject")]
    [InlineData("<WebBrowser Source=\"https://controlled.invalid/\" />", "UnsafePreviewIoObject")]
    [InlineData("<Frame Source=\"https://controlled.invalid/\" />", "UnsafePreviewIoObject")]
    [InlineData("<MediaElement Source=\"https://controlled.invalid/media.mp4\" />", "UnsafePreviewIoObject")]
    [InlineData("<Image Source=\"https://controlled.invalid/image.png\" />", "UnsafePreviewUri")]
    [InlineData("<Image Source=\"//controlled.invalid/image.png\" />", "UnsafePreviewUri")]
    [InlineData("<BitmapImage UriSource=\"file:///C:/private.png\" />", "UnsafePreviewUri")]
    [InlineData("<Image><Image.Source>file:///C:/private.png</Image.Source></Image>", "UnsafePreviewUri")]
    [InlineData("<BitmapImage><BitmapImage.UriSource>https://controlled.invalid/image.png</BitmapImage.UriSource></BitmapImage>", "UnsafePreviewUri")]
    [InlineData("<BitmapImage.UriSource>\\\\controlled.invalid\\share\\image.png</BitmapImage.UriSource>", "UnsafePreviewUri")]
    [InlineData("<TextBlock Text=\"{Binding Source={StaticResource Process}}\" />", "UnsafeBindingExpression")]
    [InlineData("<TextBlock Text=\"{Binding Source = {StaticResource Process}}\" />", "UnsafeBindingExpression")]
    [InlineData("<TextBlock Text=\"{Binding FallbackValue={StaticResource Placeholder}, Source={StaticResource Process}}\" />", "UnsafeBindingExpression")]
    [InlineData("<TextBlock><TextBlock.Text><Binding Source=\"{StaticResource Process}\" /></TextBlock.Text></TextBlock>", "UnsafeBindingExpression")]
    [InlineData("<TextBlock><TextBlock.Text><MultiBinding Converter=\"{StaticResource Converter}\" /></TextBlock.Text></TextBlock>", "UnsafeBindingExpression")]
    [InlineData("<TextBlock Text=\"{Binding Tag, RelativeSource={RelativeSource PreviousData}}\" />", "UnsafeBindingExpression")]
    [InlineData("<ResourceDictionary Source=\"pack://application:,,,/Unsafe;component/App.xaml\" />", "UnsafeResourceDictionarySource")]
    [InlineData("<ResourceDictionary.Source>pack://application:,,,/Unsafe;component/App.xaml</ResourceDictionary.Source>", "UnsafeResourceDictionarySource")]
    public void RenderBlueprint_ShouldRejectUnsafeRendererOutput(string rendererTemplate, string expectedCode)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var renderer = new UiBlueprintRenderer(CreateRegistry(projectRoot));

            var result = renderer.Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == expectedCode
                && issue.JsonPath == "$.layout"
                && issue.RepairSuggestion.Length > 0);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<Grid>{{?slot.actions}}</Grid>")]
    [InlineData("<Grid>{{?slot.actions}}{{/slot.content}}</Grid>")]
    [InlineData("<Grid>{{?slot.}}<TextBlock /></Grid>")]
    public void RenderBlueprint_ShouldRejectUnmatchedOptionalSlotMarker(string rendererTemplate)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == "RendererOptionalSectionMalformed");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldRejectNestedOptionalSlotSectionsBeforeExpansion()
    {
        var projectRoot = CreateTempProjectWithSafetyPack(
            "<Grid>{{?slot.outer}}{{?slot.inner}}<TextBlock />{{/slot.inner}}{{/slot.outer}}</Grid>",
            slotsJson: """{"outer":{"allowedKinds":["*"]},"inner":{"allowedKinds":["*"]}}""");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == "RendererOptionalSectionMalformed");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldAcceptSpacedOptionalSlotMarkers()
    {
        var projectRoot = CreateTempProjectWithSafetyPack(
            "<Grid>{{ ?slot.actions}}<StackPanel>{{slot.actions}}</StackPanel>{{ /slot.actions}}</Grid>",
            slotsJson: """{"actions":{"allowedKinds":["*"]}}""");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeTrue();
            result.Xaml.Should().NotContain("slot.actions").And.NotContain("<StackPanel>");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("{\"title\":\"Authored\"}", "Authored", "Fallback")]
    [InlineData("{}", "Fallback", "Authored")]
    public void RenderBlueprint_ShouldResolveOptionalPropertySections(
        string propertiesJson,
        string expectedText,
        string omittedText)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(
            "<TextBlock>{{?property.title}}<Run Text=\"{{title}}\" />{{/property.title}}{{^property.title}}<Run Text=\"Fallback\" />{{/property.title}}</TextBlock>",
            propertiesJson: "{\"title\":{\"type\":\"string\"}}");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint(propertiesJson)));

            result.Success.Should().BeTrue();
            result.Xaml.Should().Contain(expectedText).And.NotContain(omittedText);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<Grid>{{?property.title}}</Grid>")]
    [InlineData("<Grid>{{^property.title}}{{/property.other}}</Grid>")]
    public void RenderBlueprint_ShouldRejectMalformedOptionalPropertySections(string rendererTemplate)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(
            rendererTemplate,
            propertiesJson: "{\"title\":{\"type\":\"string\"},\"other\":{\"type\":\"string\"}}");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == "RendererOptionalSectionMalformed");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<Grid><evil:Code>void Run() { }</evil:Code></Grid>", "UnsafeXamlDirective")]
    [InlineData("<TextBlock Text=\"{evil:Static Grid.Tag}\" />", "UnsafeXamlMarkupExtension")]
    [InlineData("<TextBlock Text=\"{evil:StaticExtension Grid.Tag}\" />", "UnsafeXamlMarkupExtension")]
    [InlineData("<Grid><evil:Static Member=\"Grid.Tag\" /></Grid>", "UnsafeXamlDirective")]
    public void RenderBlueprint_ShouldRejectAliasedXamlLanguageExecution(
        string rendererTemplate,
        string expectedCode)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(
            rendererTemplate,
            """{"evil":"http://schemas.microsoft.com/winfx/2006/xaml"}""");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == expectedCode);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<ImageBrush ImageSource=\"https://controlled.invalid/image.png\" />")]
    [InlineData("<ImageDrawing ImageSource=\"file:///C:/private.png\" />")]
    [InlineData("<Window Icon=\"\\\\controlled.invalid\\share\\icon.ico\" />")]
    [InlineData("<Hyperlink NavigateUri=\"https://controlled.invalid/\" />")]
    [InlineData("<ImageBrush><ImageBrush.ImageSource>file:///C:/private.png</ImageBrush.ImageSource></ImageBrush>")]
    public void RenderBlueprint_ShouldRejectLiteralUrisOnWpfUriConsumingMembers(string rendererTemplate)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == "UnsafePreviewUri");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldAllowApplicationLocalPackUrisOnWpfUriConsumingMembers()
    {
        const string rendererTemplate =
            "<ImageBrush ImageSource=\"pack://application:,,,/Safe;component/Assets/image.png\" />";
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<Image Source=\"/Assets/image.png\" />")]
    [InlineData("<Image Source=\"pack://application:,,,/Assets/image.png\" />")]
    [InlineData("<Image Source=\"pack://application:,,,/Safe;component/Assets/image.png\" />")]
    [InlineData("<Image><Image.Source>pack://application:,,,/Safe;component/Assets/image.png</Image.Source></Image>")]
    [InlineData("<BitmapImage UriSource=\"pack://application:,,,/Safe;component/Assets/image.png\" />")]
    [InlineData("<BitmapImage><BitmapImage.UriSource>pack://application:,,,/Safe;component/Assets/image.png</BitmapImage.UriSource></BitmapImage>")]
    public void RenderBlueprint_ShouldAllowApplicationLocalPackUriOnImageSource(string rendererTemplate)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<Image Source=\"{Binding Missing, FallbackValue=https://controlled.invalid/image.png}\" />")]
    [InlineData("<Hyperlink NavigateUri=\"{Binding Missing, TargetNullValue=file:///C:/private.xaml}\" />")]
    [InlineData("<Frame Source=\"{Binding Missing, FallbackValue=\\\\controlled.invalid\\share\\page.xaml}\" />")]
    public void RenderBlueprint_ShouldRejectBindingUriFallbacks(string rendererTemplate)
    {
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot))
                .Render(new RenderBlueprintRequest(Blueprint()));

            result.Errors.Should().Contain(issue => issue.Code == "UnsafePreviewUri");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldAllowConstrainedAncestorBinding()
    {
        const string rendererTemplate =
            "<ContentControl Visibility=\"{Binding Children[0].HasItems, RelativeSource={RelativeSource AncestorType={x:Type Grid}}}\" />";
        var projectRoot = CreateTempProjectWithSafetyPack(rendererTemplate);
        try
        {
            var renderer = new UiBlueprintRenderer(CreateRegistry(projectRoot));

            var result = renderer.Render(new RenderBlueprintRequest(Blueprint()));

            result.Success.Should().BeTrue();
            result.Xaml.Should().Contain("RelativeSource AncestorType={x:Type Grid}");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void XamlSafetyScanner_ShouldRejectDeclaredExternalDictionarySource()
    {
        const string source = "https://controlled.invalid/theme.xaml";

        var issues = XamlSafetyScanner.Scan(
            $"<ResourceDictionary Source=\"{source}\" />",
            [],
            [source]);

        issues.Should().ContainSingle(issue => issue.Code == "UnsafeResourceDictionarySource");
    }

    [Fact]
    public void RenderBlueprint_ShouldNotAllowRawXamlPropertyInjection()
    {
        var renderer = new UiBlueprintRenderer(PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")));

        var result = renderer.Render(new RenderBlueprintRequest(WpfUiBlueprint("""
            {
              "kind": "wpfui.button",
              "properties": {
                "rawXaml": "<ObjectDataProvider />"
              }
            }
            """)));

        result.Success.Should().BeFalse();
        result.Validation.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.rawXaml"
            && issue.Code == "UnknownProperty");
        result.Xaml.Should().NotContain("ObjectDataProvider");
    }

    [Fact]
    public void RenderBlueprint_ShouldAllowEscapedTextThatLooksLikeUnsafeXaml()
    {
        var renderer = new UiBlueprintRenderer(PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")));

        var result = renderer.Render(new RenderBlueprintRequest(WpfUiBlueprint("""
            {
              "kind": "wpfui.button",
              "properties": {
                "text": "<TextBlock Click=\"Run\" x:Class=\"Safe.View\" xmlns:evil=\"clr-namespace:Safe\" />"
              }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().Contain("&lt;TextBlock Click=&quot;Run&quot;");
        result.Xaml.Should().Contain("x:Class=&quot;Safe.View&quot;");
        result.Xaml.Should().Contain("xmlns:evil=&quot;clr-namespace:Safe&quot;");
    }

    private static PackRegistry CreateRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot),
            null);

    private static string Blueprint(string propertiesJson = "{}")
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SafetyView",
              "packs": [{ "id": "safety", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "safety",
              "layout": { "kind": "safety.demo", "properties": PROPERTIES }
            }
            """.Replace("PROPERTIES", propertiesJson, StringComparison.Ordinal);

    private static string WpfUiBlueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SafetyView",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;

    private static string CreateTempProjectWithSafetyPack(
        string rendererTemplate,
        string xmlNamespacesJson = "{}",
        string slotsJson = "{}",
        string propertiesJson = "{}")
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-renderer-safety-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "safety", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"safety","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        var manifest = """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"safety","displayName":"Safety Pack","version":"1.0.0","blocks":["safety.demo"],"recipes":[],"xmlNamespaces":XML_NAMESPACES}
            """.Replace("XML_NAMESPACES", xmlNamespacesJson, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), manifest);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        var block = """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"safety.demo","displayName":"Demo","category":"test","properties":PROPERTIES,"slots":SLOTS,"renderer":{"xamlTemplate":"renderers/xaml/demo.xaml.sbn"},"sourceHints":[]}
            """
            .Replace("PROPERTIES", propertiesJson, StringComparison.Ordinal)
            .Replace("SLOTS", slotsJson, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "demo.block.json"), block);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "demo.xaml.sbn"), rendererTemplate);
        return projectRoot;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
