var childMarker = Environment.GetEnvironmentVariable("WPFDEVTOOLS_TEST_CHILD_MARKER")
    ?? throw new InvalidOperationException("Missing child marker.");
var childStartedPath = Environment.GetEnvironmentVariable("WPFDEVTOOLS_TEST_CHILD_STARTED_PATH")
    ?? throw new InvalidOperationException("Missing child-started path.");
var markerLiteral = "'" + childMarker.Replace("'", "''", StringComparison.Ordinal) + "'";
var childCode = "$childMarker = " + markerLiteral + "; Start-Sleep -Seconds 120";
var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
{
    UseShellExecute = false,
    CreateNoWindow = true,
};
startInfo.ArgumentList.Add("-NoProfile");
startInfo.ArgumentList.Add("-ExecutionPolicy");
startInfo.ArgumentList.Add("Bypass");
startInfo.ArgumentList.Add("-Command");
startInfo.ArgumentList.Add(childCode);
System.Diagnostics.Process.Start(startInfo);
File.WriteAllText(childStartedPath, "started");
Thread.Sleep(TimeSpan.FromSeconds(120));
