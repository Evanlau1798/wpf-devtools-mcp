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

## Implemented Security Mechanisms

### Challenge-Response Authentication

WPF DevTools uses HMAC-SHA256 Challenge-Response authentication to verify MCP Server identity before allowing Named Pipe communication.

**Protocol**:
1. Inspector generates 32-byte cryptographic random challenge
2. Inspector sends challenge to MCP Server over Named Pipe
3. MCP Server computes `HMAC-SHA256(shared_secret, challenge)` and sends 32-byte response
4. Inspector verifies response using constant-time comparison (`CryptographicOperations.FixedTimeEquals`)
5. Inspector sends 1-byte result (1=success, 0=failure)

**Key Management**:
- 256-bit shared secret (minimum 32 bytes)
- Auto-generated via `RandomNumberGenerator` if `WPFDEVTOOLS_AUTH_SECRET` environment variable is not set
- Environment variable accepts Base64-encoded secret
- Authentication has a 5-second timeout to prevent hung connections

### TLS 1.2 Encryption

All Named Pipe IPC can be encrypted using SslStream with self-signed X.509 certificates.

**Implementation Details**:
- **Algorithm**: RSA 2048-bit key, SHA-256 signature
- **Protocol**: TLS 1.2 (TLS 1.3 is incompatible with Named Pipes on Windows)
- **Certificate**: Self-signed, valid for 1 year, with Server Authentication EKU
- **Storage**: PFX file in `%APPDATA%\WpfDevTools\certs\server.pfx`
- **Password**: Random 32-byte password, protected via DPAPI (CurrentUser scope)
- **Client Validation**: Verifies server certificate subject contains `CN=WpfDevTools-Inspector`

**Forward Compatibility**: Authentication and encryption are optional. Connections without authentication/encryption remain supported for backward compatibility.

### Named Pipe ACL Restrictions

Named Pipes are created with explicit ACL rules:
- Current user: Full Control
- SYSTEM account: Full Control
- All other users: Denied

### Async File Logging

Logging uses non-blocking `Channel<T>` based async I/O:
- Bounded channel (10,000 entries max) with DropOldest overflow policy
- Background task processes log queue without blocking callers
- Automatic log rotation at 10 MB
- Graceful flush on dispose (5-second timeout)

### Rate Limiting

MCP Server STDIN transport includes global rate limiting:
- Default: 100 requests per minute
- Configurable via `RateLimiter` class
- Returns JSON-RPC error response when exceeded

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
- Challenge-Response authentication (HMAC-SHA256)
- TLS 1.2 encryption via SslStream (optional)
- Local-only communication (no network access)
- Message size limits (10 MB max)
- Timeout protection on all operations
- Rate limiting (100 requests/minute default)

**Recommendations**:
- Set `WPFDEVTOOLS_AUTH_SECRET` environment variable in production
- Enable TLS encryption for sensitive environments
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

**Last Updated**: 2026-03-06
