using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.Logging.StructuredLogger;

namespace MSBuildParallelizationAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Parse command-line arguments
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: MSBuildParallelizationAnalyzer <path_to_binlog_file> <number_of_cpu_cores>");
                    return;
                }

                string binlogFilePath = args[0];
                if (!File.Exists(binlogFilePath))
                {
                    Console.WriteLine($"Error: Binary log file '{binlogFilePath}' not found.");
                    return;
                }

                if (!int.TryParse(args[1], out int cpuCoreCount) || cpuCoreCount <= 0)
                {
                    Console.WriteLine("Error: Number of CPU cores must be a positive integer.");
                    return;
                }

                // Parse the binary log file
                var (buildStartTime, buildEndTime, tasks) = ParseBinaryLogFile(binlogFilePath);
                
                if (buildStartTime == DateTime.MinValue || buildEndTime == DateTime.MinValue || tasks.Count == 0)
                {
                    Console.WriteLine("Error: Could not extract required information from the binary log file.");
                    return;
                }

                // Calculate build time (T)
                TimeSpan buildTime = buildEndTime - buildStartTime;
                
                // Calculate parallelization metric
                double metric = CalculateParallelizationMetric(tasks, buildTime);
                Console.WriteLine($"Build Parallelization Metric: {metric:F1}");
                
                // Visualize parallelization over time
                VisualizeParallelization(tasks, buildStartTime, buildEndTime, cpuCoreCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        static (DateTime buildStartTime, DateTime buildEndTime, List<(DateTime start, DateTime end, string name)> tasks) ParseBinaryLogFile(string binlogFilePath)
        {
            DateTime buildStartTime = DateTime.MinValue;
            DateTime buildEndTime = DateTime.MinValue;
            var tasks = new List<(DateTime start, DateTime end, string name)>();
            
            try
            {
                // Open and read the binary log file
                var binLogReader = new BinaryLogReader();
                var build = binLogReader.ReadBuild(binlogFilePath);
                
                if (build == null)
                {
                    Console.WriteLine("Error: Failed to read binary log file.");
                    return (DateTime.MinValue, DateTime.MinValue, tasks);
                }
                
                // Extract build start and end times
                buildStartTime = build.StartTime;
                buildEndTime = build.EndTime;
                
                // Extract task information using a visitor pattern
                var taskCollector = new TaskCollector();
                build.VisitAllChildren(taskCollector);
                
                // Convert collected tasks to our format
                foreach (var task in taskCollector.Tasks)
                {
                    if (task.StartTime != DateTime.MinValue && task.EndTime != DateTime.MinValue)
                    {
                        tasks.Add((task.StartTime, task.EndTime, task.Name));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing binary log: {ex.Message}");
            }
            
            return (buildStartTime, buildEndTime, tasks);
        }

        static double CalculateParallelizationMetric(List<(DateTime start, DateTime end, string name)> tasks, TimeSpan buildTime)
        {
            // Calculate total CPU time (sum of all task durations)
            TimeSpan totalCpuTime = TimeSpan.Zero;
            foreach (var task in tasks)
            {
                totalCpuTime += task.end - task.start;
            }
            
            // Calculate parallelization metric (total CPU time / build time)
            return totalCpuTime.TotalSeconds / buildTime.TotalSeconds;
        }

        static void VisualizeParallelization(List<(DateTime start, DateTime end, string name)> tasks, DateTime buildStartTime, DateTime buildEndTime, int cpuCoreCount)
        {
            Console.WriteLine("\nParallelization Timeline:");
            Console.WriteLine("Time     - Active Tasks");
            Console.WriteLine("-------------------------");
            
            // Create a timeline with one-second intervals
            var intervalCount = (int)(buildEndTime - buildStartTime).TotalSeconds + 1;
            
            for (int i = 0; i < intervalCount; i++)
            {
                DateTime currentTime = buildStartTime.AddSeconds(i);
                
                // Count how many tasks are active at this time
                int activeTasks = tasks.Count(task => 
                    task.start <= currentTime && task.end >= currentTime);
                
                // Cap the visualization at the CPU core count
                int visualizedTasks = Math.Min(activeTasks, cpuCoreCount);
                
                // Create the visualization
                string visualization = new string('*', visualizedTasks);
                string timeString = currentTime.ToString("HH:mm:ss");
                
                Console.WriteLine($"{timeString} - {visualization} ({activeTasks} tasks)");
            }
            
            // Print the top 5 longest-running tasks
            Console.WriteLine("\nTop 5 Longest-Running Tasks:");
            Console.WriteLine("-----------------------------");
            
            var longestTasks = tasks
                .OrderByDescending(t => (t.end - t.start).TotalSeconds)
                .Take(5)
                .ToList();
                
            foreach (var task in longestTasks)
            {
                TimeSpan duration = task.end - task.start;
                Console.WriteLine($"{task.name} - {duration.TotalSeconds:F1} seconds");
            }
        }
    }

    // Helper class to collect task information from the binary log
    class TaskCollector : ILogNodeVisitor
    {
        public List<TaskInfo> Tasks { get; } = new List<TaskInfo>();

        public void Visit(Build build) { /* Do nothing */ }
        public void Visit(Project project) { /* Do nothing */ }
        public void Visit(Target target) { /* Do nothing */ }
        
        public void Visit(Task task)
        {
            // Extract task information
            if (task.StartTime != DateTime.MinValue && task.EndTime != DateTime.MinValue)
            {
                Tasks.Add(new TaskInfo
                {
                    Name = task.Name,
                    StartTime = task.StartTime,
                    EndTime = task.EndTime
                });
            }
        }

        public void Visit(Property property) { /* Do nothing */ }
        public void Visit(PropertyReuse propertyReuse) { /* Do nothing */ }
        public void Visit(Item item) { /* Do nothing */ }
        public void Visit(ItemGroup itemGroup) { /* Do nothing */ }
        public void Visit(Error error) { /* Do nothing */ }
        public void Visit(Warning warning) { /* Do nothing */ }
        public void Visit(Message message) { /* Do nothing */ }
        public void Visit(IssueBase issue) { /* Do nothing */ }
        public void Visit(Schedule schedule) { /* Do nothing */ }
    }

    // Helper class to store task information
    class TaskInfo
    {
        public string Name { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}