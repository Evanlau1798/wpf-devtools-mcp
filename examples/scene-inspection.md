# Scene Inspection and Diagnostics Examples

These workflows start with scene-level summaries before using focused diagnostic
tools. The JSON blocks group sequential calls as arrays for parser validation;
send each request one at a time unless your client explicitly supports batching.

## Example 1: Scene-First Inspection

### Scenario

You want to understand the UI structure of a running WPF application before
diving into details.

### Solution

```json
[
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "connect"
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "get_ui_summary"
    }
  },
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
  },
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
  },
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
]
```

## Example 2: Diagnosing Binding Errors

### Scenario

Your WPF application has binding errors, but you are not sure which bindings are
failing.

### Solution

```json
[
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "connect"
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "get_ui_summary"
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "get_binding_errors"
    }
  },
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
  },
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
]
```

## Example 3: Inspecting MVVM Architecture

### Scenario

You want to understand how ViewModels are structured and what commands are
available.

### Local prerequisites

Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the target executable's
exact absolute path before `connect`. This workflow inspects runtime ViewModel
state and invokes a command, so enable
`WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` for `get_viewmodel` and
`get_commands`, and enable both `WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION` and
`WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS` for `execute_command`.

### Solution

```json
[
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "connect"
    }
  },
  {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "get_ui_summary"
    }
  },
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
  },
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
  },
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
]
```

## Example 4: DependencyProperty Investigation

### Scenario

A property has an unexpected value, and you want to understand where it comes
from.

### Solution

```json
[
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "connect"
    }
  },
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
  },
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
  },
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
]
```
