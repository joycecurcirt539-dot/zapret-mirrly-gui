using System;
using System.Runtime.InteropServices;

namespace ZapretMirrlyGUI.Services;

public static class Win32ServiceManager
{
    // Access Rights
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;

    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;

    // Service Types & Start Options
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_AUTO_START = 0x00000002;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;

    // Controls
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    // Service Status Codes
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_RUNNING = 0x00000004;

    private const uint SERVICE_CONFIG_DESCRIPTION = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTION
    {
        public string lpDescription;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr lpdwTagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr hService, uint dwInfoLevel, ref SERVICE_DESCRIPTION lpInfo);

    public static bool IsServiceInstalled(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;

        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc != IntPtr.Zero)
            {
                CloseServiceHandle(svc);
                return true;
            }
            return false;
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static string GetServiceStatusName(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return "Не установлена";

        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return "Не установлена";

            try
            {
                SERVICE_STATUS status = new();
                if (QueryServiceStatus(svc, ref status))
                {
                    return status.dwCurrentState switch
                    {
                        SERVICE_RUNNING => "RUNNING",
                        SERVICE_START_PENDING => "START_PENDING",
                        SERVICE_STOP_PENDING => "STOP_PENDING",
                        SERVICE_STOPPED => "Отключен",
                        _ => "Не установлена"
                    };
                }
                return "Не установлена";
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool CreateWinwsService(string serviceName, string displayName, string binaryPathWithArgs, string description)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) return false;

        try
        {
            IntPtr svc = CreateService(
                scm,
                serviceName,
                displayName,
                SERVICE_ALL_ACCESS,
                SERVICE_WIN32_OWN_PROCESS,
                SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL,
                binaryPathWithArgs,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (svc == IntPtr.Zero) return false;

            try
            {
                if (!string.IsNullOrEmpty(description))
                {
                    SERVICE_DESCRIPTION sd = new() { lpDescription = description };
                    ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref sd);
                }
                return true;
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool StartWin32Service(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;

        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_START | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;

            try
            {
                return StartService(svc, 0, IntPtr.Zero);
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool StopWin32Service(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;

        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;

            try
            {
                SERVICE_STATUS status = new();
                return ControlService(svc, SERVICE_CONTROL_STOP, ref status);
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool RemoveWin32Service(string serviceName)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) return false;

        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero) return false;

            try
            {
                SERVICE_STATUS status = new();
                ControlService(svc, SERVICE_CONTROL_STOP, ref status);
                return DeleteService(svc);
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }
}
