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
        private const string GpuEngineCounterCategory = "GPU Engine";
        private const string GpuAdapterMemoryCounterCategory = "GPU Adapter Memory";

        private static readonly Process _currentProcess = Process.GetCurrentProcess();
        private static readonly System.Diagnostics.PerformanceCounter _cpuUsageCounter = new("Process", "% Processor Time", _currentProcess.ProcessName);
        private static readonly List<System.Diagnostics.PerformanceCounter> _gpuCounters = new();

        private static Timer _snapshotTimer;

        public static void Initialize()
        {
            AppDomain.CurrentDomain.UnhandledException += OnExit;
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            InitGpuCounters();

            _snapshotTimer = new Timer(LogSnapshot, null, TimeSpan.Zero, TimeSpan.FromMinutes(10));
        }

        private static void InitGpuCounters()
        {
            PerformanceCounterCategory[] categories =
            {
                new(GpuEngineCounterCategory),
                new(GpuAdapterMemoryCounterCategory),
            };

            foreach (var category in categories)
            {
                foreach (var instanceName in category.GetInstanceNames())
                {
                    _gpuCounters.AddRange(category.GetCounters(instanceName));
                }
            }
        }

        private static void AddGpuSnapshot(StringBuilder message)
        {
            message.AppendLine();

            foreach (var gpuCounter in _gpuCounters)
            {
                message.AppendLine(
                    $"  {gpuCounter.CategoryName} - {gpuCounter.CounterName}({gpuCounter.InstanceName}): {gpuCounter.NextValue()}");
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
