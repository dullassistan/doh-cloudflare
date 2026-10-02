using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace DohCloudflare
{
    public static class DohManager
    {
        public const string DohTemplate = "https://cloudflare-dns.com/dns-query";
        public static readonly string[] DnsServers = { "1.1.1.1", "1.0.0.1" };

        public const string RegistryPathDnscache = @"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters";
        public const string RegistryPathTcpipParams = @"System\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        public const int Windows11Build = 22000;

        public static bool IsDnsConfigured(string[]? currentDnsServers)
        {
            if (currentDnsServers == null || currentDnsServers.Length == 0) return false;
            return currentDnsServers.SequenceEqual(DnsServers);
        }

        public static bool IsRunningAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool IsWindows11OrHigher(int? overrideBuild = null)
        {
            if (overrideBuild.HasValue)
            {
                return overrideBuild.Value >= Windows11Build;
            }

            try
            {
                var version = Environment.OSVersion.Version;
                if (version.Major >= 10 && version.Build >= Windows11Build)
                {
                    return true;
                }

                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object? buildNumberObj = key.GetValue("CurrentBuildNumber", 0);
                        if (buildNumberObj != null)
                        {
                            int buildNumber = Convert.ToInt32(buildNumberObj);
                            return buildNumber >= Windows11Build;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        public static string[]? GetCurrentDNSServers()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\cimv2",
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"))
                {
                    foreach (ManagementObject objItem in searcher.Get().Cast<ManagementObject>())
                    {
                        string[]? dnsServers = objItem["DNSServerSearchOrder"] as string[];
                        if (dnsServers != null && dnsServers.Length > 0)
                        {
                            return dnsServers;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public static void ResetToDHCP()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\cimv2",
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"))
                {
                    foreach (ManagementObject objItem in searcher.Get().Cast<ManagementObject>())
                    {
                        try
                        {
                            ManagementBaseObject inParamsReset = objItem.GetMethodParameters("SetDNSServerSearchOrder");
                            inParamsReset["DNSServerSearchOrder"] = Array.Empty<string>();
                            objItem.InvokeMethod("SetDNSServerSearchOrder", inParamsReset, null);

                            ManagementBaseObject inParamsReg = objItem.GetMethodParameters("SetDynamicDNSRegistration");
                            inParamsReg["FullDNSRegistrationEnabled"] = true;
                            objItem.InvokeMethod("SetDynamicDNSRegistration", inParamsReg, null);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сброса DNS: {ex.Message}");
            }
        }

        public static void SetOurDNS()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\cimv2",
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"))
                {
                    foreach (ManagementObject objItem in searcher.Get().Cast<ManagementObject>())
                    {
                        try
                        {
                            ManagementBaseObject inParams = objItem.GetMethodParameters("SetDNSServerSearchOrder");
                            inParams["DNSServerSearchOrder"] = DnsServers;
                            objItem.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка установки DNS: {ex.Message}");
            }
        }

        public static List<string> GetPhysicalInterfaceGuids()
        {
            List<string> guids = new List<string>();
            try
            {
                using (RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(RegistryPathTcpipParams))
                {
                    if (baseKey != null)
                    {
                        foreach (string subKeyName in baseKey.GetSubKeyNames())
                        {
                            using (RegistryKey? subKey = baseKey.OpenSubKey(subKeyName))
                            {
                                if (subKey != null &&
                                    subKey.GetValueNames().Any(name =>
                                        name.Equals("DhcpGatewayHardware", StringComparison.OrdinalIgnoreCase)))
                                {
                                    guids.Add(subKeyName);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return guids;
        }

        public static bool CheckDohRegistryExists()
        {
            List<string> physicalInterfaces = GetPhysicalInterfaceGuids();
            if (physicalInterfaces.Count == 0) return false;

            bool allDnsConfigured = false;

            foreach (string interfaceGuid in physicalInterfaces)
            {
                bool ip1Exists = false;
                bool ip2Exists = false;

                foreach (string dnsServer in DnsServers)
                {
                    string dohServerPath = RegistryPathDnscache + "\\" + interfaceGuid +
                                           "\\DohInterfaceSettings\\Doh\\" + dnsServer;

                    try
                    {
                        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(dohServerPath))
                        {
                            if (key != null)
                            {
                                string? template = key.GetValue("DohTemplate") as string;
                                if (template == DohTemplate)
                                {
                                    if (dnsServer == DnsServers[0]) ip1Exists = true;
                                    if (dnsServer == DnsServers[1]) ip2Exists = true;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (ip1Exists && ip2Exists)
                {
                    allDnsConfigured = true;
                    break;
                }
            }

            return allDnsConfigured;
        }

        public static void ConfigureDohRegistry()
        {
            List<string> physicalInterfaces = GetPhysicalInterfaceGuids();
            if (physicalInterfaces.Count == 0)
            {
                throw new Exception("Не найдены сетевые интерфейсы");
            }

            foreach (string interfaceGuid in physicalInterfaces)
            {
                string basePath = RegistryPathDnscache + "\\" + interfaceGuid;

                foreach (string dnsServer in DnsServers)
                {
                    string dohServerPath = basePath + "\\DohInterfaceSettings\\Doh\\" + dnsServer;
                    try
                    {
                        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(dohServerPath, true))
                        {
                            key.SetValue("DohFlags", 2UL, RegistryValueKind.QWord);
                            key.SetValue("DohTemplate", DohTemplate, RegistryValueKind.String);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Ошибка настройки DoH в реестре для {dnsServer}: {ex.Message}");
                    }
                }
            }
        }

        public static void CleanDohRegistry()
        {
            List<string> physicalInterfaces = GetPhysicalInterfaceGuids();
            if (physicalInterfaces.Count == 0) return;

            foreach (string interfaceGuid in physicalInterfaces)
            {
                string dohPath = RegistryPathDnscache + "\\" + interfaceGuid + "\\DohInterfaceSettings\\Doh";

                foreach (string dnsServer in DnsServers)
                {
                    try
                    {
                        string dohServerPath = dohPath + "\\" + dnsServer;
                        Registry.LocalMachine.DeleteSubKeyTree(dohServerPath, false);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    using (RegistryKey? dohKey = Registry.LocalMachine.OpenSubKey(dohPath, false))
                    {
                        if (dohKey != null && dohKey.GetSubKeyNames().Length == 0)
                        {
                            Registry.LocalMachine.DeleteSubKeyTree(dohPath, false);

                            string dohInterfacePath = RegistryPathDnscache + "\\" + interfaceGuid + "\\DohInterfaceSettings";
                            using (RegistryKey? dohInterfaceKey = Registry.LocalMachine.OpenSubKey(dohInterfacePath, false))
                            {
                                if (dohInterfaceKey != null && dohInterfaceKey.GetSubKeyNames().Length == 0)
                                {
                                    Registry.LocalMachine.DeleteSubKeyTree(dohInterfacePath, false);

                                    string interfacePath = RegistryPathDnscache + "\\" + interfaceGuid;
                                    using (RegistryKey? interfaceKey = Registry.LocalMachine.OpenSubKey(interfacePath, false))
                                    {
                                        if (interfaceKey != null && interfaceKey.GetSubKeyNames().Length == 0)
                                        {
                                            Registry.LocalMachine.DeleteSubKeyTree(interfacePath, false);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public static void FlushDns()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch
            {
            }
        }
    }
}
