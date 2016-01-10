using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nerven.Taskuler;
using Nerven.Taskuler.Core;

namespace standbydir
{
    public class Program
    {
        protected virtual int MaxDirectoriesPerDateTime => 99;

        protected virtual string DirectoryNameDateFormat => "yyyy\\-MM\\-dd";

        protected virtual string DirectoryNameDatePlaceholder => DirectoryNameDateFormat.Replace("\\", string.Empty);

        protected virtual string DirectoryNameIndexPlaceholder => "XX";

        protected virtual string DirectoryNamePlaceholder => DirectoryNameDatePlaceholder + "-" + DirectoryNameIndexPlaceholder;

        public static int Main(params string[] args)
        {
            try
            {
                new Program().Run(args);
                return 0;
            }
            catch (Exception _e)
            {
                Console.WriteLine("Exception:");
                Console.WriteLine($"{_e}");
                return 1;
            }
        }

        public virtual void Run(IReadOnlyList<string> args)
        {
            var _rootDirectoryPaths = GetRootDirectoryPaths(args);
            var _dateTimeNow = GetNow(args);
            var _mode = GetOption(args, "--mode", "-m")?.Value ?? "once";
            switch (_mode.ToLower(CultureInfo.InvariantCulture))
            {
                case "once":
                case "o":
                    foreach (var _rootDirectoryPath in _rootDirectoryPaths)
                    {
                        EnsureEmptyDirectoryOnStandby(args, _rootDirectoryPath, _dateTimeNow);
                        DeleteObsoleteUnusedDirectories(args, _rootDirectoryPath, _dateTimeNow);
                    }
                    break;
                case "continues":
                case "c":
                    ITaskulerWorker _worker;
                    var _workerResolution = TaskulerWorker.DefaultResolution;
                    using ((IDisposable)(_worker = TaskulerWorker.Create(_workerResolution)))
                    {
                        var _ensureEmptyDirectoryOnStandbyTask = _worker.AddIntervalSchedule(TimeSpan.FromMinutes(1))
                            .AddTask(nameof(EnsureEmptyDirectoryOnStandby), () => Task.Run(() =>
                                {
                                    foreach (var _rootDirectoryPath in _rootDirectoryPaths)
                                    {
                                        EnsureEmptyDirectoryOnStandby(args, _rootDirectoryPath, _dateTimeNow);
                                    }
                                }));
                        var _deleteObsoleteUnusedDirectoriesTask = _worker.AddIntervalSchedule(TimeSpan.FromHours(2))
                            .AddTask(nameof(DeleteObsoleteUnusedDirectories), () => Task.Run(() =>
                                {
                                    foreach (var _rootDirectoryPath in _rootDirectoryPaths)
                                    {
                                        DeleteObsoleteUnusedDirectories(args, _rootDirectoryPath, _dateTimeNow);
                                    }
                                }));

                        try
                        {
                            _worker.StartAsync().Wait();

                            // Taskuler waits one resolution cycle before the first tick,
                            // and RunManually() will throw if called before the first tick has occurred.
                            Task.Delay(_workerResolution.Add(_workerResolution)).Wait();

                            _ensureEmptyDirectoryOnStandbyTask.RunManually();
                            _deleteObsoleteUnusedDirectoriesTask.RunManually();

                            Console.ReadLine();
                        }
                        finally
                        {
                            _worker.StopAsync().Wait();
                        }
                    }
                    break;
            }
        }

        protected virtual void EnsureEmptyDirectoryOnStandby(
            IReadOnlyList<string> args,
            string rootDirectoryPath,
            DateTimeOffset dateTime)
        {
            var _existingDirectoryPaths = GetExistingDirectoryPathsForDateOrderedByIndex(args, rootDirectoryPath, dateTime);

            var _directoryPath = _existingDirectoryPaths.LastOrDefault();
            if (_directoryPath != null)
            {
                var _index = GetIndexFromDirectoryName(args, Path.GetFileName(_directoryPath));
                if (_index >= MaxDirectoriesPerDateTime)
                {
                    return;
                }

                var _directoryIsEmpty = !Directory.EnumerateFileSystemEntries(_directoryPath).Any();
                if (!_directoryIsEmpty)
                {
                    _index += 1;
                    if (_index >= MaxDirectoriesPerDateTime)
                    {
                        return;
                    }

                    _directoryPath = Path.Combine(rootDirectoryPath, GetDirectoryName(args, dateTime, _index));
                    if (Directory.Exists(_directoryPath))
                    {
                        return;
                    }
                }
            }
            else
            {
                _directoryPath = Path.Combine(rootDirectoryPath, GetDirectoryName(args, dateTime, 0));
            }

            Directory.CreateDirectory(_directoryPath);
        }

        protected virtual void DeleteObsoleteUnusedDirectories(
            IReadOnlyList<string> args,
            string rootDirectoryPath,
            DateTimeOffset dateTime)
        {
            var _recentDates = GetPreviousDateTimes(args, dateTime);

            foreach (var _directoryPath in _recentDates
                .SelectMany(_recentDate => GetExistingDirectoryPathsForDateOrderedByIndex(args, rootDirectoryPath, _recentDate))
                .Where(_directoryPath => !Directory.EnumerateFileSystemEntries(_directoryPath).Any()))
            {
                Directory.Delete(_directoryPath, false);
            }
        }

        protected virtual DateTimeOffset GetNow(IReadOnlyList<string> args) => DateTimeOffset.Now.Date;
        protected virtual IReadOnlyList<DateTimeOffset> GetPreviousDateTimes(IReadOnlyList<string> args, DateTimeOffset dateTime) =>
            Enumerable.Range(1, 21).Select(_n => dateTime.AddHours(-6).AddDays(_n * -1)).ToList();

        protected virtual IReadOnlyList<KeyValuePair<string, string>> GetOptions(IReadOnlyList<string> args) =>
            args
                .TakeWhile(_arg => _arg.StartsWith("-", StringComparison.Ordinal))
                .Select(_arg =>
                    {
                        var _separatorIndex = _arg.IndexOf('=');
                        return _separatorIndex == -1
                            ? new KeyValuePair<string, string>(_arg, null)
                            : new KeyValuePair<string, string>(_arg.Substring(0, _separatorIndex), _arg.Substring(_separatorIndex + 1));
                    })
                .ToList();

        protected virtual KeyValuePair<string, string>? GetOption(IReadOnlyList<string> args, params string[] optionNames) =>
            GetOptions(args)
                .Cast<KeyValuePair<string, string>?>()
                .FirstOrDefault(_option => _option.HasValue && optionNames.Any(_optionName => string.Equals(_option.Value.Key, _optionName, StringComparison.OrdinalIgnoreCase)));

        protected virtual IReadOnlyList<string> GetRootDirectoryPaths(IReadOnlyList<string> args) =>
            args.SkipWhile(_arg => _arg.StartsWith("-", StringComparison.Ordinal)).ToList();

        protected virtual IReadOnlyList<string> GetExistingDirectoryPathsForDateOrderedByIndex(
            IReadOnlyList<string> args,
            string rootDirectoryPath,
            DateTimeOffset dateTime)
        {
            var _searchPattern = DirectoryNamePlaceholder
                .Replace(DirectoryNameDatePlaceholder, dateTime.ToString(DirectoryNameDateFormat))
                .Replace(DirectoryNameIndexPlaceholder, "??");

            return Directory.GetDirectories(rootDirectoryPath, _searchPattern)
                .Where(_directoryPath => Path.GetFileName(_directoryPath)?.Length == DirectoryNamePlaceholder.Length)
                .Where(_directoryPath =>
                {
                    int _index;
                    return int.TryParse(
                        Path.GetFileName(_directoryPath)?
                            .Substring(DirectoryNamePlaceholder.Length - DirectoryNameIndexPlaceholder.Length) ??
                        string.Empty,
                        out _index);
                })
                .OrderBy(_directoryPath => _directoryPath, StringComparer.Ordinal)
                .ToList();
        }

        protected virtual string GetDirectoryName(IReadOnlyList<string> args, DateTimeOffset dateTime, int index) =>
            DirectoryNamePlaceholder
                .Replace(DirectoryNameDatePlaceholder, dateTime.ToString(DirectoryNameDateFormat))
                .Replace(DirectoryNameIndexPlaceholder, (index + 1).ToString().PadLeft(DirectoryNameIndexPlaceholder.Length, '0'));

        protected virtual int GetIndexFromDirectoryName(IReadOnlyList<string> args, string directoryName) =>
            int.Parse(directoryName.Substring(DirectoryNamePlaceholder.Length - DirectoryNameIndexPlaceholder.Length)) - 1;
    }
}
