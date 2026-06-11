using System.ComponentModel;
using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class MvvmAnalyzerContractTests
{
    [StaFact]
    public void GetViewModel_ShouldExposePropertyWriteabilityMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var grid = new Grid { DataContext = new SampleViewModel() };
        var elementId = finder.GenerateElementId(grid);

        var result = JsonSerializer.SerializeToElement(analyzer.GetViewModel(elementId));
        var properties = result.GetProperty("properties").EnumerateArray().ToList();

        properties.Single(p => p.GetProperty("name").GetString() == nameof(SampleViewModel.Name))
            .GetProperty("canWrite").GetBoolean().Should().BeTrue();
        properties.Single(p => p.GetProperty("name").GetString() == nameof(SampleViewModel.CanSave))
            .GetProperty("canWrite").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void ModifyViewModel_ShouldExposePropertyTypeAndWriteabilityMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var grid = new Grid { DataContext = new SampleViewModel { Name = "Alice" } };
        var elementId = finder.GenerateElementId(grid);

        var result = JsonSerializer.SerializeToElement(analyzer.ModifyViewModel(elementId, nameof(SampleViewModel.Name), "Bob"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyType").GetString().Should().Be("String");
        result.GetProperty("canWrite").GetBoolean().Should().BeTrue();
        result.GetProperty("requestedValueType").GetString().Should().Be("String");
        result.GetProperty("convertedValueType").GetString().Should().Be("String");
    }

    private sealed class SampleViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSave)));
            }
        }

        public bool CanSave => !string.IsNullOrWhiteSpace(Name);
    }
}
