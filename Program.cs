using System;
using System.Drawing;
using System.Windows.Forms;
using DohCloudflare;

public static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            if (!DohManager.IsRunningAsAdministrator())
            {
                ShowErrorMessage("Требуются права администратора", "Ошибка");
                return;
            }
            
            if (!DohManager.IsWindows11OrHigher())
            {
                ShowErrorMessage("Требуется Windows 11", "Ошибка");
                return;
            }
            
            string[]? currentDnsServers = DohManager.GetCurrentDNSServers();
            bool dohConfigured = DohManager.CheckDohRegistryExists();
            bool shouldReset = DohManager.IsDnsConfigured(currentDnsServers);
            
            if (shouldReset)
            {
                DohManager.ResetToDHCP();
                DohManager.CleanDohRegistry();
                
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
                DohManager.SetOurDNS();
                
                if (!dohConfigured)
                {
                    DohManager.ConfigureDohRegistry();
                }
                
                ShowStyledMessage("Защита включена", "Зашифрованное соединение настроено!", MessageType.Success);
            }
            
            DohManager.FlushDns();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Ошибка: {ex.Message}", "Ошибка выполнения");
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
                MessageBoxButtons.OK,
                type == MessageType.Error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }
    }
    
    private static void ShowErrorMessage(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}