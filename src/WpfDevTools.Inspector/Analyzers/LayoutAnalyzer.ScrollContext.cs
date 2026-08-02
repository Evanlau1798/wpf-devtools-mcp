using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class LayoutAnalyzer
{
    private object? CreateNearestScrollContainerInfo(
        UIElement element,
        UIElement? primaryClippingAncestor,
        (double left, double top, double right, double bottom) overflow)
    {
        ScrollViewer? nearestScrollViewer = null;
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current is Visual)
        {
            if (current is ScrollViewer scrollViewer)
            {
                nearestScrollViewer ??= scrollViewer;
                if (primaryClippingAncestor is ScrollContentPresenter presenter
                    && ReferenceEquals(presenter.TemplatedParent, scrollViewer)
                    && !IsElementFrameClipped(scrollViewer))
                {
                    return CreateScrollContainerInfo(scrollViewer, overflow, isTargetClippedByViewport: true);
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return nearestScrollViewer is null
            ? null
            : CreateScrollContainerInfo(nearestScrollViewer, overflow, isTargetClippedByViewport: false);
    }

    private object CreateScrollContainerInfo(
        ScrollViewer scrollViewer,
        (double left, double top, double right, double bottom) overflow,
        bool isTargetClippedByViewport)
    {
        var elementName = string.IsNullOrWhiteSpace(scrollViewer.Name) ? null : scrollViewer.Name;
        var horizontalOverflow = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth + 0.5;
        var verticalOverflow = scrollViewer.ExtentHeight > scrollViewer.ViewportHeight + 0.5;
        var horizontalClip = overflow.left > 0 || overflow.right > 0;
        var verticalClip = overflow.top > 0 || overflow.bottom > 0;
        var canBringTargetIntoView = isTargetClippedByViewport
                                     && (!horizontalClip || (horizontalOverflow
                                         && scrollViewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled))
                                     && (!verticalClip || (verticalOverflow
                                         && scrollViewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled));
        return new
        {
            elementId = _elementFinder.GenerateElementId(scrollViewer),
            elementType = nameof(ScrollViewer),
            elementName,
            extentWidth = NormalizeDouble(scrollViewer.ExtentWidth),
            extentHeight = NormalizeDouble(scrollViewer.ExtentHeight),
            viewportWidth = NormalizeDouble(scrollViewer.ViewportWidth),
            viewportHeight = NormalizeDouble(scrollViewer.ViewportHeight),
            horizontalOffset = NormalizeDouble(scrollViewer.HorizontalOffset),
            verticalOffset = NormalizeDouble(scrollViewer.VerticalOffset),
            horizontalOverflow,
            verticalOverflow,
            horizontalScrollBarVisibility = scrollViewer.HorizontalScrollBarVisibility.ToString(),
            verticalScrollBarVisibility = scrollViewer.VerticalScrollBarVisibility.ToString(),
            computedHorizontalScrollBarVisibility = scrollViewer.ComputedHorizontalScrollBarVisibility.ToString(),
            computedVerticalScrollBarVisibility = scrollViewer.ComputedVerticalScrollBarVisibility.ToString(),
            hasVisibleScrollBarChrome = scrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                                        || scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible,
            isTargetClippedByViewport,
            canBringTargetIntoView
        };
    }

    private static bool IsElementFrameClipped(UIElement element)
    {
        var frame = new Rect(new Point(0, 0), element.RenderSize);
        if (frame.IsEmpty)
        {
            return false;
        }

        if (GetEffectiveClippingGeometry(element) is { } selfClip
            && HasOverflow(ComputeOverflow(frame, selfClip.Bounds)))
        {
            return true;
        }

        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current is Visual ancestorVisual)
        {
            if (current is UIElement ancestor
                && GetEffectiveClippingGeometry(ancestor) is { } ancestorClip)
            {
                try
                {
                    var transformed = element.TransformToAncestor(ancestorVisual).TransformBounds(frame);
                    if (HasOverflow(ComputeOverflow(transformed, ancestorClip.Bounds)))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        if (element is not FrameworkElement frameworkElement
            || !TryGetVisibleViewportRoot(frameworkElement, out var viewportRoot))
        {
            return false;
        }

        try
        {
            var transformed = ReferenceEquals(element, viewportRoot)
                ? frame
                : element.TransformToAncestor(viewportRoot).TransformBounds(frame);
            return HasOverflow(ComputeOverflow(
                transformed,
                new Rect(new Point(0, 0), viewportRoot.RenderSize)));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
