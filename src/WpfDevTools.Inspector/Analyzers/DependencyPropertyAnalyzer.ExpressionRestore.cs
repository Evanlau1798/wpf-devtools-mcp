using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class DependencyPropertyAnalyzer
{
    private static readonly ConcurrentDictionary<string, CapturedExpressionState> _capturedExpressions = new();
    private static readonly ConcurrentDictionary<string, string> _latestRollbackTokens = new();

    internal object CaptureExpressionRestore(string propertyName, string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (!TryResolveDependencyProperty(elementId, propertyName, out var depObj, out var dp, out var error))
            {
                return error!;
            }

            if (TryCaptureBindingExpression(depObj!, dp!, out var restoreToken, out var expressionKind))
            {
                return new
                {
                    success = true,
                    canRestore = true,
                    restoreToken,
                    expressionKind
                };
            }

            return new
            {
                success = true,
                canRestore = false,
                reason = $"Property '{propertyName}' uses an expression that is not restorable through BindingOperations.SetBinding in the current session."
            };
        });
    }

    internal object RestoreExpression(string propertyName, string restoreToken, string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (!TryResolveDependencyProperty(elementId, propertyName, out var depObj, out var dp, out var error))
            {
                return error!;
            }

            if (!TryRestoreCapturedExpression(depObj!, dp!, restoreToken, consumeRollbackToken: false, out var expressionKind, out var currentValue, out var restoreError))
            {
                return ToolErrorFactory.OperationFailed(
                    "restore expression-backed property",
                    new InvalidOperationException(restoreError),
                    "Retry capture_state_snapshot before mutating the property, or treat non-Binding expressions as unsupported.");
            }

            return new
            {
                success = true,
                propertyName,
                restoredExpression = true,
                expressionKind,
                currentValue = FormatResponseValue(currentValue)
            };
        });
    }

    private bool TryResolveDependencyProperty(
        string? elementId,
        string propertyName,
        out DependencyObject? depObj,
        out DependencyProperty? dp,
        out object? error)
    {
        depObj = null;
        dp = null;
        error = null;

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            error = ToolErrorFactory.ElementNotFound(elementId);
            return false;
        }

        if (element is not DependencyObject resolvedDepObj)
        {
            error = ToolErrorFactory.InvalidArgument(
                "Element is not a DependencyObject",
                "Target a WPF DependencyObject element before restoring an expression-backed DependencyProperty.");
            return false;
        }

        var resolvedDp = FindDependencyProperty(resolvedDepObj, propertyName);
        if (resolvedDp == null)
        {
            error = ToolErrorFactory.PropertyNotFound(propertyName, resolvedDepObj.GetType().Name);
            return false;
        }

        depObj = resolvedDepObj;
        dp = resolvedDp;
        return true;
    }

    private static bool TryCaptureBindingExpression(
        DependencyObject depObj,
        DependencyProperty dp,
        out string restoreToken,
        out string expressionKind)
    {
        restoreToken = string.Empty;
        expressionKind = string.Empty;

        var bindingBase = BindingOperations.GetBindingBase(depObj, dp);
        if (bindingBase == null)
        {
            return false;
        }

        var clonedBinding = CloneBindingBase(bindingBase);
        if (clonedBinding == null)
        {
            return false;
        }

        restoreToken = Guid.NewGuid().ToString("N");
        expressionKind = bindingBase switch
        {
            Binding => "Binding",
            MultiBinding => "MultiBinding",
            PriorityBinding => "PriorityBinding",
            _ => bindingBase.GetType().Name
        };

        _capturedExpressions[restoreToken] = new CapturedExpressionState(
            new WeakReference<DependencyObject>(depObj),
            dp,
            clonedBinding,
            expressionKind);
        _latestRollbackTokens[BuildDependencyPropertyKey(depObj, dp)] = restoreToken;
        CleanupCapturedExpressionsIfNeeded();
        return true;
    }

    private static bool TryRestoreCapturedExpression(
        DependencyObject depObj,
        DependencyProperty dp,
        string restoreToken,
        bool consumeRollbackToken,
        out string? expressionKind,
        out object? currentValue,
        out string? restoreError)
    {
        expressionKind = null;
        currentValue = null;
        restoreError = null;

        if (!_capturedExpressions.TryGetValue(restoreToken, out var capturedState))
        {
            restoreError = $"Expression restore token '{restoreToken}' is no longer available.";
            return false;
        }

        if (!capturedState.ElementReference.TryGetTarget(out var capturedElement) ||
            !ReferenceEquals(capturedElement, depObj) ||
            capturedState.Property != dp)
        {
            restoreError = $"Expression restore token '{restoreToken}' no longer matches the current element/property instance.";
            return false;
        }

        try
        {
            depObj.ClearValue(dp);

            if (CreatePreviewRestoreBinding(capturedState.Binding) is { } previewBinding)
            {
                BindingOperations.SetBinding(depObj, dp, previewBinding);
                BindingOperations.GetBindingExpressionBase(depObj, dp)?.UpdateTarget();
            }

            if (CloneBindingBase(capturedState.Binding) is not { } restoreBinding)
            {
                restoreError = $"Expression restore token '{restoreToken}' could not clone the captured binding for replay.";
                return false;
            }

            BindingOperations.SetBinding(depObj, dp, restoreBinding);
            BindingOperations.GetBindingExpressionBase(depObj, dp)?.UpdateTarget();
            depObj.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            expressionKind = capturedState.ExpressionKind;
            currentValue = depObj.GetValue(dp);

            if (consumeRollbackToken)
            {
                _latestRollbackTokens.TryRemove(BuildDependencyPropertyKey(depObj, dp), out _);
            }

            return true;
        }
        catch (Exception ex)
        {
            restoreError = ex.Message;
            return false;
        }
    }

    private static bool TryRestoreLatestCapturedExpression(
        DependencyObject depObj,
        DependencyProperty dp,
        out string? expressionKind,
        out object? currentValue,
        out string? restoreError)
    {
        expressionKind = null;
        currentValue = null;
        restoreError = null;

        var key = BuildDependencyPropertyKey(depObj, dp);
        if (!_latestRollbackTokens.TryGetValue(key, out var restoreToken))
        {
            return false;
        }

        return TryRestoreCapturedExpression(depObj, dp, restoreToken, consumeRollbackToken: true, out expressionKind, out currentValue, out restoreError);
    }

    private static string BuildDependencyPropertyKey(DependencyObject depObj, DependencyProperty dp) =>
        $"{RuntimeHelpers.GetHashCode(depObj)}::{dp.OwnerType.FullName}::{dp.Name}";

    private static void CleanupCapturedExpressionsIfNeeded()
    {
        if (_capturedExpressions.Count <= 2048)
        {
            return;
        }

        foreach (var entry in _capturedExpressions)
        {
            if (!entry.Value.ElementReference.TryGetTarget(out _))
            {
                _capturedExpressions.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed record CapturedExpressionState(
        WeakReference<DependencyObject> ElementReference,
        DependencyProperty Property,
        BindingBase Binding,
        string ExpressionKind);

    private static BindingBase? CreatePreviewRestoreBinding(BindingBase bindingBase) => bindingBase switch
    {
        Binding binding => CreatePreviewBinding(binding),
        MultiBinding multiBinding => CreatePreviewMultiBinding(multiBinding),
        _ => null
    };

    private static Binding CreatePreviewBinding(Binding source)
    {
        var preview = CloneBinding(source);
        preview.Mode = BindingMode.OneWay;
        preview.UpdateSourceTrigger = UpdateSourceTrigger.Explicit;
        return preview;
    }

    private static MultiBinding CreatePreviewMultiBinding(MultiBinding source)
    {
        var preview = CloneMultiBinding(source);
        preview.Mode = BindingMode.OneWay;
        preview.UpdateSourceTrigger = UpdateSourceTrigger.Explicit;
        return preview;
    }
}
