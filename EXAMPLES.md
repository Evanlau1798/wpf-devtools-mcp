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
      "elementId": "errorElement1"
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
      "elementId": "mainWindow"
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
      "elementId": "mainWindow"
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
      "elementId": "mainWindow",
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
      "elementId": "myButton",
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
      "elementId": "myButton"
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
      "elementId": "myButton",
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
      "elementId": "complexControl"
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
      "elementId": "submitButton"
    }
  }
}

// 3. Simulate keyboard input in a TextBox (send individual key presses)
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "simulate_keyboard",
    "arguments": {
      "processId": 12345,
      "elementId": "usernameTextBox",
      "key": "T"
    }
  }
}

// Repeat for each character, or use Tab to move to the next field
{
  "jsonrpc": "2.0",
  "id": "3b",
  "method": "tools/call",
  "params": {
    "name": "simulate_keyboard",
    "arguments": {
      "processId": 12345,
      "elementId": "usernameTextBox",
      "key": "Tab"
    }
  }
}

// 4. Capture a screenshot for verification
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "element_screenshot",
    "arguments": {
      "processId": 12345,
      "elementId": "mainWindow"
    }
  }
}

// 5. Verify validation errors
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
      "elementId": "myButton",
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
      "elementId": "myButton",
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
      "elementId": "progressBar",
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
      "elementId": "progressBar",
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
      "elementId": "myControl"
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
      "elementId": "myControl"
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
      "elementId": "myControl",
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
      "elementId": "myControl"
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
