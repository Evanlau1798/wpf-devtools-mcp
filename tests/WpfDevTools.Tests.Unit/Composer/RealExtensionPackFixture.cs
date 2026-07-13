using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

internal static class RealExtensionPackFixture
{
    public const string MaterialDesignBlueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"MaterialDesignAcceptance",
          "packs":[
            {"id":"core","version":"0.1.0","required":true,"role":"layout-pack"},
            {"id":"materialdesign","version":"5.3.2","required":true,"role":"primary"}
          ],
          "primaryPack":"materialdesign",
          "layout":{
            "kind":"materialdesign.window",
            "properties":{"title":"Material acceptance"},
            "slots":{"content":[{
              "kind":"core.stack",
              "properties":{"margin":"24","spacing":"0,0,0,12"},
              "slots":{"children":[{
                "kind":"materialdesign.card",
                "slots":{"content":[{
                  "kind":"core.stack",
                  "slots":{"children":[
                    {"kind":"core.text","properties":{"text":"Material workspace","fontSize":24,"fontWeight":"SemiBold"}},
                    {"kind":"materialdesign.action","properties":{"execute":"{Binding OpenWorkspaceCommand}","payload":"material-532","caption":"Open workspace"}}
                  ]}
                }]}
              }]}
            }]}
          }
        }
        """;

    public const string MahAppsBlueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"MahAppsWindowProof",
          "packs":[{"id":"mahapps","version":"2.4.11","required":true,"role":"primary"}],
          "primaryPack":"mahapps",
          "layout":{"kind":"mahapps.window","properties":{"title":"Operations"}}
        }
        """;

    public static string CreateMaterialDesignProject()
        => CreateProject("materialdesign", "5.3.2", "MaterialDesignAcceptance");

    public static string CreateMahAppsProject()
        => CreateProject("mahapps", "2.4.11", "MahAppsWindowProof");

    private static string CreateProject(string packId, string version, string rootNamespace)
    {
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-real-pack-" + Guid.NewGuid().ToString("N"));
        var source = TestRepositoryPaths.GetRepoFilePath(
            Path.Combine("tests", "WpfDevTools.Tests.Unit", "TestData", "ComposerPacks", packId, version));
        var destination = Path.Combine(root, ".wpfdevtools", "packs", packId, version);
        CopyDirectory(source, destination);
        File.WriteAllText(
            Path.Combine(root, rootNamespace + ".csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <RootNamespace>{{rootNamespace}}</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        return root;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
