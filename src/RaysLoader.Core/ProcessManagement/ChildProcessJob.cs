using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RaysLoader.ProcessManagement;

/// <summary>
/// Ties launched tools to the lifetime of Ray's Loader. Every process assigned
/// here, and every child it starts (yt-dlp launches FFmpeg), is terminated by
/// Windows when the last handle to the job closes, which happens automatically
/// when Ray's Loader exits or crashes.
/// </summary>
internal static class ChildProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private static readonly Lazy<IntPtr> Job = new(CreateJob, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Returns false when the process could not be assigned (non-Windows, exited, or denied).</summary>
    public static bool TryAssign(Process process)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var job = Job.Value;
        if (job == IntPtr.Zero) return false;
        try
        {
            return AssignProcessToJobObject(job, process.Handle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited before it could be assigned.
            return false;
        }
    }

    internal static bool IsAssigned(Process process) =>
        OperatingSystem.IsWindows() &&
        Job.Value != IntPtr.Zero &&
        IsProcessInJob(process.Handle, Job.Value, out var result) &&
        result;

    private static IntPtr CreateJob()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var information = new JobObjectExtendedLimitInformationData
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };
        var length = Marshal.SizeOf<JobObjectExtendedLimitInformationData>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, pointer, false);
            if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, pointer, (uint)length)) return job;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        Debug.WriteLine($"Could not configure the child-process job (Win32 error {Marshal.GetLastWin32Error()}).");
        CloseHandle(job);
        return IntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationData
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
