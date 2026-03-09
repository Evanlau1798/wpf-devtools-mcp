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
        /// <summary>Red color name</summary>
        public const string Red = "Red";
        /// <summary>Blue color name</summary>
        public const string Blue = "Blue";
        /// <summary>Green color name</summary>
        public const string Green = "Green";
        /// <summary>Yellow color name</summary>
        public const string Yellow = "Yellow";
    }

    /// <summary>
    /// Keyboard event types
    /// </summary>
    public static class KeyboardEvents
    {
        /// <summary>KeyDown event type</summary>
        public const string KeyDown = "KeyDown";
        /// <summary>KeyUp event type</summary>
        public const string KeyUp = "KeyUp";
    }

    /// <summary>
    /// Data formats for drag and drop
    /// </summary>
    public static class DataFormats
    {
        /// <summary>Plain text format</summary>
        public const string Text = "Text";
        /// <summary>File drop format</summary>
        public const string FileDrop = "FileDrop";
        /// <summary>HTML format</summary>
        public const string Html = "Html";
    }

    /// <summary>
    /// Binding update directions
    /// </summary>
    public static class BindingDirections
    {
        /// <summary>Update binding source from target</summary>
        public const string Source = "Source";
        /// <summary>Update binding target from source</summary>
        public const string Target = "Target";
    }

    /// <summary>
    /// Default values
    /// </summary>
    public static class Defaults
    {
        /// <summary>Default highlight duration in milliseconds</summary>
        public const int HighlightDuration = 2000; // milliseconds
        /// <summary>Default event trace duration in milliseconds</summary>
        public const int EventTraceDuration = 5000; // milliseconds
        /// <summary>Minimum event trace duration for start mode in milliseconds (AI agent round-trip)</summary>
        public const int StartModeMinDuration = 30000; // 30 seconds
        /// <summary>Threshold for detecting binding leaks</summary>
        public const int BindingLeakThreshold = 100;
    }
}
