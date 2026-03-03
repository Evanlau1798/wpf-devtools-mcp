# WPF DevTools MCP Server

A Model Context Protocol (MCP) server that enables AI agents to deeply inspect and interact with running WPF applications through in-process DLL injection.

## Features

### Core Capabilities

- **Process Management**: Discover and connect to running WPF applications
- **Visual Tree Inspection**: Browse and analyze the Visual and Logical trees
- **Binding Diagnostics**: Detect and diagnose binding errors and DataContext chains
- **MVVM Support**: Inspect ViewModels, execute commands, and modify properties
- **DependencyProperty Analysis**: Analyze value sources, metadata, and precedence
- **Style & Template Inspection**: Examine applied styles, triggers, and templates
- **RoutedEvent Tracing**: Trace event routing and fire custom events
- **Layout Analysis**: Inspect layout information, clipping, and transforms
- **Performance Diagnostics**: Measure render statistics and detect binding leaks
- **Element Interaction**: Click elements, simulate keyboard input, and capture screenshots

### Unique Value Proposition

First free, open-source MCP server providing WPF-specific deep inspection capabilities that are impossible with out-of-process tools:
- Direct access to `BindingOperations`, `DependencyPropertyHelper`, and Visual Tree internals
- In-process execution for accurate binding error detection
- Real-time property change watching
- Command execution with CanExecute validation

## Installation

### Prerequisites

- .NET 8.0 SDK or later
- Windows OS
- Visual Studio 2022 or VS Code (optional)

### Build from Source

```bash
git clone https://github.com/yourusername/wpf-devtools-mcp.git
cd wpf-devtools-mcp
dotnet build
```

### Run Tests

```bash
dotnet test
```

## Quick Start

### 1. Start the MCP Server

```bash
dotnet run --project src/WpfDevTools.Mcp.Server/
```

The server will start in STDIO mode and wait for MCP protocol messages.

### 2. Connect to a WPF Application

Use the `get_processes` tool to list running WPF applications:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_processes"
  }
}
```

Connect to a specific process using the `connect` tool:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "connect",
    "arguments": {
      "processId": 12345
    }
  }
}
```

### 3. Inspect the Visual Tree

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_visual_tree",
    "arguments": {
      "processId": 12345,
      "depth": 3
    }
  }
}
```

## Architecture

```
AI Agent (Claude Code/Cursor/etc)
    ↓ MCP Protocol (STDIO/HTTP+SSE)
MCP Server (Tool Router, Session Manager)
    ↓ Named Pipes IPC (0.1-1ms latency)
Injected Inspector DLL (In-Process)
    ↓ Direct Memory Access
Target WPF Application
```

### Key Design Decisions

- **In-Process Injection**: Required for accessing WPF internals
- **Named Pipes**: Message mode with length-prefix framing
- **UI Thread Marshalling**: All Visual Tree operations use `Dispatcher.Invoke()`
- **Token Efficiency**: All tools support `depth`, `filter`, `compact` parameters

## MCP Tools (42 Total)

### 1. Process Management (3 tools)

#### get_processes
List all running WPF applications.

**Parameters**: None

**Returns**: Array of process information (PID, name, title, architecture, .NET version)

#### connect
Connect to a WPF application (performs injection if needed).

**Parameters**:
- `processId` (required): Process ID to connect to

**Returns**: Connection status

#### ping
Verify connection health and measure latency.

**Parameters**:
- `processId` (required): Process ID

**Returns**: Latency in milliseconds

### 2. Tree & XAML (5 tools)

#### get_visual_tree
Retrieve the Visual Tree structure.

**Parameters**:
- `processId` (required): Process ID
- `depth` (optional): Maximum tree depth (default: unlimited)
- `filter` (optional): Filter by element type
- `compact` (optional): Return minimal properties (default: false)

**Returns**: Visual Tree structure

#### get_logical_tree
Retrieve the Logical Tree structure.

**Parameters**: Same as `get_visual_tree`

**Returns**: Logical Tree structure

#### compare_trees
Compare Visual and Logical trees to identify differences.

**Parameters**:
- `processId` (required): Process ID

**Returns**: Comparison result with differences highlighted

#### serialize_to_xaml
Serialize element to XAML.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: root)

**Returns**: XAML string

#### get_namescope
Get NameScope for element lookup.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: root)

**Returns**: NameScope dictionary

### 3. Binding Diagnostics (5 tools)

#### get_bindings
Get all bindings for an element or tree.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: root)
- `recursive` (optional): Include child elements (default: false)

**Returns**: Array of binding information

#### get_binding_errors
Get all binding errors in the application.

**Parameters**:
- `processId` (required): Process ID

**Returns**: Array of binding errors with details

#### get_binding_value_chain
Get the complete value resolution chain for a binding.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name

**Returns**: Value chain from source to target

#### get_datacontext_chain
Get the DataContext inheritance chain.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: DataContext chain from element to root

#### force_binding_update
Force a binding to update.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name

**Returns**: Update result

### 4. DependencyProperty (5 tools)

#### get_dp_value_source
Get the value source for a DependencyProperty.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name

**Returns**: Value source (Local, Style, Template, Inherited, Default)

#### get_dp_metadata
Get metadata for a DependencyProperty.

**Parameters**:
- `processId` (required): Process ID
- `propertyName` (required): Property name

**Returns**: Property metadata

#### set_dp_value
Set a DependencyProperty value.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name
- `value` (required): New value

**Returns**: Success status

#### clear_dp_value
Clear a DependencyProperty local value.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name

**Returns**: Success status

#### watch_dp_changes
Watch for DependencyProperty changes.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name

**Returns**: Subscription ID (events pushed via SSE)

### 5. Style/Template (5 tools)

#### get_applied_styles
Get all applied styles for an element.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Array of applied styles

#### get_triggers
Get all triggers for an element.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Array of triggers with conditions

#### get_template_tree
Get the template Visual Tree.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Template tree structure

#### get_resource_chain
Get the resource lookup chain.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `resourceKey` (required): Resource key

**Returns**: Resource chain from element to application

#### override_style_setter
Override a style setter value.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name
- `value` (required): New value

**Returns**: Success status

### 6. RoutedEvent (3 tools)

#### trace_routed_events
Trace routed event propagation.

**Parameters**:
- `processId` (required): Process ID
- `eventName` (required): Event name

**Returns**: Event trace (tunneling → bubbling)

#### get_event_handlers
Get all handlers for a routed event.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `eventName` (required): Event name

**Returns**: Array of event handlers

#### fire_routed_event
Fire a routed event.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `eventName` (required): Event name

**Returns**: Success status

### 7. Interaction (5 tools)

#### click_element
Simulate a mouse click on an element.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Success status

#### drag_and_drop
Simulate drag and drop.

**Parameters**:
- `processId` (required): Process ID
- `sourceElementId` (required): Source element ID
- `targetElementId` (required): Target element ID

**Returns**: Success status

#### scroll_to_element
Scroll an element into view.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Success status

#### simulate_keyboard
Simulate keyboard input.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `text` (required): Text to type

**Returns**: Success status

#### element_screenshot
Capture a screenshot of an element.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Base64-encoded PNG image

### 8. Layout (4 tools)

#### get_layout_info
Get layout information for an element.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Layout information (ActualWidth, ActualHeight, DesiredSize, RenderSize)

#### highlight_element
Highlight an element in the UI.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `duration` (optional): Highlight duration in ms (default: 2000)

**Returns**: Success status

#### get_clipping_info
Get clipping information for an element.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Clipping geometry

#### invalidate_layout
Force a layout update.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: root)

**Returns**: Success status

### 9. MVVM (5 tools)

#### get_viewmodel
Get ViewModel from DataContext.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: ViewModel type and properties

#### get_commands
Get all ICommand properties from ViewModel.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID

**Returns**: Array of commands with CanExecute status

#### execute_command
Execute a command.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `commandName` (required): Command name
- `parameter` (optional): Command parameter

**Returns**: Execution result

#### modify_viewmodel
Modify a ViewModel property.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (required): Element ID
- `propertyName` (required): Property name
- `value` (required): New value

**Returns**: Success status

#### get_validation_errors
Get all validation errors.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: all)

**Returns**: Array of validation errors

### 10. Performance (4 tools)

#### get_render_stats
Get rendering statistics.

**Parameters**:
- `processId` (required): Process ID

**Returns**: Frame rate, frame time, visual count

#### find_binding_leaks
Find potential binding memory leaks.

**Parameters**:
- `processId` (required): Process ID
- `threshold` (optional): Leak threshold (default: 100)

**Returns**: Array of potential leaks

#### measure_element_render_time
Measure element render time.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: root)

**Returns**: Render time in milliseconds

#### get_visual_count
Get visual element count.

**Parameters**:
- `processId` (required): Process ID
- `elementId` (optional): Element ID (default: root)

**Returns**: Total visual element count

## SDK Mode (Opt-in)

For applications that cannot use DLL injection (single-file apps, Native AOT, etc.), use the SDK mode:

### Installation

```bash
dotnet add package WpfDevTools.Inspector.Sdk
```

### Usage

```csharp
using WpfDevTools.Inspector.Sdk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InspectorSdk.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        InspectorSdk.Shutdown();
        base.OnExit(e);
    }
}
```

## HTTP+SSE Transport

For web-based AI agents, use HTTP+SSE transport instead of STDIO:

```bash
dotnet run --project src/WpfDevTools.Mcp.Server/ -- --transport http --port 3000
```

### Endpoints

- `POST /mcp` - Send MCP requests
- `GET /events` - Subscribe to SSE events (property changes, etc.)

## Troubleshooting

### DLL Injection Fails

**Symptom**: `connect` tool returns "Injection failed"

**Possible Causes**:
1. Architecture mismatch (x86 vs x64)
2. Antivirus blocking injection
3. Self-contained single-file app
4. Native AOT app

**Solutions**:
1. Ensure MCP Server architecture matches target app
2. Add exception to antivirus
3. Use SDK mode instead

### Binding Errors Not Detected

**Symptom**: `get_binding_errors` returns empty array

**Possible Causes**:
1. Binding errors occur before connection
2. PresentationTraceSources not enabled

**Solutions**:
1. Connect early in application lifecycle
2. Enable PresentationTraceSources in app.config

### Named Pipe Connection Timeout

**Symptom**: Connection hangs or times out

**Possible Causes**:
1. Inspector DLL failed to initialize
2. UI thread blocked
3. Firewall blocking Named Pipes

**Solutions**:
1. Check Inspector logs in temp directory
2. Ensure UI thread is responsive
3. Allow Named Pipes in firewall

### Performance Issues

**Symptom**: Application becomes slow after connection

**Possible Causes**:
1. Deep tree traversal with no depth limit
2. Too many property change watchers
3. Frequent screenshot captures

**Solutions**:
1. Use `depth` parameter to limit tree traversal
2. Unwatch properties when done
3. Capture screenshots sparingly

## Development

### Project Structure

```
src/
├── WpfDevTools.Mcp.Server/       # MCP Server (STDIO/HTTP)
├── WpfDevTools.Inspector/        # Injected DLL
├── WpfDevTools.Inspector.Sdk/    # Opt-in SDK
├── WpfDevTools.Injector/         # DLL injection
└── WpfDevTools.Shared/           # Shared types

tests/
├── WpfDevTools.Tests.Unit/       # Unit tests
├── WpfDevTools.Tests.Integration/ # Integration tests
└── WpfDevTools.Tests.TestApp/    # Test WPF app
```

### Build Commands

```bash
# Build all projects
dotnet build

# Run tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Build release
dotnet build -c Release
```

### Contributing

1. Fork the repository
2. Create a feature branch
3. Write tests (TDD required)
4. Ensure 80%+ code coverage
5. Submit a pull request

## License

MIT License - see LICENSE file for details

## Acknowledgments

- DLL injection based on [Snoop WPF](https://github.com/snoopwpf/snoopwpf) (Ms-PL)
- MCP Protocol by [Anthropic](https://modelcontextprotocol.io/)
