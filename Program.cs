using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Drawing;
using System.Security.Principal;

public static class Program
{
    private const string DOH_TEMPLATE = "https://cloudflare-dns.com/dns-query";
    private static readonly string[] OUR_DNS_SERVERS = { "1.1.1.1", "1.0.0.1" };
    
    private const string REGISTRY_PATH_DNSCACHE = @"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters";
    private const string REGISTRY_PATH_TCPIP_PARAMS = @"System\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const int WINDOWS_11_BUILD = 22000;
    
    [STAThread]
    static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            if (!IsRunningAsAdministrator())
            {
                ShowErrorMessage("Требуются права администратора", "Ошибка");
                return;
            }
            
            if (!IsWindows11OrHigher())
            {
                ShowErrorMessage("Требуется Windows 11", "Ошибка");
                return;
            }
            
            string[]? currentDnsServers = GetCurrentDNSServers();
            bool dohConfigured = CheckDohRegistryExists();
            bool shouldReset = currentDnsServers != null && currentDnsServers.SequenceEqual(OUR_DNS_SERVERS);
            
            if (shouldReset)
            {
                ResetToDHCP();
                CleanDohRegistry();
                
                if (dohConfigured)
                {
                    ShowStyledMessage("Защита отключена", "Зашифрованное соединение отключено!", MessageType.Error);
                }
                else
                {
                    ShowStyledMessage("Информация", "DNS сброшены на DHCP", MessageType.Info);
                }
            }
            else
            {
                SetOurDNS();
                
                if (!dohConfigured)
                {
                    ConfigureDohRegistry();
                }
                
                ShowStyledMessage("Защита включена", "Зашифрованное соединение настроено!", MessageType.Success);
            }
            
            RunCommand("ipconfig", "/flushdns");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Ошибка: {ex.Message}", "Ошибка выполнения");
        }
    }
    
    private static bool IsRunningAsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
    
    private static bool IsWindows11OrHigher()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            if (version.Major >= 10 && version.Build >= WINDOWS_11_BUILD)
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
                        return buildNumber >= WINDOWS_11_BUILD;
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
    
    private static string[]? GetCurrentDNSServers()
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
    
    private static void ResetToDHCP()
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
                        inParamsReset["DNSServerSearchOrder"] = new string[0];
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
    
    private static void SetOurDNS()
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
                        inParams["DNSServerSearchOrder"] = OUR_DNS_SERVERS;
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
    
    private static List<string> GetPhysicalInterfaceGuids()
    {
        List<string> guids = new List<string>();
        try
        {
            using (RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(REGISTRY_PATH_TCPIP_PARAMS))
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
    
    private static bool CheckDohRegistryExists()
    {
        List<string> physicalInterfaces = GetPhysicalInterfaceGuids();
        if (physicalInterfaces.Count == 0) return false;
        
        bool allDnsConfigured = false;
        
        foreach (string interfaceGuid in physicalInterfaces)
        {
            bool ip1Exists = false;
            bool ip2Exists = false;
            
            foreach (string dnsServer in OUR_DNS_SERVERS)
            {
                string dohServerPath = REGISTRY_PATH_DNSCACHE + "\\" + interfaceGuid + 
                                       "\\DohInterfaceSettings\\Doh\\" + dnsServer;
                
                try
                {
                    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(dohServerPath))
                    {
                        if (key != null)
                        {
                            string? template = key.GetValue("DohTemplate") as string;
                            if (template == DOH_TEMPLATE)
                            {
                                if (dnsServer == OUR_DNS_SERVERS[0]) ip1Exists = true;
                                if (dnsServer == OUR_DNS_SERVERS[1]) ip2Exists = true;
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
    
    private static void ConfigureDohRegistry()
    {
        List<string> physicalInterfaces = GetPhysicalInterfaceGuids();
        if (physicalInterfaces.Count == 0)
        {
            throw new Exception("Не найдены сетевые интерфейсы");
        }
        
        foreach (string interfaceGuid in physicalInterfaces)
        {
            string basePath = REGISTRY_PATH_DNSCACHE + "\\" + interfaceGuid;
            
            foreach (string dnsServer in OUR_DNS_SERVERS)
            {
                string dohServerPath = basePath + "\\DohInterfaceSettings\\Doh\\" + dnsServer;
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(dohServerPath, true))
                    {
                        key.SetValue("DohFlags", 2UL, RegistryValueKind.QWord);
                        key.SetValue("DohTemplate", DOH_TEMPLATE, RegistryValueKind.String);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ошибка настройки DoH: {ex.Message}");
                }
            }
        }
    }
    
	private static void CleanDohRegistry()
	{
	    List<string> physicalInterfaces = GetPhysicalInterfaceGuids();
	    if (physicalInterfaces.Count == 0) return;
	    
	    foreach (string interfaceGuid in physicalInterfaces)
	    {
	        string dohPath = REGISTRY_PATH_DNSCACHE + "\\" + interfaceGuid + "\\DohInterfaceSettings\\Doh";
	        
	        foreach (string dnsServer in OUR_DNS_SERVERS)
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
	                    
	                    string dohInterfacePath = REGISTRY_PATH_DNSCACHE + "\\" + interfaceGuid + "\\DohInterfaceSettings";
	                    using (RegistryKey? dohInterfaceKey = Registry.LocalMachine.OpenSubKey(dohInterfacePath, false))
	                    {
	                        if (dohInterfaceKey != null && dohInterfaceKey.GetSubKeyNames().Length == 0)
	                        {
	                            Registry.LocalMachine.DeleteSubKeyTree(dohInterfacePath, false);
	                            
	                            string interfacePath = REGISTRY_PATH_DNSCACHE + "\\" + interfaceGuid;
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
    
    private enum MessageType { Success, Error, Info }
    
    private static void ShowStyledMessage(string title, string message, MessageType type)
    {
        try
        {
            Color bgColor = type switch
            {
                MessageType.Success => Color.FromArgb(0x27, 0xae, 0x60),
                MessageType.Error => Color.FromArgb(0xe7, 0x4c, 0x3c),
                MessageType.Info => Color.FromArgb(0x34, 0x98, 0xdb),
                _ => Color.FromArgb(0x34, 0x98, 0xdb)
            };
            
            Form messageForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = bgColor,
                TopMost = true,
                Size = new Size(400, 120),
                ShowInTaskbar = false
            };
            
            Label messageLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = message,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            messageForm.Controls.Add(messageLabel);
            
            System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            closeTimer.Tick += (s, e) => 
            {
                closeTimer.Stop();
                messageForm.Close();
            };
            
            closeTimer.Start();
            
            messageForm.ShowDialog();
        }
        catch
        {
            MessageBox.Show(message, title, 
                type == MessageType.Error ? MessageBoxButtons.OK : MessageBoxButtons.OK,
                type == MessageType.Error ? MessageBoxIcon.Error : 
                type == MessageType.Success ? MessageBoxIcon.Information : MessageBoxIcon.Information);
        }
    }
    
    private static void ShowErrorMessage(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    
    private static void RunCommand(string command, string arguments)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            
            using (Process? process = Process.Start(psi))
            {
                process?.WaitForExit();
            }
        }
        catch
        {
        }
    }
}