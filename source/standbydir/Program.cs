using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            foreach (var _rootDirectoryPath in _rootDirectoryPaths)
            {
                EnsureEmptyDirectoryOnStandby(args, _rootDirectoryPath, _dateTimeNow);
                DeleteObsoleteUnusedDirectories(args, _rootDirectoryPath, _dateTimeNow);
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
