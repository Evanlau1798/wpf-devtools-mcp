namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    private static void DeleteTempRootWithRetry(string tempRoot)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                DeleteTempRoot(tempRoot);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(250);
            }
        }
    }
}
