using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management; // For WMI
using Microsoft.Win32; // For registry
using System.Threading;

namespace ToggleDeviceSounds
{
    internal class Program
    {
        // Device and sound file paths
        const string DeviceId = "XYM1564";
        const string DeviceInsertedFileName = @"C:\Windows\Media\Windows Hardware Insert.wav";
        const string DeviceRemovedFileName = @"C:\Windows\Media\Windows Hardware Remove.wav";

        static Action<string> Log;
        static StreamWriter logWriter = null;

        static void Main(string[] args)
        {
            // Ensure only one instance is running
            bool createdNew;
            string mutexName = "ToggleDeviceSounds_Mutex";
            using (var mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Another instance is already running
                    Console.WriteLine($"{DateTime.Now} - Another instance of ToggleDeviceSounds is already running. Exiting.");
                    return;
                }

                // Parse arguments
                string deviceInsertedFileName = DeviceInsertedFileName;
                string deviceRemovedFileName = DeviceRemovedFileName;
                string logFilePath = null;
                Queue<string> logBuffer = new Queue<string>(10);

                foreach (var arg in args)
                {
                    if (arg.StartsWith("-deviceInsertedFileName=", StringComparison.OrdinalIgnoreCase))
                    {
                        deviceInsertedFileName = arg.Substring("-deviceInsertedFileName=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("-deviceRemovedFileName=", StringComparison.OrdinalIgnoreCase))
                    {
                        deviceRemovedFileName = arg.Substring("-deviceRemovedFileName=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("-logFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        logFilePath = arg.Substring("-logFile=".Length).Trim('"');
                    }
                }

                // Setup logging
                if (!string.IsNullOrWhiteSpace(logFilePath))
                {
                    if (Directory.Exists(logFilePath))
                    {
                        string fileName = $"ToggleDeviceSounds-{DateTime.Now:yyyyMMdd-HHmmss}.log";
                        logFilePath = Path.Combine(logFilePath, fileName);

                        // Keep only the last 10 log files in the directory
                        var dirInfo = new DirectoryInfo(Path.GetDirectoryName(logFilePath));
                        var logFiles = dirInfo.GetFiles("ToggleDeviceSounds-*.log")
                            .OrderByDescending(f => f.CreationTime)
                            .ToList();
                        if (logFiles.Count > 10)
                        {
                            foreach (var file in logFiles.Skip(10))
                            {
                                try { file.Delete(); } catch { /* ignore */ }
                            }
                        }
                    }
                    logWriter = new StreamWriter(logFilePath, false) { AutoFlush = true };
                    Log = msg =>
                    {
                        string logMsg = msg.Length > 0 ? $"{DateTime.Now} - {msg}" : string.Empty;
                        Console.WriteLine(logMsg);
                        logWriter.WriteLine(logMsg);
                    };
                }
                else
                {
                    Log = msg => { Console.WriteLine(msg.Length > 0 ? $"{DateTime.Now} - {msg}" : string.Empty); };
                }

                Log("ToggleDeviceSounds started.");
                Log("Usage:");
                Log($" {Path.GetFileName(AppDomain.CurrentDomain.FriendlyName)} [-deviceInsertedFileName=\"<path to sound file>\"] [-deviceRemovedFileName=\"<path to sound file>\"] [-logFile=\"<path to log file>\"]");
                Log(string.Empty);
                Log($"Device ID: {DeviceId}");
                Log($"Device Inserted Sound File: {deviceInsertedFileName}");
                Log($"Device Removed Sound File: {deviceRemovedFileName}");

                if (!string.IsNullOrWhiteSpace(logFilePath))
                    Log($"Logging to: {logFilePath}");

                List<string> monitorIds = GetMonitorDeviceIds();
                bool deviceWasPresent = monitorIds.Contains(DeviceId);
                if (deviceWasPresent)
                    DisableDeviceSounds(deviceInsertedFileName, deviceRemovedFileName);
                else
                    EnableDeviceSounds(deviceInsertedFileName, deviceRemovedFileName);

                Log($"Polling for device events ({DeviceId}). Press Ctrl+C to exit.");
                while (true)
                {
                    var oldIds = monitorIds;
                    var newIds = GetMonitorDeviceIds();
                    var added = newIds.Except(oldIds).ToList();
                    var removed = oldIds.Except(newIds).ToList();

                    if (added.Any())
                        Log($"Monitor(s) plugged in: {string.Join(", ", added)}");
                    if (removed.Any())
                        Log($"Monitor(s) removed: {string.Join(", ", removed)}");

                    bool devicePresent = newIds.Contains(DeviceId);
                    if (devicePresent != deviceWasPresent)
                    {
                        if (devicePresent)
                            DisableDeviceSounds(deviceInsertedFileName, deviceRemovedFileName);
                        else
                            EnableDeviceSounds(deviceInsertedFileName, deviceRemovedFileName);
                        deviceWasPresent = devicePresent;
                    }
                    monitorIds = newIds;
                    Thread.Sleep(3000);
                }
            }
        }

        static void DisableDeviceSounds(string deviceInsertedFileName, string deviceRemovedFileName)
        {
            SetRegistryValue(@"HKEY_CURRENT_USER\\AppEvents\\Schemes\\Apps\\.Default\\DeviceConnect\\.Current", "(default)", "");
            SetRegistryValue(@"HKEY_CURRENT_USER\\AppEvents\\Schemes\\Apps\\.Default\\DeviceDisconnect\\.Current", "(default)", "");
            Log("Device connect/disconnect sounds disabled.");
        }

        static void EnableDeviceSounds(string deviceInsertedFileName, string deviceRemovedFileName)
        {
            SetRegistryValue(@"HKEY_CURRENT_USER\\AppEvents\\Schemes\\Apps\\.Default\\DeviceConnect\\.Current", "(default)", deviceInsertedFileName);
            SetRegistryValue(@"HKEY_CURRENT_USER\\AppEvents\\Schemes\\Apps\\.Default\\DeviceDisconnect\\.Current", "(default)", deviceRemovedFileName);
            Log("Device connect/disconnect sounds enabled.");
        }

        static void SetRegistryValue(string key, string valueName, string value)
        {
            // valueName is "(default)" for default value
            using (var regKey = Registry.CurrentUser.OpenSubKey(key.Replace(@"HKEY_CURRENT_USER\\", string.Empty), true))
            {
                if (regKey != null)
                {
                    regKey.SetValue(valueName == "(default)" ? string.Empty : valueName, value);
                }
            }
        }

        static List<string> GetMonitorDeviceIds()
        {
            var ids = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorID"))
                {
                    foreach (ManagementObject queryObj in searcher.Get())
                    {
                        string instanceName = queryObj["InstanceName"] as string;
                        if (!string.IsNullOrEmpty(instanceName))
                        {
                            var parts = instanceName.Split('\\');
                            if (parts.Length > 1)
                                ids.Add(parts[1]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error querying monitor IDs: {ex.Message}");
            }
            return ids;
        }
    }
}
