# State and Interaction Workflow Examples

These examples cover state snapshots, interaction checks, and change-watching
flows. The JSON blocks group sequential calls as arrays for parser validation;
send each request one at a time unless your client explicitly supports batching.

## Example 5: Mutation-Safe Workflow

### Scenario

You need to safely modify UI state and be able to revert changes if something
goes wrong.

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
      "name": "capture_state_snapshot",
      "arguments": {
        "elementId": "Window_0",
        "includeFocus": true
      }
    }
  },
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
  },
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
  },
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
]
```

## Example 6: Automated UI Testing

### Scenario

You want to automate UI testing by simulating user interactions.

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
      "name": "get_interaction_readiness",
      "arguments": {
        "elementId": "Button_2"
      }
    }
  },
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
  },
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
  },
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
  },
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
  },
  {
    "jsonrpc": "2.0",
    "id": 7,
    "method": "tools/call",
    "params": {
      "name": "get_validation_errors"
    }
  }
]
```

## Example 7: Real-Time Property Watching

### Scenario

You want to monitor property changes over time.

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
      "name": "watch_dp_changes",
      "arguments": {
        "elementId": "ProgressBar_1",
        "propertyName": "Value"
      }
    }
  },
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
  },
  {
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
      "name": "drain_events"
    }
  }
]
```

`watch_dp_changes` is registration-only over STDIO transport. Use
`wait_for_dp_change` for a polling-friendly timeout-bounded wait, or use
`drain_events` to retrieve accumulated change events.
