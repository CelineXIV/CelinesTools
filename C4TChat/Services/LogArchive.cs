using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace C4TChat.Services;

internal static class LogArchive
{
    internal const string ArchiveSubfolder = "Archive";

    internal sealed record MaintenanceResult(int Archived, int Deleted);

    internal static string ArchiveFolder(string logsFolder) => Path.Combine(logsFolder, ArchiveSubfolder);

    private static string ArchivePathForMonth(string logsFolder, string month) =>
        Path.Combine(ArchiveFolder(logsFolder), month + ".zip");

    internal static bool TryParseDate(string name, out DateTime date) =>
        DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    internal static List<string> ListArchivedDates(string logsFolder)
    {
        var dates = new List<string>();
        var folder = ArchiveFolder(logsFolder);
        if (!Directory.Exists(folder))
        {
            return dates;
        }

        foreach (var zipPath in Directory.GetFiles(folder, "*.zip"))
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    var name = Path.GetFileNameWithoutExtension(entry.Name);
                    if (TryParseDate(name, out _))
                    {
                        dates.Add(name);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        return dates;
    }

    internal static List<string>? ReadArchivedDay(string logsFolder, string date)
    {
        if (date.Length < 7)
        {
            return null;
        }

        var zipPath = ArchivePathForMonth(logsFolder, date[..7]);
        if (!File.Exists(zipPath))
        {
            return null;
        }

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(date + ".txt");
            if (entry == null)
            {
                return null;
            }

            var lines = new List<string>();
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static MaintenanceResult Run(string logsFolder, int archiveAfterDays, int deleteAfterDays)
    {
        var archived = 0;
        var deleted = 0;
        if (!Directory.Exists(logsFolder))
        {
            return new MaintenanceResult(0, 0);
        }

        var today = DateTime.Today;
        var deleteBefore = deleteAfterDays > 0 ? today.AddDays(-deleteAfterDays) : DateTime.MinValue;
        var archiveBefore = archiveAfterDays > 0 ? today.AddDays(-archiveAfterDays) : DateTime.MinValue;

        foreach (var file in Directory.GetFiles(logsFolder, "*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!TryParseDate(name, out var date) || date >= today)
            {
                continue;
            }

            try
            {
                if (date < deleteBefore)
                {
                    File.Delete(file);
                    deleted++;
                }
                else if (date < archiveBefore)
                {
                    AddToArchive(logsFolder, file, name);
                    File.Delete(file);
                    archived++;
                }
            }
            catch (Exception)
            {
            }
        }

        if (deleteAfterDays > 0)
        {
            deleted += DeleteFromArchives(logsFolder, deleteBefore);
        }

        return new MaintenanceResult(archived, deleted);
    }

    private static void AddToArchive(string logsFolder, string file, string date)
    {
        Directory.CreateDirectory(ArchiveFolder(logsFolder));
        using var zip = ZipFile.Open(ArchivePathForMonth(logsFolder, date[..7]), ZipArchiveMode.Update);

        var existing = zip.GetEntry(date + ".txt");
        var previous = string.Empty;
        if (existing != null)
        {
            using (var reader = new StreamReader(existing.Open(), Encoding.UTF8))
            {
                previous = reader.ReadToEnd();
            }

            existing.Delete();
        }

        var entry = zip.CreateEntry(date + ".txt", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(previous);
        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sourceReader = new StreamReader(source, Encoding.UTF8);
        writer.Write(sourceReader.ReadToEnd());
    }

    private static int DeleteFromArchives(string logsFolder, DateTime deleteBefore)
    {
        var deleted = 0;
        var folder = ArchiveFolder(logsFolder);
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        foreach (var zipPath in Directory.GetFiles(folder, "*.zip"))
        {
            try
            {
                var empty = false;
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                {
                    foreach (var entry in zip.Entries.ToList())
                    {
                        if (TryParseDate(Path.GetFileNameWithoutExtension(entry.Name), out var date) && date < deleteBefore)
                        {
                            entry.Delete();
                            deleted++;
                        }
                    }

                    empty = zip.Entries.Count == 0;
                }

                if (empty)
                {
                    File.Delete(zipPath);
                }
            }
            catch (Exception)
            {
            }
        }

        return deleted;
    }

    internal static long FolderSize(string logsFolder)
    {
        if (!Directory.Exists(logsFolder))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(logsFolder, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception)
            {
            }
        }

        return total;
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", CultureInfo.CurrentCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", CultureInfo.CurrentCulture) + " GB",
    };
}
