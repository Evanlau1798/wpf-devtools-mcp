using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

[McpServerResourceType]
public static class CapabilityResources
{
    [McpServerResource(
        Name = "wpf_capabilities",
        Title = "WPF Capabilities",
        UriTemplate = "wpf://capabilities",
        MimeType = "text/markdown")]
    [Description("Capability summary for discovery, transport limits, server metadata, and high-value workflows.")]
    public static string GetCapabilities() =>
        $"""
        # WPF DevTools MCP Capabilities

        - Server: `wpf-devtools-mcp`
        - Version: `{ServerMetadata.GetDisplayVersion()}`
        - Schema version: `{ServerMetadata.GetSchemaVersion()}`
        - Transport: `stdio`
        - Tool surface: WPF process discovery, connection, tree inspection, binding diagnostics, DependencyProperty analysis, MVVM inspection, style/template inspection, interaction, layout, performance, and routed-event diagnostics
        - Prompt surface: workflow entry points for connection, binding diagnosis, command/click diagnosis, elevated-target diagnosis, and secondary-window inspection
        - Resource surface: capability summary, workflow references, elevated-target limitations, and window/focus limitations
        - Feature flags: `prompts=true`, `resources=true`, `stateSnapshots=true`, `diagnosticNormalization=true`, `elevatedTargetDiagnostics=true`

        ## Transport notes

        - STDIO mode is request/response oriented.
        - Watchers and event traces should be treated as polling-oriented workflows, not true push subscriptions.
        - Prompt discovery can surface as slash commands in clients that support MCP prompts.
        - Resource discovery can surface through `@resource` references in clients that support MCP resources.

        ## Known capability boundaries

        - Elevated targets require the MCP server itself to run as administrator.
        - Main-window targeting is the default when `elementId` is omitted.
        - Runtime mutations are not persisted to XAML.
        - Snapshot restore currently supports DependencyProperty local values, scalar ViewModel values, and focus restoration.
        """;

    [McpServerResource(
        Name = "wpf_binding_workflow",
        Title = "Binding Workflow",
        UriTemplate = "wpf://workflows/binding-debug",
        MimeType = "text/markdown")]
    [Description("Canonical workflow for binding errors, value-chain tracing, DataContext inheritance, and validation correlation.")]
    public static string GetBindingWorkflow() =>
        """
        # Binding Debug Workflow

        Use this when UI data is blank, wrong, or stale.

        1. `get_binding_errors`
        2. `get_visual_tree` or `get_logical_tree` to locate the relevant `elementId`
        3. `get_bindings`
        4. `get_binding_value_chain`
        5. `get_datacontext_chain`
        6. `get_validation_errors` when validation may block updates

        Cross-tool semantics:
        - `get_binding_errors` reports failures captured by WPF binding tracing.
        - `get_binding_value_chain` explains how one binding resolved.
        - `get_datacontext_chain` explains inherited source context.
        - `get_validation_errors` explains rule-based invalid state on the element subtree.
        """;

    [McpServerResource(
        Name = "wpf_elevated_target_limitations",
        Title = "Elevated Target Limitations",
        UriTemplate = "wpf://limitations/elevated-targets",
        MimeType = "text/markdown")]
    [Description("Explains why elevated targets may be discoverable but still reject connect or control operations.")]
    public static string GetElevatedTargetLimitations() =>
        """
        # Elevated Target Limitations

        A non-administrator MCP server can often discover an elevated WPF process, but it cannot inject into or control it.

        Expected behavior:
        - `get_processes` may list the target and mark `isElevated` / `requiresElevationToConnect`.
        - `connect` may fail with `AccessDenied`.
        - Interaction and mutation tools also require matching privilege if the target is elevated.

        In stdio mode, the MCP server inherits the privilege level of the host client process.
        To control an administrator-launched target, start the host client and MCP server as administrator.
        """;

    [McpServerResource(
        Name = "wpf_injection_failure_limitations",
        Title = "Injection Failure Limitations",
        UriTemplate = "wpf://limitations/injection-failures",
        MimeType = "text/markdown")]
    [Description("Summarizes high-signal injection failure causes, packaging constraints, and the current SDK-mode escape hatch.")]
    public static string GetInjectionFailureLimitations() =>
        """
        # Injection Failure Limitations

        Common high-signal failure classes:

        - `architecture mismatch`: MCP server / injector and target process bitness do not match.
        - `access denied`: target privilege is higher than the MCP host process.
        - `timeout`: target process is hung, blocked, or the inspector pipe never became available.
        - `unsupported packaging`: single-file, trimmed, or Native AOT targets may reject injection.
        - `security software interference`: AV / endpoint controls may block DLL injection or named pipes.

        Current mitigation guidance:

        - Prefer matching architecture builds.
        - Run the MCP host as administrator for elevated targets.
        - Use SDK mode or an opt-in integration path when packaging blocks injection.
        """;

    [McpServerResource(
        Name = "wpf_window_focus_limitations",
        Title = "Window And Focus Limitations",
        UriTemplate = "wpf://limitations/window-focus",
        MimeType = "text/markdown")]
    [Description("Clarifies main-window defaults, multi-window targeting rules, and focus-sensitive behavior.")]
    public static string GetWindowFocusLimitations() =>
        """
        # Window And Focus Limitations

        Multi-window WPF inspection is supported, but targeting rules matter:

        - Omitting `elementId` targets `Application.MainWindow`, not the currently focused window.
        - Use `get_windows` first when dialogs, popups, or tool windows are present.
        - Re-check `get_windows` after focus or visibility changes.
        - Prefer explicit window `elementId` targeting for tree inspection, screenshots, and interactions.

        This avoids accidental inspection of the wrong root when secondary windows are active.
        """;

    [McpServerResource(
        Name = "wpf_state_safety_notes",
        Title = "Runtime State Safety Notes",
        UriTemplate = "wpf://limitations/state-safety",
        MimeType = "text/markdown")]
    [Description("Summarizes current mutation safety boundaries until snapshot/restore helpers exist.")]
    public static string GetStateSafetyNotes() =>
        """
        # Runtime State Safety Notes

        Mutation tools change the live application state but do not persist to XAML.

        Current safety model:
        - Changes are process-local and reset on app restart.
        - Snapshot/restore is session-scoped, not durable persistence.
        - `capture_state_snapshot` can capture DependencyProperty local values, scalar ViewModel values, and focus state.
        - `restore_state_snapshot` can replay those captured values in the same connected session.
        - Prefer minimal mutations and capture a snapshot before a debugging sequence when rollback matters.

        Use this guidance in demos, troubleshooting, and regression flows to avoid cross-test contamination.
        """;
}
