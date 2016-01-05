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
            catch (Exception e)
            {
                Console.WriteLine("Exception:");
                Console.WriteLine($"{e}");
                return 1;
            }
        }

        public virtual void Run(IReadOnlyList<string> args)
        {
            var rootDirectoryPaths = GetRootDirectoryPaths(args);
            var dateTimeNow = GetNow(args);

            foreach (var rootDirectoryPath in rootDirectoryPaths)
            {
                EnsureEmptyDirectoryOnStandby(args, rootDirectoryPath, dateTimeNow);
                DeleteObsoleteUnusedDirectories(args, rootDirectoryPath, dateTimeNow);
            }
        }

        protected virtual void EnsureEmptyDirectoryOnStandby(
            IReadOnlyList<string> args,
            string rootDirectoryPath,
            DateTimeOffset dateTime)
        {
            var existingDirectoryPaths = GetExistingDirectoryPathsForDateOrderedByIndex(args, rootDirectoryPath, dateTime);

            var directoryPath = existingDirectoryPaths.LastOrDefault();
            if (directoryPath != null)
            {
                var index = GetIndexFromDirectoryName(args, Path.GetFileName(directoryPath));
                if (index >= MaxDirectoriesPerDateTime)
                {
                    return;
                }

                var directoryIsEmpty = !Directory.EnumerateFileSystemEntries(directoryPath).Any();
                if (!directoryIsEmpty)
                {
                    index += 1;
                    if (index >= MaxDirectoriesPerDateTime)
                    {
                        return;
                    }

                    directoryPath = Path.Combine(rootDirectoryPath, GetDirectoryName(args, dateTime, index));
                    if (Directory.Exists(directoryPath))
                    {
                        return;
                    }
                }
            }
            else
            {
                directoryPath = Path.Combine(rootDirectoryPath, GetDirectoryName(args, dateTime, 0));
            }

            Directory.CreateDirectory(directoryPath);
        }

        protected virtual void DeleteObsoleteUnusedDirectories(
            IReadOnlyList<string> args,
            string rootDirectoryPath,
            DateTimeOffset dateTime)
        {
            var recentDates = GetPreviousDateTimes(args, dateTime);

            foreach (var directoryPath in recentDates
                .SelectMany(recentDate => GetExistingDirectoryPathsForDateOrderedByIndex(args, rootDirectoryPath, recentDate))
                .Where(directoryPath => !Directory.EnumerateFileSystemEntries(directoryPath).Any()))
            {
                Directory.Delete(directoryPath, false);
            }
        }

        protected virtual DateTimeOffset GetNow(IReadOnlyList<string> args) => DateTimeOffset.Now.Date;
        protected virtual IReadOnlyList<DateTimeOffset> GetPreviousDateTimes(IReadOnlyList<string> args, DateTimeOffset dateTime) =>
            Enumerable.Range(1, 21).Select(n => dateTime.AddDays(n * -1)).ToList();

        protected virtual IReadOnlyList<string> GetRootDirectoryPaths(IReadOnlyList<string> args) =>
            args.SkipWhile(arg => arg.StartsWith("-", StringComparison.Ordinal)).ToList();

        protected virtual IReadOnlyList<string> GetExistingDirectoryPathsForDateOrderedByIndex(
            IReadOnlyList<string> args,
            string rootDirectoryPath,
            DateTimeOffset dateTime)
        {
            var searchPattern = DirectoryNamePlaceholder
                .Replace(DirectoryNameDatePlaceholder, dateTime.ToString(DirectoryNameDateFormat))
                .Replace(DirectoryNameIndexPlaceholder, "??");

            return Directory.GetDirectories(rootDirectoryPath, searchPattern)
                .Where(directoryPath => Path.GetFileName(directoryPath)?.Length == DirectoryNamePlaceholder.Length)
                .Where(directoryPath =>
                {
                    int index;
                    return int.TryParse(
                        Path.GetFileName(directoryPath)?
                            .Substring(DirectoryNamePlaceholder.Length - DirectoryNameIndexPlaceholder.Length) ??
                        string.Empty,
                        out index);
                })
                .OrderBy(directoryPath => directoryPath, StringComparer.Ordinal)
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
