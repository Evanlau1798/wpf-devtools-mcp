using System.ComponentModel;
using System.Text.Json;
using FluentAssertions;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class MvvmAnalyzerSchemaContractTests
{
    [StaFact]
    public void GetViewModel_ShouldExposeDocumentedTypeNameAndNotifyFlag()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var button = new Button { DataContext = new ObservableViewModel() };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.GetViewModel(elementId);
        var doc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("typeName").GetString().Should().Be(nameof(ObservableViewModel));
        doc.GetProperty("implementsINotifyPropertyChanged").GetBoolean().Should().BeTrue();
    }

    private sealed class ObservableViewModel : INotifyPropertyChanged
    {
#pragma warning disable CS0067
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

        public string Name { get; set; } = "Alice";
    }
}
