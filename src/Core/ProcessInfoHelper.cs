using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace LinkToBrowser.Core
{
    public static class ProcessInfoHelper
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessBasicInformationClass = 0;
        private const int ProcessCommandLineInformationClass = 60;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
        private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
        private const int StatusBufferOverflow = unchecked((int)0x80000005);

        private static readonly HashSet<string> ShellWrapperExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe",
            "svchost.exe",
            "cmd.exe",
            "powershell.exe",
            "pwsh.exe",
            "conhost.exe",
            "rundll32.exe",
            "dllhost.exe",
            "runtimebroker.exe",
            "openwith.exe",
            "searchhost.exe",
            "startmenuexperiencehost.exe",
            "shellexperiencehost.exe",
            "sihost.exe",
            "taskhostw.exe",
            "applicationframehost.exe"
        };

        public static bool TryReadCommandLineFast(int pid, out string commandLine)
        {
            commandLine = string.Empty;
            if (pid <= 0)
            {
                return false;
            }

            var handle = IntPtr.Zero;
            var buffer = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                var bufferSize = 1024;
                buffer = Marshal.AllocHGlobal(bufferSize);
                var status = NtQueryInformationProcess(handle, ProcessCommandLineInformationClass, buffer, bufferSize, out var needed);

                if ((status == StatusInfoLengthMismatch || status == StatusBufferTooSmall || status == StatusBufferOverflow) &&
                    needed > bufferSize && needed <= 1024 * 1024)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = needed + 64;
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    status = NtQueryInformationProcess(handle, ProcessCommandLineInformationClass, buffer, bufferSize, out needed);
                }

                if (status != 0)
                {
                    return false;
                }

                var unicode = Marshal.PtrToStructure<UnicodeString>(buffer);
                if (unicode.Length == 0 || unicode.Buffer == IntPtr.Zero)
                {
                    commandLine = string.Empty;
                    return true;
                }

                commandLine = Marshal.PtrToStringUni(unicode.Buffer, unicode.Length / 2) ?? string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }

        public static Dictionary<int, string> QueryCommandLinesBatchWmi(IEnumerable<int> pids)
        {
            var result = new Dictionary<int, string>();
            var uniquePids = (pids ?? Enumerable.Empty<int>()).Where(p => p > 0).Distinct().ToList();
            if (uniquePids.Count == 0)
            {
                return result;
            }

            try
            {
                var filter = string.Join(" OR ", uniquePids.Select(p => $"ProcessId = {p}"));
                using var searcher = new ManagementObjectSearcher($"SELECT ProcessId, CommandLine FROM Win32_Process WHERE {filter}");
                using var collection = searcher.Get();
                foreach (ManagementBaseObject obj in collection)
                {
                    using (obj)
                    {
                        if (obj["ProcessId"] != null && int.TryParse(Convert.ToString(obj["ProcessId"]), out var pid))
                        {
                            result[pid] = obj["CommandLine"] as string ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        public static string QueryProcessExePath(uint pid)
        {
            if (pid == 0)
            {
                return string.Empty;
            }

            var handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
                if (handle == IntPtr.Zero)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder(1024);
                var size = sb.Capacity;
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        public static int GetParentProcessId(int pid)
        {
            if (pid <= 0)
            {
                return 0;
            }

            var handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
                if (handle == IntPtr.Zero)
                {
                    return 0;
                }

                var pbi = new ProcessBasicInformation();
                var status = NtQueryInformationProcess(
                    handle,
                    ProcessBasicInformationClass,
                    ref pbi,
                    Marshal.SizeOf<ProcessBasicInformation>(),
                    out _);

                return status == 0 ? pbi.InheritedFromUniqueProcessId.ToInt32() : 0;
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        public static string DetectSourceApp(string selfExePath)
        {
            try
            {
                var parentChain = new List<string>();
                var visited = new HashSet<int>();
                var currentPid = Process.GetCurrentProcess().Id;
                visited.Add(currentPid);

                for (var depth = 0; depth < 4; depth++)
                {
                    var parentPid = GetParentProcessId(currentPid);
                    if (parentPid <= 0 || !visited.Add(parentPid))
                    {
                        break;
                    }

                    var exe = QueryProcessExePath((uint)parentPid);
                    if (!string.IsNullOrWhiteSpace(exe))
                    {
                        parentChain.Add(exe);
                    }

                    currentPid = parentPid;
                }

                var foregroundExe = string.Empty;
                var fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fgHwnd, out var fgPid);
                    if (fgPid > 0)
                    {
                        foregroundExe = QueryProcessExePath(fgPid);
                    }
                }

                return SelectSourceApp(parentChain, foregroundExe, selfExePath);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string SelectSourceApp(IEnumerable<string> parentChainExes, string foregroundExe, string selfExePath)
        {
            var selfName = NormalizeExeFileName(selfExePath);

            if (parentChainExes != null)
            {
                foreach (var exe in parentChainExes)
                {
                    var name = NormalizeExeFileName(exe);
                    if (IsMeaningfulSourceApp(name, selfName))
                    {
                        return name;
                    }
                }
            }

            var fgName = NormalizeExeFileName(foregroundExe);
            if (IsMeaningfulSourceApp(fgName, selfName))
            {
                return fgName;
            }

            return string.Empty;
        }

        public static string NormalizeExeFileName(string exeOrName)
        {
            var raw = (exeOrName ?? string.Empty).Trim().Trim('"');
            if (raw.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileName(raw);
            }
            catch
            {
                return raw;
            }
        }

        private static bool IsMeaningfulSourceApp(string exeFileName, string selfExeFileName)
        {
            if (string.IsNullOrWhiteSpace(exeFileName))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(selfExeFileName) &&
                string.Equals(exeFileName, selfExeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !ShellWrapperExeNames.Contains(exeFileName);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            IntPtr processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref ProcessBasicInformation processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
