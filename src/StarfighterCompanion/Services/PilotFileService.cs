namespace StarfighterCompanion.Services;

/// <summary>Writes pilot files safely: backups first, then an atomic replace.</summary>
public static class PilotFileService
{
    public const string OriginalBackupSuffix = ".original.bak";
    public const string BackupSuffix = ".bak";

    /// <summary>
    /// Saves <paramref name="bytes"/> to <paramref name="path"/>.
    /// Before the first overwrite a pristine copy is kept as <c>file.original.bak</c> (never replaced afterwards),
    /// and every overwrite keeps the previous version as <c>file.bak</c>.
    /// Returns the backups that were written.
    /// </summary>
    public static IReadOnlyList<string> Save(string path, byte[] bytes)
    {
        var backups = new List<string>();
        if (File.Exists(path))
        {
            var original = path + OriginalBackupSuffix;
            if (!File.Exists(original))
            {
                File.Copy(path, original);
                backups.Add(original);
            }

            var previous = path + BackupSuffix;
            File.Copy(path, previous, overwrite: true);
            backups.Add(previous);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return backups;
    }
}
