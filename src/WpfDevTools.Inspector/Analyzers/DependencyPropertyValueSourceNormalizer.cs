using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

internal static class DependencyPropertyValueSourceNormalizer
{
    internal static string Normalize(BaseValueSource rawBaseValueSource, bool hadLocalValue, bool isAnimated)
    {
        if (isAnimated)
        {
            return "Animation";
        }

        if (hadLocalValue || rawBaseValueSource == BaseValueSource.Local)
        {
            return "LocalValue";
        }

        return rawBaseValueSource switch
        {
            BaseValueSource.Inherited => "Inherited",
            BaseValueSource.Style or BaseValueSource.DefaultStyle or BaseValueSource.ImplicitStyleReference => "Style",
            BaseValueSource.ParentTemplate => "TemplateBinding",
            BaseValueSource.StyleTrigger or BaseValueSource.TemplateTrigger or BaseValueSource.ParentTemplateTrigger or BaseValueSource.DefaultStyleTrigger => "Trigger",
            _ => "Default"
        };
    }
}
