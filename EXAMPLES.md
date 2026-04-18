# WPF DevTools MCP Server - Usage Examples

This document provides practical examples of using the WPF DevTools MCP Server.
All examples follow the recommended **scene-first workflow** and use the modern API
where `processId` is optional after the initial `connect()` call.

## Example 1: Scene-First Inspection (Recommended Starting Workflow)

### Scenario
You want to understand the UI structure of a running WPF application before diving into details.

### Solution

```json
// 1. Connect to the first available WPF process (auto-discovery)
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Get a high-level UI summary (scene-first)
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_ui_summary"
  }
}
// Response includes navigation.recommended suggesting next tools

// 3. Get a snapshot of a specific element for focused inspection
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_element_snapshot",
    "arguments": {
      "elementId": "DataGrid_1"
    }
  }
}

// 4. If it's a form, get form-specific summary
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_form_summary",
    "arguments": {
      "elementId": "Window_0"
    }
  }
}

// 5. Check visibility issues if elements aren't rendering
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "diagnose_visibility",
    "arguments": {
      "elementId": "Panel_3"
    }
  }
}
```

## Example 2: Diagnosing Binding Errors

### Scenario
Your WPF application has binding errors, but you're not sure which bindings are failing.

### Solution

```json
// 1. Connect (auto-discovery)
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Start with UI summary to understand the structure
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_ui_summary"
  }
}

// 3. Get all binding errors
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_binding_errors"
  }
}

// 4. For each error, get the DataContext chain to understand the issue
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_datacontext_chain",
    "arguments": {
      "elementId": "TextBlock_3"
    }
  }
}

// 5. Get the full binding value chain to trace the data flow
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "get_binding_value_chain",
    "arguments": {
      "elementId": "TextBlock_3",
      "propertyName": "Text"
    }
  }
}
```

## Example 3: Inspecting MVVM Architecture

### Scenario
You want to understand how ViewModels are structured and what commands are available.

### Solution

```json
// 1. Connect
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Get UI summary to find elements with data bindings
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_ui_summary"
  }
}

// 3. Get ViewModel for a specific element
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_viewmodel",
    "arguments": {
      "elementId": "Window_0"
    }
  }
}

// 4. Get all commands in the ViewModel
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_commands",
    "arguments": {
      "elementId": "Window_0"
    }
  }
}

// 5. Execute a command
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "execute_command",
    "arguments": {
      "elementId": "Window_0",
      "commandName": "SaveCommand"
    }
  }
}
```

## Example 4: DependencyProperty Investigation

### Scenario
A property has an unexpected value, and you want to understand where it comes from.

### Solution

```json
// 1. Connect
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Get the value source
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_dp_value_source",
    "arguments": {
      "elementId": "Button_1",
      "propertyName": "Background"
    }
  }
}

// 3. If it's from a Style, get applied styles
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_applied_styles",
    "arguments": {
      "elementId": "Button_1"
    }
  }
}

// 4. Get the resource chain to see where the style comes from
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_resource_chain",
    "arguments": {
      "elementId": "Button_1",
      "resourceKey": "ButtonStyle"
    }
  }
}
```

## Example 5: Mutation-Safe Workflow (State Snapshot)

### Scenario
You need to safely modify UI state and be able to revert changes if something goes wrong.

### Solution

```json
// 1. Connect
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Capture a state snapshot before making changes
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "capture_state_snapshot",
    "arguments": {
      "elementId": "Window_0",
      "includeFocus": true
    }
  }
}

// 3. Perform mutations
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "set_dp_value",
    "arguments": {
      "elementId": "TextBox_1",
      "propertyName": "Text",
      "value": "New Value"
    }
  }
}

// 4. Check what changed
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_state_diff",
    "arguments": {
      "snapshotId": "snapshot_abc123"
    }
  }
}

// 5. Restore if needed
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "restore_state_snapshot",
    "arguments": {
      "snapshotId": "snapshot_abc123"
    }
  }
}
```

## Example 6: Automated UI Testing

### Scenario
You want to automate UI testing by simulating user interactions.

### Solution

```json
// 1. Connect
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Check interaction readiness of a UI element
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_interaction_readiness",
    "arguments": {
      "elementId": "Button_2"
    }
  }
}

// 3. Click the button
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "elementId": "Button_2"
    }
  }
}

// 4. Set text via DependencyProperty (preferred for text input)
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "set_dp_value",
    "arguments": {
      "elementId": "TextBox_1",
      "propertyName": "Text",
      "value": "admin"
    }
  }
}

// 5. Use simulate_keyboard for navigation/shortcuts (not for text input)
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "simulate_keyboard",
    "arguments": {
      "elementId": "TextBox_1",
      "key": "Tab"
    }
  }
}

// 6. Capture a screenshot for verification (metadata mode for token efficiency)
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "tools/call",
  "params": {
    "name": "element_screenshot",
    "arguments": {
      "elementId": "Window_0",
      "outputMode": "metadata"
    }
  }
}

// 7. Verify validation errors
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "tools/call",
  "params": {
    "name": "get_validation_errors"
  }
}
```

## Example 7: Real-time Property Watching

### Scenario
You want to monitor property changes over time.

### Solution

```json
// 1. Connect
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Start watching a DependencyProperty
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "watch_dp_changes",
    "arguments": {
      "elementId": "ProgressBar_1",
      "propertyName": "Value"
    }
  }
}

// 3. Wait for a change (STDIO-friendly, timeout-bounded)
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "wait_for_dp_change",
    "arguments": {
      "elementId": "ProgressBar_1",
      "propertyName": "Value",
      "timeoutMs": 5000
    }
  }
}

// 4. Drain all accumulated events
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "drain_events"
  }
}
```

> **Note**: `watch_dp_changes` is registration-only over STDIO transport. Use
> `wait_for_dp_change` (polling-friendly, timeout-bounded) or `drain_events`
> to retrieve accumulated change events.

## Example 8: Layout Debugging

### Scenario
An element is clipped or not visible, and you want to understand why.

### Solution

```json
// 1. Connect
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "connect"
  }
}

// 2. Diagnose visibility first (scene-level tool)
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "diagnose_visibility",
    "arguments": {
      "elementId": "StackPanel_2"
    }
  }
}

// 3. Get detailed layout information
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_layout_info",
    "arguments": {
      "elementId": "StackPanel_2"
    }
  }
}

// 4. Check clipping information
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_clipping_info",
    "arguments": {
      "elementId": "StackPanel_2"
    }
  }
}

// 5. Highlight the element to see its bounds
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "highlight_element",
    "arguments": {
      "elementId": "StackPanel_2",
      "duration": 5000
    }
  }
}
```

## Example 9: Process Disambiguation

### Scenario
Multiple WPF applications are running and you need to connect to a specific one.

### Solution

```json
// 1. List processes with a process name filter
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_processes",
    "arguments": {
      "nameFilter": "MyApp"
    }
  }
}

// 2. Connect to the specific process
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

// 3. Continue with scene-first workflow
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_ui_summary"
  }
}
```

## Tips for AI Agents

### Scene-First Approach
Always start with `get_ui_summary` after `connect()` to understand the UI structure
before diving into specific tools. Follow `navigation.recommended` hints in responses.

### Efficient Tree Traversal
Use the `depth` parameter to minimize token usage:
```json
{
  "name": "get_visual_tree",
  "arguments": {
    "depth": 3
  }
}
```

### Token Efficiency
- Prefer scene-level tools (`get_ui_summary`, `get_element_snapshot`, `get_form_summary`) over tree dumps
- For screenshots, use `outputMode: "metadata"` or `outputMode: "file"` instead of base64
- Use `compact: true` where available to reduce response size

### Filtering Processes
Use `nameFilter` to narrow by process name, or `windowFilter` only with `visible`, `all`, or `foreground`:
```json
{
  "name": "get_processes",
  "arguments": {
    "nameFilter": "MyApp"
  }
}
```

### Connection Health
Use `ping` only when an explicit health check is needed (not as a polling mechanism):
```json
{
  "name": "ping"
}
```

### Error Recovery
Responses include structured error codes and hints. Common patterns:
- `NOT_CONNECTED` -> Call `connect()` first
- `ELEMENT_NOT_FOUND` -> Use `find_elements` to search by type/name
- `TIMEOUT` -> Target app may be busy; retry with larger timeout
# WPF DevTools MCP Server - Usage Examples

This document provides practical examples of using the WPF DevTools MCP Server.

## Example 1: Diagnosing Binding Errors

### Scenario
Your WPF application has binding errors, but you're not sure which bindings are failing.

### Solution

```json
// 1. List running WPF processes
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_processes"
  }
}

// 2. Connect to your application
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

// 3. Get all binding errors
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_binding_errors",
    "arguments": {
      "processId": 12345
    }
  }
}

// 4. For each error, get the DataContext chain to understand the issue
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "get_datacontext_chain",
    "arguments": {
      "processId": 12345,
      "elementId": "TextBlock_3"
    }
  }
}
```

## Example 2: Inspecting MVVM Architecture

### Scenario
You want to understand how ViewModels are structured and what commands are available.

### Solution

```json
// 1. Get the Visual Tree to find elements with DataContext
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_visual_tree",
    "arguments": {
      "processId": 12345,
      "depth": 2
    }
  }
}

// 2. Get ViewModel for a specific element
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_viewmodel",
    "arguments": {
      "processId": 12345,
      "elementId": "Window_0"
    }
  }
}

// 3. Get all commands in the ViewModel
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_commands",
    "arguments": {
      "processId": 12345,
      "elementId": "Window_0"
    }
  }
}

// 4. Execute a command
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "execute_command",
    "arguments": {
      "processId": 12345,
      "elementId": "Window_0",
      "commandName": "SaveCommand"
    }
  }
}
```

## Example 3: Analyzing DependencyProperty Values

### Scenario
A property has an unexpected value, and you want to understand where it comes from.

### Solution

```json
// 1. Get the value source
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_dp_value_source",
    "arguments": {
      "processId": 12345,
      "elementId": "Button_1",
      "propertyName": "Background"
    }
  }
}

// 2. If it's from a Style, get applied styles
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_applied_styles",
    "arguments": {
      "processId": 12345,
      "elementId": "Button_1"
    }
  }
}

// 3. Get the resource chain to see where the style comes from
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "get_resource_chain",
    "arguments": {
      "processId": 12345,
      "elementId": "Button_1",
      "resourceKey": "ButtonStyle"
    }
  }
}
```

## Example 4: Performance Debugging

### Scenario
Your application is slow, and you want to identify performance bottlenecks.

### Solution

```json
// 1. Get render statistics
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_render_stats",
    "arguments": {
      "processId": 12345
    }
  }
}

// 2. Count visual elements to check for excessive complexity
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_visual_count",
    "arguments": {
      "processId": 12345
    }
  }
}

// 3. Find binding leaks
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "find_binding_leaks",
    "arguments": {
      "processId": 12345,
      "threshold": 100
    }
  }
}

// 4. Measure render time for specific elements
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "measure_element_render_time",
    "arguments": {
      "processId": 12345,
      "elementId": "DataGrid_1"
    }
  }
}
```

## Example 5: Automated Testing

### Scenario
You want to automate UI testing by simulating user interactions.

### Solution

```json
// 1. Find the button to click
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_visual_tree",
    "arguments": {
      "processId": 12345,
      "depth": 2
    }
  }
}

// 2. Click the button
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "click_element",
    "arguments": {
      "processId": 12345,
      "elementId": "Button_2"
    }
  }
}

// 3. Set text via DependencyProperty (preferred for text input)
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "set_dp_value",
    "arguments": {
      "processId": 12345,
      "elementId": "TextBox_1",
      "propertyName": "Text",
      "value": "admin"
    }
  }
}

// 4. Use simulate_keyboard for navigation/shortcuts (not for text input)
{
  "jsonrpc": "2.0",
  "id": "3b",
  "method": "tools/call",
  "params": {
    "name": "simulate_keyboard",
    "arguments": {
      "processId": 12345,
      "elementId": "TextBox_1",
      "key": "Tab"
    }
  }
}

// 5. Capture a screenshot for verification
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "element_screenshot",
    "arguments": {
      "processId": 12345,
      "elementId": "Window_0"
    }
  }
}

// 6. Verify validation errors
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "get_validation_errors",
    "arguments": {
      "processId": 12345
    }
  }
}
```

## Example 6: Tracing RoutedEvents

### Scenario
You want to understand how a routed event propagates through the Visual Tree.

### Solution

```json
// 1. Start tracing a routed event
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "trace_routed_events",
    "arguments": {
      "processId": 12345,
      "eventName": "MouseDown"
    }
  }
}

// 2. Get event handlers for a specific element
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_event_handlers",
    "arguments": {
      "processId": 12345,
      "elementId": "Button_1",
      "eventName": "Click"
    }
  }
}

// 3. Fire a custom routed event
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "fire_routed_event",
    "arguments": {
      "processId": 12345,
      "elementId": "Button_1",
      "eventName": "CustomEvent"
    }
  }
}
```

## Example 7: Real-time Property Watching

> **Note**: Step 2 requires HTTP+SSE transport which is planned but not yet implemented.
> In the current STDIO transport, use polling with `get_dp_value_source` to check for changes.

### Scenario
You want to monitor property changes in real-time.

### Solution

```json
// 1. Start watching a DependencyProperty
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "watch_dp_changes",
    "arguments": {
      "processId": 12345,
      "elementId": "ProgressBar_1",
      "propertyName": "Value"
    }
  }
}

// 2. In STDIO mode, poll with get_dp_value_source to check for changes.
// (HTTP+SSE transport with push events is planned but not yet implemented.)

// 3. Modify the property to trigger a change
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "set_dp_value",
    "arguments": {
      "processId": 12345,
      "elementId": "ProgressBar_1",
      "propertyName": "Value",
      "value": 75
    }
  }
}
```

## Example 8: Layout Debugging

### Scenario
An element is clipped or not visible, and you want to understand why.

### Solution

```json
// 1. Get layout information
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_layout_info",
    "arguments": {
      "processId": 12345,
      "elementId": "StackPanel_2"
    }
  }
}

// 2. Check clipping information
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_clipping_info",
    "arguments": {
      "processId": 12345,
      "elementId": "StackPanel_2"
    }
  }
}

// 3. Highlight the element to see its bounds
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "highlight_element",
    "arguments": {
      "processId": 12345,
      "elementId": "StackPanel_2",
      "duration": 5000
    }
  }
}

// 4. Force a layout update
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "invalidate_layout",
    "arguments": {
      "processId": 12345,
      "elementId": "StackPanel_2"
    }
  }
}
```

## Tips for AI Agents

### Efficient Tree Traversal

Always use the `depth` parameter to minimize token usage:

```json
{
  "name": "get_visual_tree",
  "arguments": {
    "processId": 12345,
    "depth": 3
  }
}
```

### Filtering Processes

Use `nameFilter` on `get_processes` to narrow down results:

```json
{
  "name": "get_processes",
  "arguments": {
    "nameFilter": "MyApp"
  }
}
```

### Error Handling

Always check for errors in responses:

```json
{
  "success": false,
  "error": "Process 12345 is not connected"
}
```

### Connection Management

Ping regularly to verify connection health:

```json
{
  "name": "ping",
  "arguments": {
    "processId": 12345
  }
}
```
