using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Xml;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Serializes WPF elements to XAML
/// </summary>
public class XamlSerializer
{
    public string SerializeToXaml(DependencyObject element)
    {
        if (element == null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        try
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                XamlWriter.Save(element, writer);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"<!-- Failed to serialize: {ex.Message} -->";
        }
    }
}
