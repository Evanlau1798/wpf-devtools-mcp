using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class MvvmAnalyzerSecurityPatternTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("UserPassword")]
    [InlineData("ApiToken")]
    [InlineData("SecretKey")]
    [InlineData("ConnectionString")]
    [InlineData("SessionCookie")]
    [InlineData("AuthHeader")]
    public void SensitivePropertyPattern_ShouldMatchCompoundSensitiveNames(string propertyName)
    {
        var pattern = typeof(MvvmAnalyzer)
            .GetField("SensitivePropertyPattern", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)
            .Should().BeOfType<Regex>()
            .Subject;

        pattern.IsMatch(propertyName).Should().BeTrue();
    }
}
