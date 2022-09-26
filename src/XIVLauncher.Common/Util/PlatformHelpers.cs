using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace XIVLauncher.Common.Util;

public static class PlatformHelpers
{
    public static Platform GetPlatform()
    {
        if (EnvironmentSettings.IsWine)
            return Platform.Win32OnLinux;

        // TODO(goat): Add mac here, once it's merged

        return Platform.Win32;
    }

    /// <summary>
    ///     Generates a temporary file name.
    /// </summary>
    /// <returns>A temporary file name that is almost guaranteed to be unique.</returns>
    public static string GetTempFileName()
    {
        // https://stackoverflow.com/a/50413126
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    }

    public static void OpenBrowser(string url)
    {
        // https://github.com/dotnet/corefx/issues/10361
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            url = url.Replace("&", "^&");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    public static bool IsElevated()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Win32NT:
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

            case PlatformID.Unix:
                return geteuid() == 0;

            default:
                return false;
        }
    }

    public static void Untar(string path, string output)
    {
        var psi = new ProcessStartInfo("tar")
        {
            Arguments = $"-xf \"{path}\" -C \"{output}\""
        };

        var tarProcess = Process.Start(psi);

        if (tarProcess == null)
            throw new Exception("Could not start tar.");

        tarProcess.WaitForExit();

        if (tarProcess.ExitCode != 0)
            throw new Exception("Could not untar.");
    }

    public static void Un7za(string path, string output)
    {
        var sevenzaPath = Path.Combine(Paths.ResourcesPath, "7za.exe");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Log.Information("[DUPDATE] Extracting 7z dalamud slowly.");

            using (var archive = ArchiveFactory.Open(path))
            {
                var reader = archive.ExtractAllEntries();

                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory)
                        reader.WriteEntryToDirectory(output, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });
                }
            }

            Log.Information("[DUPDATE] Extracting finished.");
            return;
        }

        var psi = new ProcessStartInfo(sevenzaPath)
        {
            Arguments = $"x -y \"{path}\" -o\"{output}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var tarProcess = Process.Start(psi);
        var outputLines = tarProcess.StandardOutput.ReadToEnd();
        if (tarProcess == null)
            throw new BadImageFormatException("Could not start 7za.");

        tarProcess.WaitForExit();
        if (tarProcess.ExitCode != 0)
            throw new FormatException($"Could not un7z.\n{outputLines}");
    }

    private static readonly IPEndPoint DefaultLoopbackEndpoint = new(IPAddress.Loopback, port: 0);

    public static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        socket.Bind(DefaultLoopbackEndpoint);
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

#if WIN32
    /*
     * WINE: The APIs DriveInfo uses are buggy on Wine. Let's just use the kernel32 API instead.
     */

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
                                                 out ulong lpFreeBytesAvailable,
                                                 out ulong lpTotalNumberOfBytes,
                                                 out ulong lpTotalNumberOfFreeBytes);

    public static long GetDiskFreeSpace(DirectoryInfo info)
    {
        if (info == null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        ulong dummy = 0;

        if (!GetDiskFreeSpaceEx(info.Root.FullName, out ulong freeSpace, out dummy, out dummy))
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }

        return (long)freeSpace;
    }
#else
        public static long GetDiskFreeSpace(DirectoryInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            DriveInfo drive = new DriveInfo(info.FullName);

            return drive.AvailableFreeSpace;
        }
#endif
}