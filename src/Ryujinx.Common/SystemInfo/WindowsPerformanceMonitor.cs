using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace Ryujinx.Common.SystemInfo
{
    [SupportedOSPlatform("windows")]
    public static class WindowsPerformanceMonitor
    {
        private const int Megabytes = 1024 * 1024;

        private static readonly Process _currentProcess = Process.GetCurrentProcess();
        private static readonly System.Diagnostics.PerformanceCounter _cpuUsageCounter = new("Process", "% Processor Time", _currentProcess.ProcessName);

        private static readonly PerformanceCounterCategory _gpuEngineCounterCategory = new("GPU Engine");
        private static readonly PerformanceCounterCategory _gpuAdapterMemoryCounterCategory = new("GPU Adapter Memory");

        private static Timer _snapshotTimer;

        public static void Initialize()
        {
            AppDomain.CurrentDomain.UnhandledException += OnExit;
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            _snapshotTimer = new Timer(LogSnapshot, null, TimeSpan.Zero, TimeSpan.FromMinutes(10));
        }

        private static List<System.Diagnostics.PerformanceCounter> GetGpuEngineCounters()
        {
            List<System.Diagnostics.PerformanceCounter> counters = new();

            foreach (var instanceName in _gpuEngineCounterCategory.GetInstanceNames())
            {
                if (instanceName.StartsWith($"pid_{_currentProcess.Id}_"))
                {
                    counters.AddRange(_gpuEngineCounterCategory.GetCounters(instanceName));
                }
            }

            return counters;
        }

        private static List<System.Diagnostics.PerformanceCounter> GetGpuAdapterMemoryCounters()
        {
            List<System.Diagnostics.PerformanceCounter> counters = new();

            foreach (var instanceName in _gpuAdapterMemoryCounterCategory.GetInstanceNames())
            {
                counters.AddRange(_gpuAdapterMemoryCounterCategory.GetCounters(instanceName));
            }

            return counters;
        }

        private static void AddGpuSnapshot(StringBuilder message)
        {
            message.AppendLine();

            foreach (var gpuCounter in GetGpuEngineCounters())
            {
                message.AppendLine(
                    $"  {gpuCounter.CategoryName} - {gpuCounter.CounterName}({gpuCounter.InstanceName}): {gpuCounter.NextValue()}");
            }

            foreach (var gpuCounter in GetGpuAdapterMemoryCounters())
            {
                message.AppendLine(
                    $"  {gpuCounter.CategoryName} - {gpuCounter.CounterName}({gpuCounter.InstanceName}): {gpuCounter.NextValue() / Megabytes} MiB");
            }
        }

        private static void LogSnapshot(object state)
        {
            StringBuilder message = new();
            float cpuUsage = _cpuUsageCounter.NextValue();

            message.AppendLine("Performance snapshot:");
            message.AppendLine($"  Process CPU usage         : {cpuUsage:F2} %");
            message.AppendLine($"  Physical memory usage     : {_currentProcess.WorkingSet64 / Megabytes} MiB");
            message.AppendLine($"  Base priority             : {_currentProcess.BasePriority}");
            message.AppendLine($"  Priority class            : {_currentProcess.PriorityClass}");
            message.AppendLine($"  User processor time       : {_currentProcess.UserProcessorTime}");
            message.AppendLine($"  Privileged processor time : {_currentProcess.PrivilegedProcessorTime}");
            message.AppendLine($"  Total processor time      : {_currentProcess.TotalProcessorTime}");
            message.AppendLine($"  Virtual memory size       : {_currentProcess.VirtualMemorySize64 / Megabytes} MiB");
            message.AppendLine($"  Paged system memory size  : {_currentProcess.PagedSystemMemorySize64 / Megabytes} MiB");
            message.AppendLine($"  Paged memory size         : {_currentProcess.PagedMemorySize64 / Megabytes} MiB");

            AddGpuSnapshot(message);

            Logger.Notice.Print(LogClass.Application, message.ToString());
        }

        private static void OnExit(object sender, EventArgs e)
        {
            _snapshotTimer.Dispose();
            LogSnapshot(null);

            StringBuilder message = new();

            message.AppendLine("Peak performance snapshot:");
            message.AppendLine($"  Physical memory usage     : {_currentProcess.PeakWorkingSet64 / Megabytes} MiB");
            message.AppendLine($"  Paged memory size         : {_currentProcess.PeakPagedMemorySize64 / Megabytes} MiB");
            message.AppendLine($"  Virtual memory size       : {_currentProcess.PeakVirtualMemorySize64 / Megabytes} MiB");


            Logger.Notice.Print(LogClass.Application, message.ToString());
        }
    }
}
