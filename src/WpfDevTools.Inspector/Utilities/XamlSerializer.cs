using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Xml;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Serializes WPF elements to XAML
/// </summary>
public class XamlSerializer
{
    internal const int DefaultMaxSerializedXamlCharacters = MessageFraming.MaxMessageSizeBytes / 4;
    internal const int DefaultMaxSerializedXamlUtf8Bytes = MessageFraming.MaxMessageSizeBytes / 2;

    /// <summary>
    /// Serialize a WPF element to XAML string
    /// </summary>
    /// <param name="element">Element to serialize</param>
    /// <returns>XAML string representation of the element</returns>
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
                XamlWriter.Save(element, writer);
            }

            return textWriter.ToString();
        }
        catch (XamlPayloadTooLargeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"XamlSerializer: Failed to serialize element: {ex.Message}");
            return $"<!-- Failed to serialize element to XAML: {ex.GetType().Name} -->";
        }
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
