# Security Policy

## Supported Versions

We release security updates for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 0.1.x   | ✓ Supported        |
| < 0.1   | ✗ Not Supported    |

## Reporting a Vulnerability

**Please do NOT report security vulnerabilities through public GitHub issues.**

Instead, please report them via one of the following methods:

1. **Email**: Send details to the project maintainer (see GitHub profile)
2. **GitHub Security Advisory**: Use the "Security" tab → "Report a vulnerability"

### What to Include

Please include the following information in your report:

- Type of vulnerability (e.g., code injection, privilege escalation, information disclosure)
- Full paths of source file(s) related to the vulnerability
- Location of the affected source code (tag/branch/commit or direct URL)
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the vulnerability (what an attacker could achieve)

### Response Timeline

- **Initial Response**: Within 48 hours
- **Status Update**: Within 7 days
- **Fix Timeline**: Depends on severity
  - Critical: 1-2 weeks
  - High: 2-4 weeks
  - Medium: 4-8 weeks
  - Low: Next release cycle

## Security Considerations

### DLL Injection

**Risk**: WPF DevTools uses DLL injection to inspect target applications.

**Mitigations**:
- Requires administrator privileges
- DLL path validation (no network paths, no system directories, no path traversal)
- Optional Authenticode signature verification
- Inspector runs in-process with target application (same security context)

**Recommendations**:
- Only inject into applications you trust
- Enable code signing verification in production: `WPFDEVTOOLS_REQUIRE_SIGNATURE=1`
- Review DLL source code before building

### Named Pipes Communication

**Risk**: Inter-process communication via Named Pipes.

**Mitigations**:
- ACL-restricted pipes (current user + SYSTEM only)
- Local-only communication (no network access)
- Message size limits (10 MB max)
- Timeout protection on all operations

**Recommendations**:
- Run MCP Server and target application under the same user account
- Do not expose Named Pipes to untrusted processes

### Reflection-Based Property Modification

**Risk**: `modify_viewmodel` and `execute_command` tools use reflection to modify application state.

**Mitigations**:
- Property blacklist prevents modification of sensitive properties (password, token, secret, etc.)
- Audit logging of all modifications
- Command execution requires `CanExecute` check

**Recommendations**:
- Use read-only tools (`get_viewmodel`, `get_bindings`) when possible
- Review audit logs regularly
- Consider implementing custom property whitelist for your application

### Information Disclosure

**Risk**: MCP tools can read application state, including potentially sensitive data.

**Mitigations**:
- Error messages sanitized to prevent path disclosure
- No automatic data exfiltration
- All operations require explicit MCP tool calls

**Recommendations**:
- Do not use on applications handling sensitive data in production
- Intended for development and debugging only
- Clear sensitive data from memory before inspection

## Known Limitations

1. **Self-Contained Single-File Apps**: Cannot inject (Snoop limitation)
2. **Native AOT Apps**: Cannot inject
3. **Trimmed Apps**: May fail if dependencies removed
4. **Antivirus Software**: May block injection (requires code signing)

## Security Best Practices

### For Developers

1. **Enable Code Signing**: Set `WPFDEVTOOLS_REQUIRE_SIGNATURE=1`
2. **Review Audit Logs**: Check for unauthorized property modifications
3. **Limit Tool Usage**: Use only necessary tools, avoid `execute_command` in production
4. **Update Regularly**: Apply security patches promptly

### For Users

1. **Verify Source**: Only use official releases or build from source
2. **Check Signatures**: Verify DLL signatures before injection
3. **Isolate Environment**: Use in development/test environments only
4. **Monitor Activity**: Watch for unexpected behavior after injection

## Security Updates

Security updates will be announced via:
- GitHub Security Advisories
- Release notes (CHANGELOG.md)
- GitHub Releases page

Subscribe to repository notifications to receive security alerts.

## Acknowledgments

We appreciate responsible disclosure of security vulnerabilities. Contributors who report valid security issues will be acknowledged in release notes (unless they prefer to remain anonymous).

## Contact

For security-related questions or concerns, please contact the project maintainers through GitHub.

---

**Last Updated**: 2026-03-05
