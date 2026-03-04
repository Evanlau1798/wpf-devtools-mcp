# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial implementation of MCP Server with STDIO transport
- 44 MCP tools across 10 categories:
  - Process Management (3 tools)
  - Tree & XAML (6 tools)
  - Binding Diagnostics (5 tools)
  - DependencyProperty (5 tools)
  - Style/Template (4 tools)
  - RoutedEvent (3 tools)
  - Interaction (5 tools)
  - Layout (4 tools)
  - MVVM (5 tools)
  - Performance (4 tools)
- DLL injection framework based on Snoop WPF
- Named Pipes IPC for Inspector-to-Server communication
- Inspector DLL with WPF analysis capabilities
- Multi-targeting support (.NET 8.0 + .NET Framework 4.8)
- Multi-architecture support (x86, x64, ARM64)
- Comprehensive test suite (686 tests)
- Test-driven development workflow
- File-based logging system
- Session management for multiple WPF processes
- Element ID tracking and lookup
- UI thread marshalling with timeout protection

### Security
- Input validation for all tool parameters
- Process ID validation
- Element ID validation
- Timeout protection for UI thread operations
- Safe JSON serialization with circular reference handling

### Documentation
- Comprehensive README with tool documentation
- Configuration examples for Claude Desktop, Cursor, VS Code
- Troubleshooting guide
- Contributing guidelines with TDD requirements
- CLAUDE.md for AI agent guidance
- MIT License with Ms-PL attribution for Snoop-based code

## [0.1.0] - TBD

### Planned
- HTTP+SSE transport for web-based AI agents
- SDK mode for opt-in inspection (no injection required)
- NuGet package distribution
- Code signing for antivirus compatibility
- Additional performance optimization tools
- Enhanced error reporting
- Real-time property change notifications via SSE
- Visual element highlighting improvements
- XAML export/import capabilities

---

## Release Notes

### Version 0.1.0 (Planned)
First public release with core functionality:
- STDIO transport
- 44 MCP tools
- DLL injection
- Named Pipes IPC
- Multi-targeting support

### Future Releases
- 0.2.0: HTTP+SSE transport, SDK mode
- 0.3.0: NuGet packages, code signing
- 1.0.0: Production-ready release with full documentation
