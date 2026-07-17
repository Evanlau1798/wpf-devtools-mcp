using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Layout information
/// </summary>
public sealed partial class LayoutAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Create a new LayoutAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public LayoutAnalyzer(ElementFinder elementFinder)
        : base(elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get layout information for an element
    /// </summary>
    public object GetLayoutInfo(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target from get_visual_tree or find_elements before inspecting layout.");
            }

            var notRenderedReason = GetNotRenderedReason(fe);

            return new
            {
                success = true,
                actualWidth = NormalizeDouble(fe.ActualWidth),
                actualHeight = NormalizeDouble(fe.ActualHeight),
                width = NormalizeDouble(fe.Width),
                height = NormalizeDouble(fe.Height),
                minWidth = NormalizeDouble(fe.MinWidth),
                minHeight = NormalizeDouble(fe.MinHeight),
                maxWidth = NormalizeDouble(fe.MaxWidth),
                maxHeight = NormalizeDouble(fe.MaxHeight),
                desiredSize = new
                {
                    width = NormalizeDouble(fe.DesiredSize.Width),
                    height = NormalizeDouble(fe.DesiredSize.Height)
                },
                desiredWidth = NormalizeDouble(fe.DesiredSize.Width),
                desiredHeight = NormalizeDouble(fe.DesiredSize.Height),
                renderSize = new
                {
                    width = NormalizeDouble(fe.RenderSize.Width),
                    height = NormalizeDouble(fe.RenderSize.Height)
                },
                margin = new
                {
                    left = NormalizeDouble(fe.Margin.Left),
                    top = NormalizeDouble(fe.Margin.Top),
                    right = NormalizeDouble(fe.Margin.Right),
                    bottom = NormalizeDouble(fe.Margin.Bottom)
                },
                padding = GetPaddingInfo(fe),
                horizontalAlignment = fe.HorizontalAlignment.ToString(),
                verticalAlignment = fe.VerticalAlignment.ToString(),
                layoutState = notRenderedReason == null ? "Rendered" : "NotRendered",
                notRenderedReason,
                positionInParent = GetPositionInfo(fe, VisualTreeHelper.GetParent(fe) as Visual),
                positionInWindow = GetPositionInfo(fe, Window.GetWindow(fe))
            };
        });
    }

    private static string? GetNotRenderedReason(FrameworkElement element)
    {
        return element.ActualWidth <= 0 || element.ActualHeight <= 0
            ? SceneSummaryElementHelpers.GetLayoutSizeBlockerReason(element)
            : null;
    }

    private static double? NormalizeDouble(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) ? value : null;
    }

    private static object GetPaddingInfo(FrameworkElement element)
    {
        return element switch
        {
            Control control => CreateThicknessInfo(control.Padding),
            Border border => CreateThicknessInfo(border.Padding),
            _ => CreateThicknessInfo(null)
        };
    }

    private static object CreateThicknessInfo(Thickness? thickness)
    {
        return new
        {
            left = NormalizeDouble(thickness?.Left ?? double.NaN),
            top = NormalizeDouble(thickness?.Top ?? double.NaN),
            right = NormalizeDouble(thickness?.Right ?? double.NaN),
            bottom = NormalizeDouble(thickness?.Bottom ?? double.NaN)
        };
    }

    private static object GetPositionInfo(Visual target, Visual? relativeTo)
    {
        if (relativeTo == null)
        {
            return new { x = (double?)null, y = (double?)null };
        }

        try
        {
            var point = target.TransformToAncestor(relativeTo).Transform(new Point(0, 0));
            return new { x = NormalizeDouble(point.X), y = NormalizeDouble(point.Y) };
        }
        catch (InvalidOperationException)
        {
            return new { x = (double?)null, y = (double?)null };
        }
    }

    // Task 4 scope boundary: this detects transformed bounds that are entirely outside the
    // visible root viewport. It is not a complete semantic model for every transform family
    // or occlusion case.
    private static bool IsRenderTransformOffscreen(FrameworkElement element)
    {
        if (element.RenderTransform is not { } renderTransform || renderTransform.Value.IsIdentity)
        {
            return false;
        }

        if (!TryGetVisibleViewportRoot(element, out var viewportRoot))
        {
            return false;
        }

        var elementBounds = GetContentBounds(element);
        if (elementBounds.IsEmpty)
        {
            return false;
        }

        var viewportBounds = new Rect(new Point(0, 0), viewportRoot.RenderSize);
        if (viewportBounds.IsEmpty)
        {
            return false;
        }

        try
        {
            var transformedBounds = element.TransformToAncestor(viewportRoot).TransformBounds(elementBounds);
            return !transformedBounds.IntersectsWith(viewportBounds);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetVisibleViewportRoot(FrameworkElement element, out UIElement viewportRoot)
    {
        var window = Window.GetWindow(element);
        if (window is null)
        {
            viewportRoot = null!;
            return false;
        }

        DependencyObject? current = element;
        while (current is not null && !ReferenceEquals(current, window))
        {
            if (current is ContentPresenter presenter
                && ReferenceEquals(presenter.Content, window.Content))
            {
                viewportRoot = presenter;
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        viewportRoot = window;
        return true;
    }

    /// <summary>
    /// Invalidate layout for an element
    /// </summary>
    public object InvalidateLayout(string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not UIElement uiElement)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a UIElement",
                    "Choose a UIElement target before calling invalidate_layout.");
            }

            try
            {
                uiElement.InvalidateMeasure();
                uiElement.InvalidateArrange();
                uiElement.UpdateLayout();

                return new { success = true, message = "Layout invalidated and updated successfully" };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "invalidate layout",
                    ex,
                    "Ensure the target UIElement is still loaded before retrying invalidate_layout.");
            }
        });
    }
}
