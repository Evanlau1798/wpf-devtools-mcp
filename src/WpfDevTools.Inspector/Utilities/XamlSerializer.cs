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
    /// <summary>
    /// Serialize a WPF element to XAML string
    /// </summary>
    /// <param name="element">Element to serialize</param>
    /// <returns>XAML string representation of the element</returns>
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
        catch (Exception)
        {
            return "<!-- Failed to serialize element to XAML -->";
        }
    }
}
