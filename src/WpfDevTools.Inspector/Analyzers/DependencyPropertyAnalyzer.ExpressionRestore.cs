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
    private static readonly ConcurrentDictionary<string, string> _latestRollbackTokensByRequestKey = new();

    internal object CaptureExpressionRestore(string propertyName, string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (!TryResolveDependencyProperty(elementId, propertyName, out var depObj, out var dp, out var error))
            {
                return error!;
            }

            if (TryCaptureBindingExpression(depObj!, dp!, elementId, propertyName, out var restoreToken, out var expressionKind))
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

    internal object RestoreExpression(
        string propertyName,
        string restoreToken,
        string? elementId = null,
        object? targetValue = null,
        bool hasTargetValue = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (!TryResolveDependencyProperty(elementId, propertyName, out var depObj, out var dp, out var error))
            {
                return error!;
            }

            if (!TryRestoreCapturedExpression(
                    depObj!,
                    dp!,
                    restoreToken,
                    consumeRollbackToken: false,
                    targetValue,
                    hasTargetValue,
                    out var expressionKind,
                    out var currentValue,
                    out var restoreError))
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

        var element = ResolveElement(elementId);

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
        string? requestElementId,
        string requestPropertyName,
        out string restoreToken,
        out string expressionKind)
    {
        restoreToken = string.Empty;
        expressionKind = string.Empty;

        var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
        var bindingBase = ResolveBindingBaseForCapture(
            BindingOperations.GetBindingBase(depObj, dp),
            BindingOperations.GetBindingExpressionBase(depObj, dp));
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
            expressionKind,
            FormatResponseValue(depObj.GetValue(dp)),
            valueSource.BaseValueSource);
        RememberLatestRollbackToken(depObj, dp, requestElementId, requestPropertyName, restoreToken);
        CleanupCapturedExpressionsIfNeeded();
        return true;
    }

    internal static BindingBase? ResolveBindingBaseForCapture(
        BindingBase? bindingBase,
        BindingExpressionBase? bindingExpressionBase)
    {
        if (bindingBase != null)
        {
            return bindingBase;
        }

        return bindingExpressionBase?.ParentBindingBase;
    }

    private static bool TryRestoreCapturedExpression(
        DependencyObject depObj,
        DependencyProperty dp,
        string restoreToken,
        bool consumeRollbackToken,
        object? targetValue,
        bool hasTargetValue,
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
            if (capturedState.BaseValueSource == BaseValueSource.ParentTemplate)
            {
                return TryRestoreParentTemplateExpression(
                    depObj,
                    dp,
                    capturedState,
                    targetValue,
                    hasTargetValue,
                    out expressionKind,
                    out currentValue,
                    out restoreError);
            }

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
            var bindingExpression = BindingOperations.GetBindingExpressionBase(depObj, dp);
            bindingExpression?.UpdateTarget();
            TryRestoreBindingSourceValue(depObj, dp, bindingExpression, capturedState, targetValue, hasTargetValue);
            bindingExpression?.UpdateTarget();
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
        string? requestElementId,
        string requestPropertyName,
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
            var requestKey = BuildRequestRollbackKey(requestElementId, requestPropertyName);
            if (!_latestRollbackTokensByRequestKey.TryGetValue(requestKey, out restoreToken))
            {
                return false;
            }
        }

        var restored = TryRestoreCapturedExpression(
            depObj,
            dp,
            restoreToken,
            consumeRollbackToken: true,
            targetValue: null,
            hasTargetValue: false,
            out expressionKind,
            out currentValue,
            out restoreError);
        if (restored)
        {
            ForgetLatestRollbackToken(depObj, dp, requestElementId, requestPropertyName, restoreToken);
        }

        return restored;
    }

    private static string BuildDependencyPropertyKey(DependencyObject depObj, DependencyProperty dp) =>
        $"{RuntimeHelpers.GetHashCode(depObj)}::{dp.OwnerType.FullName}::{dp.Name}";

    private static string BuildRequestRollbackKey(string? elementId, string propertyName) =>
        $"{elementId ?? "<root>"}::{GetRollbackPropertyName(propertyName)}";

    private static void RememberLatestRollbackToken(
        DependencyObject depObj,
        DependencyProperty dp,
        string? requestElementId,
        string requestPropertyName,
        string restoreToken)
    {
        _latestRollbackTokens[BuildDependencyPropertyKey(depObj, dp)] = restoreToken;
        _latestRollbackTokensByRequestKey[BuildRequestRollbackKey(requestElementId, requestPropertyName)] = restoreToken;
    }

    private static void ForgetLatestRollbackToken(
        DependencyObject depObj,
        DependencyProperty dp,
        string? requestElementId,
        string requestPropertyName,
        string restoreToken)
    {
        var objectKey = BuildDependencyPropertyKey(depObj, dp);
        if (_latestRollbackTokens.TryGetValue(objectKey, out var latestObjectToken) &&
            string.Equals(latestObjectToken, restoreToken, StringComparison.Ordinal))
        {
            _latestRollbackTokens.TryRemove(objectKey, out _);
        }

        var requestKey = BuildRequestRollbackKey(requestElementId, requestPropertyName);
        if (_latestRollbackTokensByRequestKey.TryGetValue(requestKey, out var latestRequestToken) &&
            string.Equals(latestRequestToken, restoreToken, StringComparison.Ordinal))
        {
            _latestRollbackTokensByRequestKey.TryRemove(requestKey, out _);
        }
    }

    private static string GetRollbackPropertyName(string propertyName)
    {
        var separatorIndex = propertyName.LastIndexOf('.');
        return separatorIndex >= 0
            ? propertyName.Substring(separatorIndex + 1)
            : propertyName;
    }

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
        string ExpressionKind,
        string? CapturedTargetValue,
        BaseValueSource BaseValueSource);

    private static bool TryRestoreParentTemplateExpression(
        DependencyObject depObj, DependencyProperty dp, CapturedExpressionState capturedState,
        object? targetValue, bool hasTargetValue,
        out string? expressionKind, out object? currentValue, out string? restoreError)
    {
        expressionKind = null;
        currentValue = null;
        restoreError = null;

        try
        {
            depObj.ClearValue(dp);
            TryRestoreTemplatedParentSourceValue(depObj, capturedState.Binding, targetValue, hasTargetValue);
            depObj.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
            if (!valueSource.IsExpression || valueSource.BaseValueSource != BaseValueSource.ParentTemplate)
            {
                restoreError = "The original templated-parent expression did not reappear after clearing the local value.";
                return false;
            }

            expressionKind = capturedState.ExpressionKind;
            currentValue = depObj.GetValue(dp);
            return true;
        }
        catch (Exception ex)
        {
            restoreError = ex.Message;
            return false;
        }
    }

    private static void TryRestoreTemplatedParentSourceValue(
        DependencyObject depObj, BindingBase bindingBase, object? targetValue, bool hasTargetValue)
    {
        if (!hasTargetValue ||
            bindingBase is not Binding binding ||
            binding.RelativeSource?.Mode != RelativeSourceMode.TemplatedParent ||
            binding.Path?.Path is not { Length: > 0 } sourcePropertyName ||
            sourcePropertyName.Contains('.') ||
            sourcePropertyName.Contains('['))
        {
            return;
        }

        var templatedParent = depObj switch
        {
            FrameworkElement frameworkElement => frameworkElement.TemplatedParent as DependencyObject,
            FrameworkContentElement frameworkContentElement => frameworkContentElement.TemplatedParent as DependencyObject,
            _ => null
        };
        if (templatedParent == null)
        {
            return;
        }

        var sourceDp = FindDependencyProperty(templatedParent, sourcePropertyName);
        if (sourceDp == null || sourceDp.ReadOnly)
        {
            return;
        }

        templatedParent.SetCurrentValue(sourceDp, ConvertValue(targetValue, sourceDp.PropertyType));
    }

    private static void TryRestoreBindingSourceValue(
        DependencyObject depObj,
        DependencyProperty dp,
        BindingExpressionBase? bindingExpression,
        CapturedExpressionState capturedState,
        object? targetValue,
        bool hasTargetValue)
    {
        if (bindingExpression == null || !CanRestoreBindingSource(capturedState.Binding))
        {
            return;
        }

        var restoreValue = hasTargetValue ? targetValue : capturedState.CapturedTargetValue;
        try
        {
            depObj.SetCurrentValue(dp, ConvertValue(restoreValue, dp.PropertyType));
            bindingExpression.UpdateSource();
        }
        catch
        {
            // Some bindings are target-only or cannot ConvertBack. Verification reports any remaining mismatch.
        }
    }

    private static bool CanRestoreBindingSource(BindingBase bindingBase) => bindingBase switch
    {
        Binding binding => binding.Mode is not BindingMode.OneWay and not BindingMode.OneTime,
        MultiBinding binding => binding.Mode is not BindingMode.OneWay and not BindingMode.OneTime,
        _ => false
    };

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
