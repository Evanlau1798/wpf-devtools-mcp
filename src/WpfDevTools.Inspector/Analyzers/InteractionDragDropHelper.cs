using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfDevTools.Inspector.Analyzers;

internal static class InteractionDragDropHelper
{
    private static bool? _reflectionSupported;
    private static readonly object ReflectionLock = new object();

    internal static bool IsReflectionSupported()
    {
        lock (ReflectionLock)
        {
            if (_reflectionSupported.HasValue)
            {
                return _reflectionSupported.Value;
            }

            var constructor = typeof(DragEventArgs).GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[]
                {
                    typeof(IDataObject),
                    typeof(DragDropKeyStates),
                    typeof(DragDropEffects),
                    typeof(DependencyObject),
                    typeof(Point)
                },
                null);

            _reflectionSupported = constructor != null;
            return _reflectionSupported.Value;
        }
    }

    internal static void MarkReflectionUnsupported()
    {
        lock (ReflectionLock)
        {
            _reflectionSupported = false;
        }
    }

    internal static DataObject CreateDataObject(DependencyObject sourceElement, string dataFormat)
    {
        var data = new DataObject();
        var payload = ResolvePayload(sourceElement, dataFormat);

        if (IsTextFormat(dataFormat))
        {
            var text = payload?.ToString() ?? string.Empty;
            data.SetData(DataFormats.Text, text);
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData(typeof(string), text);
            return data;
        }

        data.SetData(dataFormat, payload);
        return data;
    }

    internal static void NormalizeTextDropResult(
        DependencyObject sourceElement,
        DependencyObject targetElement,
        string dataFormat,
        string? originalTargetText)
    {
        if (targetElement is not TextBox targetTextBox || !IsTextFormat(dataFormat))
        {
            return;
        }

        var payloadText = ResolvePayload(sourceElement, dataFormat).ToString() ?? string.Empty;
        var currentText = targetTextBox.Text;
        var originalText = originalTargetText ?? string.Empty;

        if (currentText == originalText
            || currentText == payloadText + originalText
            || currentText == originalText + payloadText)
        {
            targetTextBox.Text = payloadText;
        }
    }

    private static bool IsTextFormat(string dataFormat)
    {
        return string.Equals(dataFormat, DataFormats.Text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataFormat, "Text", StringComparison.OrdinalIgnoreCase);
    }

    private static object ResolvePayload(DependencyObject sourceElement, string dataFormat)
    {
        if (!IsTextFormat(dataFormat))
        {
            return sourceElement;
        }

        return sourceElement switch
        {
            TextBox textBox => textBox.Text,
            TextBlock textBlock => textBlock.Text,
            ContentControl contentControl => contentControl.Content?.ToString() ?? string.Empty,
            _ => sourceElement.ToString() ?? string.Empty
        };
    }
}
