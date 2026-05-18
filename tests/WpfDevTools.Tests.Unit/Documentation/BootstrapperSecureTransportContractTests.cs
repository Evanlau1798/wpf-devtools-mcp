using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BootstrapperSecureTransportContractTests
{
    [Fact]
    public void BootstrapEntry_ShouldCarrySecureTransportParametersThroughManagedBootstrap()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("authSecretFile",
            "the native bootstrapper should receive a secret file path instead of a raw secret in bootstrap parameters");
        content.Should().NotContain("pair.rfind(L\"authSecretBase64=\", 0)",
            "raw authentication secrets must not be accepted through the injected bootstrap argument string");
        content.Should().Contain("certDirectory",
            "the native bootstrapper must preserve the certificate directory when secure pipe TLS is enabled");
        content.Should().Contain("BuildManagedParams",
            "the managed inspector bootstrap needs the same secure transport parameters after the native hand-off");
        content.Should().Contain("ParseKeyValueParams",
            "secure transport bootstrap arguments are now carried as key=value pairs instead of the legacy two-token format");
    }

    [Fact]
    public void BootstrapperExitCodes_ShouldIncludeSpecificAuthSecretLoadFailure()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/exit_codes.h"));

        content.Should().MatchRegex(@"AuthSecretLoadFailed\s*=\s*0x15");
    }

    [Fact]
    public void BootstrapEntry_ShouldReturnAuthSecretLoadFailedWhenSecretFileCannotBeRead()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("if (!LoadAuthSecretFromFile(config))",
            "auth secret file load failures should have a dedicated bootstrap branch");
        content.Should().Contain("return ExitCodes::AuthSecretLoadFailed;",
            "auth secret file read failures should not be misclassified as inspector path failures");
    }

    [Fact]
    public void BootstrapEntry_ShouldLogAuthSecretFileDeletionFailure()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("SecureDeleteAuthSecretFile(config.AuthSecretFile)");
        content.Should().Contain("GetLastError()");
        content.Should().Contain("OutputDebugStringW");
        content.Should().NotContain("DeleteFileW(config.AuthSecretFile.c_str());",
            "auth secret file deletion failures should produce a diagnostic signal");
    }

    [Fact]
    public void BootstrapEntry_ShouldWipeAuthSecretFileBeforeDeleting()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));

        content.Should().Contain("static bool WipeFileContents(",
            "native cleanup should overwrite and truncate the temporary auth secret file before deletion");
        content.Should().Contain("FlushFileBuffers",
            "best-effort file wiping should flush the overwritten content before truncation/deletion");
        content.Should().Contain("SetEndOfFile",
            "best-effort cleanup should truncate the auth secret file so hard links do not retain plaintext bytes");
        content.Should().Contain("SecureDeleteAuthSecretFile(config.AuthSecretFile)",
            "auth secret cleanup should use the wiping delete helper after loading the secret");
    }

    [Fact]
    public void BootstrapEntry_ShouldLogAuthSecretWipeFailureBeforeDeleting()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));
        var method = GetMethodBody(content, "static void SecureDeleteAuthSecretFile");

        method.Should().Contain("if (!WipeFileContents(path))",
            "native cleanup must not silently ignore wipe/truncate failures before deleting the primary path");
        method.Should().Contain("LogAuthSecretFileCleanupFailure(L\"wipe\"",
            "native cleanup should emit a diagnostic when a hard-link mitigation wipe fails");
        method.IndexOf("WipeFileContents(path)", StringComparison.Ordinal)
            .Should().BeLessThan(method.IndexOf("DeleteFileW", StringComparison.Ordinal));
    }

    [Fact]
    public void BootstrapEntry_ShouldPreserveAuthSecretWipeSizeErrorCode()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));
        var method = GetMethodBody(content, "static bool WipeFileContents");
        var getFileSizeFailureBlock = GetBlockAfter(method, "if (!GetFileSizeEx(file, &fileSize))");

        getFileSizeFailureBlock.Should().Contain("DWORD errorCode = GetLastError();",
            "native cleanup diagnostics should preserve the GetFileSizeEx failure before CloseHandle can overwrite last-error state");
        getFileSizeFailureBlock.Should().Contain("SetLastError(errorCode");
        getFileSizeFailureBlock.IndexOf("GetLastError()", StringComparison.Ordinal)
            .Should().BeLessThan(getFileSizeFailureBlock.IndexOf("CloseHandle(file)", StringComparison.Ordinal));
    }

    [Fact]
    public void BootstrapEntry_ShouldUseDeterministicErrorForAuthSecretPartialWipe()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));
        var method = GetMethodBody(content, "static bool WipeFileContents");

        method.Should().NotContain("|| bytesWritten != bytesToWrite",
            "partial WriteFile success does not guarantee GetLastError contains the current failure");
        method.Should().Contain("if (bytesWritten != bytesToWrite)");
        var partialWriteBlock = GetBlockAfter(method, "if (bytesWritten != bytesToWrite)");

        partialWriteBlock.Should().NotContain("recordLastErrorFailure(",
            "short writes should not call any helper that reads stale Win32 last-error state");
        partialWriteBlock.Should().NotContain("GetLastError()",
            "short writes should not call the last-error helper because WriteFile can succeed without setting a fresh error");
        partialWriteBlock.Should().Contain("recordDeterministicFailure(ERROR_WRITE_FAULT);",
            "short writes should report a deterministic fallback error instead of reading stale last-error state");
    }

    [Fact]
    public void BootstrapEntry_ShouldUseFailClosedJsonConfigParser()
    {
        var entryContent = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));
        var parserContent = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_config_parser.cpp"));
        var projectContent = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        entryContent.Should().NotContain("findValue",
            "the temp-file bootstrap fallback should not locate JSON fields with substring scanning");
        entryContent.Should().Contain("TryParseBootstrapConfigJson",
            "the fallback config file should be parsed as a JSON object before values are applied");
        parserContent.Should().Contain("ParseJsonString",
            "string values should be parsed with JSON escape and delimiter semantics");
        parserContent.Should().Contain("MB_ERR_INVALID_CHARS",
            "malformed UTF-8 config files should fail closed instead of being lossy-decoded");
        parserContent.Should().Contain("case 'u'",
            "JSON unicode escapes should be handled explicitly");
        projectContent.Should().Contain("bootstrap_config_parser.cpp",
            "the native parser implementation must be compiled into the bootstrapper");
    }

    [Fact]
    public void BootstrapperSecretBuffers_ShouldBeSecurelyWipedAfterManagedHandoff()
    {
        var wipeHeaderPath = GetRepoFilePath("src/WpfDevTools.Bootstrapper/secure_memory.h");
        File.Exists(wipeHeaderPath).Should().BeTrue(
            "secret-bearing native buffers should share one audited wipe helper instead of ad hoc cleanup");

        var wipeHeader = File.ReadAllText(wipeHeaderPath);
        var entryContent = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/bootstrap_entry.cpp"));
        var coreClrContent = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/coreclr_hosting.cpp"));
        var projectContent = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        wipeHeader.Should().Contain("SecureZeroMemory",
            "the compiler must not optimize away secret buffer wiping");
        wipeHeader.Should().Contain("SecureWipeString");
        wipeHeader.Should().Contain("SecureWipeBuffer");
        projectContent.Should().Contain("secure_memory.h");

        entryContent.Should().Contain("#include \"secure_memory.h\"");
        entryContent.Should().Contain("SecureWipeString(content)");
        entryContent.Should().Contain("SecureWipeString(secret)");
        entryContent.Should().Contain("SecureWipeString(config.AuthSecretBase64)");
        entryContent.Should().Contain("SecureWipeString(managedParams)");
        entryContent.Should().Contain("ReserveManagedParamsCapacity(result, config)",
            "managed parameter construction should reserve capacity before copying the secret to avoid allocator-retained stale copies");
        entryContent.Should().NotContain("return HostNetFramework(config.InspectorPath.c_str(), managedParams.c_str());");
        entryContent.Should().NotContain("return HostNetCore(config.InspectorPath.c_str(), managedParams.c_str());");
        GetBuildManagedParamsSecretTail(entryContent).Should().NotContain("AppendParam",
            "authSecretBase64 should be the final managed parameter appended so no later growth can copy the secret into an allocator-retained old buffer");

        coreClrContent.Should().Contain("#include \"secure_memory.h\"");
        coreClrContent.Should().Contain("SecureWipeBuffer(utf8Buf.get()");
    }

    private static string GetBuildManagedParamsSecretTail(string entryContent)
    {
        var methodStart = entryContent.IndexOf("static std::wstring BuildManagedParams", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);

        var methodEnd = entryContent.IndexOf("static bool ParseLegacyParams", methodStart, StringComparison.Ordinal);
        methodEnd.Should().BeGreaterThan(methodStart);

        var method = entryContent[methodStart..methodEnd];
        const string secretAppendMarker = "AppendParam(result, L\"authSecretBase64\"";
        var secretAppend = method.IndexOf(secretAppendMarker, StringComparison.Ordinal);
        secretAppend.Should().BeGreaterThanOrEqualTo(0);

        var secretAppendLineEnd = method.IndexOf('\n', secretAppend);
        secretAppendLineEnd.Should().BeGreaterThan(secretAppend);

        return method[secretAppendLineEnd..];
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string GetMethodBody(string content, string signature)
    {
        var start = content.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var braceStart = content.IndexOf('{', start);
        braceStart.Should().BeGreaterThan(start);

        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[braceStart..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not locate method body for {signature}.");
    }

    private static string GetBlockAfter(string content, string marker)
    {
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var braceStart = content.IndexOf('{', start);
        braceStart.Should().BeGreaterThan(start);

        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[braceStart..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not locate block for {marker}.");
    }
}
