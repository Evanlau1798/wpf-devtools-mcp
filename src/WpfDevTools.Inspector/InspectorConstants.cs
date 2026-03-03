namespace WpfDevTools.Inspector;

/// <summary>
/// Constants used throughout the Inspector
/// </summary>
public static class InspectorConstants
{
    /// <summary>
    /// Default colors for UI operations
    /// </summary>
    public static class Colors
    {
        public const string Red = "Red";
        public const string Blue = "Blue";
        public const string Green = "Green";
        public const string Yellow = "Yellow";
    }

    /// <summary>
    /// Keyboard event types
    /// </summary>
    public static class KeyboardEvents
    {
        public const string KeyDown = "KeyDown";
        public const string KeyUp = "KeyUp";
    }

    /// <summary>
    /// Data formats for drag and drop
    /// </summary>
    public static class DataFormats
    {
        public const string Text = "Text";
        public const string FileDrop = "FileDrop";
        public const string Html = "Html";
    }

    /// <summary>
    /// Binding update directions
    /// </summary>
    public static class BindingDirections
    {
        public const string Source = "Source";
        public const string Target = "Target";
    }

    /// <summary>
    /// Default values
    /// </summary>
    public static class Defaults
    {
        public const int HighlightDuration = 2000; // milliseconds
        public const int EventTraceDuration = 5000; // milliseconds
        public const int BindingLeakThreshold = 100;
    }
}
