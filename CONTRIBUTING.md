# Contributing to WPF DevTools MCP Server

We welcome contributions! Please follow these guidelines to ensure a smooth collaboration process.

## Coding Standards

- Follow C# coding conventions and .NET best practices
- Use meaningful variable and method names
- Keep methods small and focused (< 50 lines)
- Keep files focused (< 500 lines)
- Use immutable patterns (avoid mutation)
- Handle errors explicitly at every level

## Test-Driven Development (Required)

All new features and bug fixes MUST follow TDD workflow:

1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Improve code while keeping tests green
4. **VERIFY**: Ensure 80%+ code coverage

```bash
# Build the target unit test project first
dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug

# Run unit tests without rebuilding
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug --no-build

# Check coverage after an explicit build
dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Release
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Release --no-build --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```

## Branch Naming

- `feature/description` - New features
- `fix/description` - Bug fixes
- `refactor/description` - Code refactoring
- `docs/description` - Documentation updates

## Commit Format

Follow conventional commits:

```
<type>: <description>

<optional body>
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`

## Pull Request Process

1. Fork the repository
2. Create a feature branch from `master`
3. Write tests first (TDD)
4. Implement feature/fix
5. Ensure all tests pass and coverage is 80%+
6. Update documentation if needed
7. Submit pull request with clear description
8. Address review feedback

## Code Review Checklist

Before submitting your pull request, ensure:

- [ ] Tests written and passing
- [ ] Code coverage 80%+
- [ ] No hardcoded values
- [ ] Proper error handling
- [ ] Documentation updated
- [ ] No breaking changes (or clearly documented)

## Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- .NET Framework 4.8 targeting pack
- Windows 10 or later
- Visual Studio 2022 or VS Code (optional)

### Build and Test

```bash
# Clone the repository
git clone https://github.com/Evanlau1798/wpf-devtools-mcp.git
cd wpf-devtools-mcp

# Build managed projects
dotnet build

# Build the native bootstrapper required by live bootstrap integration tests
# Replace <DOTNET_VERSION> below with your installed .NET host pack version
# (e.g. 8.0.22). Check available versions under:
#   C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj /m /p:Configuration=Debug /p:Platform=x64 /p:NetHostIncludeDir="C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\<DOTNET_VERSION>\runtimes\win-x64\native" /p:NetHostLibDir="C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\<DOTNET_VERSION>\runtimes\win-x64\native"

# Run all tests after build (separate step avoids file-lock issues)
dotnet test --no-build

# Run a specific test project after building it explicitly
dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug --no-build

# Run tests with coverage (IMPORTANT: Use coverlet.runsettings)
dotnet build tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug
dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Debug --no-build --settings coverlet.runsettings --collect:"XPlat Code Coverage"

# Build for release
dotnet build -c Release
```

If you run `tests/WpfDevTools.Tests.Integration/BootstrapInjectionTests.cs`, build the matching native bootstrapper artifact first. The smoke test now fails fast when bootstrapper DLLs are missing so CI and local runs cannot report false green coverage.

**Test Coverage Requirements:**
- Minimum: 80% line coverage (MANDATORY)
- Current Status: 83.17% ✅ (Exceeds target)
- Unit test coverage is the primary metric
- Integration test coverage (27%) is normal for end-to-end tests

## Questions or Issues?

If you have questions or encounter issues:

1. Check existing [Issues](https://github.com/Evanlau1798/wpf-devtools-mcp/issues)
2. Review the [README.md](README.md) for documentation
3. Open a new issue with detailed information

Thank you for contributing to WPF DevTools MCP Server!
