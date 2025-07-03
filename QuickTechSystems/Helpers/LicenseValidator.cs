using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace QuickTechSystems.WPF.Helpers
{
    public static class LicenseValidator
    {
        // File will be stored in the application's main directory
        private static readonly string LicenseFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "resource.index"
        );

        // Get the first active MAC address (non-loopback, 6 bytes)
        public static string GetCurrentMacAddress()
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.GetPhysicalAddress().GetAddressBytes().Length == 6
                )
                .Select(n => BitConverter.ToString(n.GetPhysicalAddress().GetAddressBytes()))
                .FirstOrDefault() ?? string.Empty;
        }

        // Check if the license file exists
        public static bool IsLicenseFileExists() => File.Exists(LicenseFilePath);

        // Save the MAC address to the hidden file
        public static void SaveMacAddress(string macAddress)
        {
            var json = JsonSerializer.Serialize(new { MacAddress = macAddress });
            File.WriteAllText(LicenseFilePath, json);

            // Hide the file in Windows Explorer
            File.SetAttributes(LicenseFilePath, FileAttributes.Hidden);
        }

        // Load the saved MAC address from the hidden file
        public static string? LoadSavedMacAddress()
        {
            if (!IsLicenseFileExists())
                return null;

            var json = File.ReadAllText(LicenseFilePath);
            var data = JsonSerializer.Deserialize<LicenseData>(json);
            return data?.MacAddress;
        }

        // Internal structure for JSON parsing
        private class LicenseData
        {
            public string? MacAddress { get; set; }
        }
    }
}
