using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SnapActions.Core;

internal static class BrowserNativeHost
{
    internal const string ExtensionOrigin = "chrome-extension://ccgckebadlhbplacbcjbfohpinbdoehh/";
    internal static string PipeName => $"SnapActions.Browser.{WindowsIdentity.GetCurrent().User!.Value}{Config.RuntimePaths.InstanceSuffix}";

    // Chrome supplies redirected stdio even though this is a Windows GUI executable.
    // This mode must run before the desktop app's single-instance check or UI initialization.
    internal static async Task RunAsync()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var stop = new CancellationTokenSource();
        try
        {
            var firstMessage = BrowserMessage.ReadAsync(input, stop.Token);
            var connection = pipe.ConnectAsync(5000, stop.Token);
            if (await Task.WhenAny(connection, firstMessage) == firstMessage && await firstMessage == null)
                return; // Only EOF ends the host; an early real message must be forwarded.
            await connection;

            async Task ForwardInput()
            {
                var message = await firstMessage;
                while (message != null)
                {
                    await BrowserMessage.WriteAsync(pipe, message, stop.Token);
                    message = await BrowserMessage.ReadAsync(input, stop.Token);
                }
            }

            async Task ForwardOutput()
            {
                while (await BrowserMessage.ReadAsync(pipe, stop.Token) is { } message)
                    await BrowserMessage.WriteAsync(output, message, stop.Token);
            }

            var incoming = ForwardInput();
            var outgoing = ForwardOutput();
            // Redirected console reads may ignore cancellation while the browser keeps stdin
            // open. Waiting for both relays after desktop EOF would strand this host and stop
            // the companion from reconnecting. Exit this dedicated process on either EOF.
            await await Task.WhenAny(incoming, outgoing);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException)
        {
            // Disconnects end this host. The extension reconnects without restarting the browser.
        }
        finally { stop.Cancel(); }
    }

    internal static uint FindBrowserProcess(uint hostProcessId)
    {
        try
        {
            using var host = Process.GetProcessById((int)hostProcessId);
            if (!string.Equals(host.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                return 0;
            uint current = hostProcessId;
            for (int depth = 0; depth < 4; depth++)
            {
                using var process = Process.GetProcessById((int)current);
                if (NtQueryInformationProcess(process.Handle, 0, out var info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
                    return 0;
                current = (uint)info.ParentProcessId.ToInt64();
                using var parent = Process.GetProcessById((int)current);
                if (parent.ProcessName is "brave" or "chrome" or "msedge") return current;
                // Some Chromium versions launch native hosts via cmd.exe.
                if (!string.Equals(parent.ProcessName, "cmd", StringComparison.OrdinalIgnoreCase)) return 0;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1, PebBaseAddress, Reserved2, Reserved3, ProcessId, ParentProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass,
        out ProcessBasicInformation information, int length, out int returnLength);
}
