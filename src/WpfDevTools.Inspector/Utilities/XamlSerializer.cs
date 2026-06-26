using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Creates a bounded, side-effect-safe runtime XAML snapshot for WPF elements.
/// </summary>
public class XamlSerializer
{
    internal const int DefaultMaxSerializedXamlCharacters = MessageFraming.MaxMessageSizeBytes / 4;
    internal const int DefaultMaxSerializedXamlUtf8Bytes = MessageFraming.MaxMessageSizeBytes / 2;
    private const int MaxSnapshotDepth = 6;
    private const int MaxSnapshotNodes = 256;

    /// <summary>
    /// Serialize a WPF element to a runtime XAML snapshot string.
    /// </summary>
    /// <param name="element">Element to serialize</param>
    /// <returns>XAML-like snapshot string representation of the element.</returns>
    public string SerializeToXaml(DependencyObject element) => SerializeToXaml(element, maxCharacters: null);

    internal string SerializeToXaml(DependencyObject element, int? maxCharacters)
    {
        if (element == null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (maxCharacters is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters), "Serialized XAML character budget must be positive.");
        }

        try
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true
            };

            using var textWriter = maxCharacters.HasValue
                ? new BoundedStringWriter(maxCharacters.Value)
                : new StringWriter(CultureInfo.InvariantCulture);

            using (var writer = XmlWriter.Create(textWriter, settings))
            {
                var snapshotWriter = new RuntimeXamlSnapshotWriter(writer);
                snapshotWriter.Write(element);
            }

            return textWriter.ToString();
        }
        catch (XamlPayloadTooLargeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"XamlSerializer: Failed to serialize element: {SensitiveLogRedactor.Redact(ex.Message)}");
            throw new XamlSerializationException(ex.GetType().Name, ex);
        }
    }

    private sealed class RuntimeXamlSnapshotWriter
    {
        private readonly XmlWriter _writer;
        private readonly HashSet<DependencyObject> _visited = new(ReferenceIdentityComparer.Instance);
        private int _nodeCount;

        public RuntimeXamlSnapshotWriter(XmlWriter writer)
        {
            _writer = writer;
        }

        public void Write(DependencyObject element)
        {
            WriteElement(element, depth: 0);
        }

        private void WriteElement(DependencyObject element, int depth)
        {
            if (!_visited.Add(element))
            {
                _writer.WriteStartElement(GetElementName(element));
                _writer.WriteAttributeString("RuntimeType", GetRuntimeTypeName(element));
                _writer.WriteAttributeString("ReferenceCycle", "true");
                _writer.WriteEndElement();
                return;
            }

            _nodeCount++;
            _writer.WriteStartElement(GetElementName(element));
            WriteCommonAttributes(element);

            if (depth >= MaxSnapshotDepth)
            {
                _writer.WriteAttributeString("ChildrenTruncated", "true");
                _writer.WriteEndElement();
                return;
            }

            var wroteTruncation = false;
            foreach (var child in GetChildElements(element))
            {
                if (_nodeCount >= MaxSnapshotNodes)
                {
                    wroteTruncation = true;
                    break;
                }

                WriteElement(child, depth + 1);
            }

            if (wroteTruncation)
            {
                _writer.WriteComment($"Snapshot truncated after {MaxSnapshotNodes} nodes.");
            }

            _writer.WriteEndElement();
        }

        private void WriteCommonAttributes(DependencyObject element)
        {
            var type = element.GetType();
            if (!IsFrameworkType(type))
            {
                _writer.WriteAttributeString("RuntimeType", GetRuntimeTypeName(element));
            }

            if (element is FrameworkElement frameworkElement)
            {
                WriteStringAttribute("Name", frameworkElement.Name);
                WriteLengthAttribute("Width", frameworkElement.Width);
                WriteLengthAttribute("Height", frameworkElement.Height);
                WriteThicknessAttribute("Margin", frameworkElement.Margin);
                WriteEnumAttribute("HorizontalAlignment", frameworkElement.HorizontalAlignment, HorizontalAlignment.Stretch);
                WriteEnumAttribute("VerticalAlignment", frameworkElement.VerticalAlignment, VerticalAlignment.Stretch);
            }

            if (element is UIElement uiElement)
            {
                WriteEnumAttribute("Visibility", uiElement.Visibility, Visibility.Visible);
                WriteBooleanAttribute("IsEnabled", uiElement.IsEnabled, defaultValue: true);
            }

            WriteStringAttribute("AutomationId", AutomationProperties.GetAutomationId(element));

            switch (element)
            {
                case TextBlock textBlock:
                    WriteStringAttribute("Text", textBlock.Text);
                    break;
                case TextBox textBox:
                    WriteStringAttribute("Text", textBox.Text);
                    break;
                case HeaderedContentControl headeredContentControl:
                    WriteSimpleValueAttribute("Header", headeredContentControl.Header);
                    WriteSimpleValueAttribute("Content", headeredContentControl.Content);
                    break;
                case ContentControl contentControl:
                    WriteSimpleValueAttribute("Content", contentControl.Content);
                    break;
                case ItemsControl itemsControl:
                    _writer.WriteAttributeString(
                        "ItemsCount",
                        itemsControl.Items.Count.ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }

        private void WriteStringAttribute(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _writer.WriteAttributeString(name, value);
            }
        }

        private void WriteSimpleValueAttribute(string name, object? value)
        {
            if (value is DependencyObject)
            {
                return;
            }

            var text = value switch
            {
                null => null,
                string stringValue => stringValue,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.GetType().Name
            };

            WriteStringAttribute(name, text);
        }

        private void WriteLengthAttribute(string name, double value)
        {
            if (!double.IsNaN(value))
            {
                _writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void WriteThicknessAttribute(string name, Thickness value)
        {
            if (value != default)
            {
                _writer.WriteAttributeString(
                    name,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3}",
                        value.Left,
                        value.Top,
                        value.Right,
                        value.Bottom));
            }
        }

        private void WriteEnumAttribute<T>(string name, T value, T defaultValue)
            where T : struct, Enum
        {
            if (!EqualityComparer<T>.Default.Equals(value, defaultValue))
            {
                _writer.WriteAttributeString(name, value.ToString());
            }
        }

        private void WriteBooleanAttribute(string name, bool value, bool defaultValue)
        {
            if (value != defaultValue)
            {
                _writer.WriteAttributeString(name, value ? "true" : "false");
            }
        }

        private static IEnumerable<DependencyObject> GetChildElements(DependencyObject element)
        {
            var yielded = new HashSet<DependencyObject>(ReferenceIdentityComparer.Instance);

            foreach (var child in GetSemanticChildren(element))
            {
                if (yielded.Add(child))
                {
                    yield return child;
                }
            }

            var visualChildCount = GetVisualChildCount(element);
            for (var i = 0; i < visualChildCount; i++)
            {
                DependencyObject? child = null;
                try
                {
                    child = VisualTreeHelper.GetChild(element, i);
                }
                catch (InvalidOperationException)
                {
                }

                if (child != null && yielded.Add(child))
                {
                    yield return child;
                }
            }
        }

        private static IEnumerable<DependencyObject> GetSemanticChildren(DependencyObject element)
        {
            switch (element)
            {
                case Panel panel:
                    foreach (UIElement child in panel.Children)
                    {
                        yield return child;
                    }

                    break;
                case Decorator { Child: { } child }:
                    yield return child;
                    break;
                case HeaderedContentControl headeredContentControl:
                    if (headeredContentControl.Header is DependencyObject header)
                    {
                        yield return header;
                    }

                    if (headeredContentControl.Content is DependencyObject content)
                    {
                        yield return content;
                    }

                    break;
                case ContentControl { Content: DependencyObject contentElement }:
                    yield return contentElement;
                    break;
            }
        }

        private static int GetVisualChildCount(DependencyObject element)
        {
            try
            {
                return VisualTreeHelper.GetChildrenCount(element);
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        private static string GetElementName(DependencyObject element)
        {
            var name = element.GetType().Name;
            var tickIndex = name.IndexOf('`');
            if (tickIndex >= 0)
            {
                name = name.Substring(0, tickIndex);
            }

            return IsValidXmlName(name) ? name : "Element";
        }

        private static bool IsValidXmlName(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsXmlNameStartChar(name[0]))
            {
                return false;
            }

            for (var i = 1; i < name.Length; i++)
            {
                if (!IsXmlNameChar(name[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsXmlNameStartChar(char value) =>
            value == '_' || char.IsLetter(value);

        private static bool IsXmlNameChar(char value) =>
            IsXmlNameStartChar(value) || char.IsDigit(value) || value is '-' or '.';

        private static bool IsFrameworkType(Type type) =>
            type.Namespace?.StartsWith("System.Windows.", StringComparison.Ordinal) == true;

        private static string GetRuntimeTypeName(DependencyObject element) =>
            element.GetType().FullName ?? element.GetType().Name;
    }

    private sealed class ReferenceIdentityComparer : IEqualityComparer<DependencyObject>
    {
        public static readonly ReferenceIdentityComparer Instance = new();

        public bool Equals(DependencyObject? x, DependencyObject? y) =>
            ReferenceEquals(x, y);

        public int GetHashCode(DependencyObject obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class BoundedStringWriter : StringWriter
    {
        private readonly int _maxCharacters;
        private int _characterCount;

        public BoundedStringWriter(int maxCharacters)
            : base(CultureInfo.InvariantCulture)
        {
            _maxCharacters = maxCharacters;
        }

        public override void Write(char value)
        {
            EnsureCanWrite(1);
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            EnsureCanWrite(count);
            base.Write(buffer, index, count);
        }

        public override void Write(string? value)
        {
            if (value != null)
            {
                EnsureCanWrite(value.Length);
            }

            base.Write(value);
        }

        private void EnsureCanWrite(int count)
        {
            if (count <= 0)
            {
                return;
            }

            var nextCount = _characterCount + count;
            if (nextCount > _maxCharacters)
            {
                throw new XamlPayloadTooLargeException(nextCount, _maxCharacters);
            }

            _characterCount = nextCount;
        }
    }
}

internal sealed class XamlPayloadTooLargeException : Exception
{
    public XamlPayloadTooLargeException(int characterCount, int maxCharacterCount)
        : base($"Serialized XAML exceeded the {maxCharacterCount} character budget.")
    {
        CharacterCount = characterCount;
        MaxCharacterCount = maxCharacterCount;
    }

    public int CharacterCount { get; }

    public int MaxCharacterCount { get; }
}

internal sealed class XamlSerializationException : Exception
{
    public XamlSerializationException(string exceptionType, Exception innerException)
        : base("Failed to serialize the WPF element to XAML.", innerException)
    {
        ExceptionType = exceptionType;
    }

    public string ExceptionType { get; }
}
