using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace QuickTechSystems.WPF.Helpers
{
    public static class LicenseValidator
    {
        // File will be stored in the application's main directory
        private static readonly string LicenseFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "resource.index"
        );

        // Get the current machine's GUID from Windows Registry
        public static string GetCurrentMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key != null)
                    {
                        var machineGuid = key.GetValue("MachineGuid")?.ToString();
                        return machineGuid ?? string.Empty;
                    }
                }
            }
            catch (Exception)
            {
                // If we can't access the registry, try alternative location
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false))
                    {
                        if (key != null)
                        {
                            var machineGuid = key.GetValue("MachineGuid")?.ToString();
                            return machineGuid ?? string.Empty;
                        }
                    }
                }
                catch
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        // Check if the license file exists
        public static bool IsLicenseFileExists() => File.Exists(LicenseFilePath);

        // Save the Machine GUID to the hidden file
        public static void SaveMachineGuid(string machineGuid)
        {
            try
            {
                // Ensure the directory exists
                var directory = Path.GetDirectoryName(LicenseFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // If file exists, remove read-only and hidden attributes
                if (File.Exists(LicenseFilePath))
                {
                    try
                    {
                        File.SetAttributes(LicenseFilePath, FileAttributes.Normal);
                    }
                    catch
                    {
                        // If we can't change attributes, try to delete the file
                        try
                        {
                            File.Delete(LicenseFilePath);
                        }
                        catch
                        {
                            // If we still can't delete, throw a clear error
                            throw new InvalidOperationException($"Cannot overwrite existing license file. Please delete the file manually:\n{LicenseFilePath}");
                        }
                    }
                }

                var licenseData = new LicenseData
                {
                    MachineGuid = machineGuid,
                    CreatedDate = DateTime.UtcNow,
                    Version = "1.0"
                };

                var json = JsonSerializer.Serialize(licenseData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Write the actual license file
                File.WriteAllText(LicenseFilePath, json);

                // Hide the file in Windows Explorer
                try
                {
                    File.SetAttributes(LicenseFilePath, FileAttributes.Hidden);
                }
                catch
                {
                    // If we can't hide the file, that's okay - the license still works
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Access denied. Please run the application as Administrator or delete the existing license file manually.\nFile: {LicenseFilePath}\nDetails: {ex.Message}");
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new InvalidOperationException($"Directory not found: {Path.GetDirectoryName(LicenseFilePath)}\nDetails: {ex.Message}");
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"File operation failed. The file might be in use by another process.\nFile: {LicenseFilePath}\nDetails: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save license information.\nError: {ex.GetType().Name}\nMessage: {ex.Message}\nPath: {LicenseFilePath}");
            }
        }

        // Load the saved Machine GUID from the hidden file
        public static string? LoadSavedMachineGuid()
        {
            if (!IsLicenseFileExists())
                return null;

            try
            {
                var json = File.ReadAllText(LicenseFilePath);
                var data = JsonSerializer.Deserialize<LicenseData>(json);
                return data?.MachineGuid;
            }
            catch (Exception ex)
            {
                // Log the error but don't crash - treat as no license
                System.Diagnostics.Debug.WriteLine($"Error reading license file: {ex.Message}");
                return null;
            }
        }

        // Validate if the current machine matches the licensed machine
        public static bool ValidateLicense()
        {
            var currentGuid = GetCurrentMachineGuid();
            var savedGuid = LoadSavedMachineGuid();

            if (string.IsNullOrWhiteSpace(currentGuid) || string.IsNullOrWhiteSpace(savedGuid))
                return false;

            return string.Equals(currentGuid, savedGuid, StringComparison.OrdinalIgnoreCase);
        }

        // Get license information for display purposes
        public static LicenseInfo? GetLicenseInfo()
        {
            if (!IsLicenseFileExists())
                return null;

            try
            {
                var json = File.ReadAllText(LicenseFilePath);
                var data = JsonSerializer.Deserialize<LicenseData>(json);

                if (data == null)
                    return null;

                return new LicenseInfo
                {
                    MachineGuid = data.MachineGuid,
                    CreatedDate = data.CreatedDate,
                    Version = data.Version,
                    IsValid = ValidateLicense()
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Remove license file (for deactivation)
        public static bool RemoveLicense()
        {
            try
            {
                if (IsLicenseFileExists())
                {
                    // Remove attributes first
                    File.SetAttributes(LicenseFilePath, FileAttributes.Normal);
                    File.Delete(LicenseFilePath);
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Get diagnostic information
        public static string GetDiagnosticInfo()
        {
            var info = $"License File Path: {LicenseFilePath}\n";
            info += $"Directory Exists: {Directory.Exists(Path.GetDirectoryName(LicenseFilePath))}\n";
            info += $"File Exists: {File.Exists(LicenseFilePath)}\n";
            info += $"Current Machine GUID: {GetCurrentMachineGuid()}\n";
            info += $"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}\n";

            if (File.Exists(LicenseFilePath))
            {
                try
                {
                    var attributes = File.GetAttributes(LicenseFilePath);
                    info += $"File Attributes: {attributes}\n";
                }
                catch (Exception ex)
                {
                    info += $"File Attributes: ERROR - {ex.Message}\n";
                }
            }

            try
            {
                var testPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "write_test.tmp");
                File.WriteAllText(testPath, "test");
                File.Delete(testPath);
                info += "Write Permission: OK\n";
            }
            catch (Exception ex)
            {
                info += $"Write Permission: FAILED - {ex.Message}\n";
            }

            return info;
        }

        // Internal structure for JSON serialization
        private class LicenseData
        {
            public string? MachineGuid { get; set; }
            public DateTime CreatedDate { get; set; }
            public string? Version { get; set; }
        }

        // Public structure for license information
        public class LicenseInfo
        {
            public string? MachineGuid { get; set; }
            public DateTime CreatedDate { get; set; }
            public string? Version { get; set; }
            public bool IsValid { get; set; }
        }
    }
}