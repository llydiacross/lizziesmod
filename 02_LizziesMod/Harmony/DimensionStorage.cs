using System;
using System.IO;

namespace LizziesMod
{
    public static class DimensionStorage
    {
        private const string ModDataDirectoryName = "LizziesMod";
        private const string DimensionsDirectoryName = "Dimensions";
        private const string BackupDirectoryName = "LizziesMod_Backups";
        private const string SaveMarkerFileName = "main.ttw";
        private static readonly object storageLock = new object();

        public static bool TryGetDimensionSaveDirectory(string overworldSaveDirectory, string dimensionId, out string saveDirectory)
        {
            saveDirectory = "";
            if (!IsValidDimensionId(dimensionId) || DimensionManager.IsOverworld(dimensionId)) return false;

            try
            {
                saveDirectory = GetDimensionRoot(overworldSaveDirectory, dimensionId);
                return File.Exists(Path.Combine(saveDirectory, SaveMarkerFileName));
            }
            catch (Exception exception)
            {
                Logger.Error($"[DimensionManager] Could not resolve dimension '{dimensionId}': {exception.Message}");
                saveDirectory = "";
                return false;
            }
        }

        public static bool HasSaveSnapshot(string overworldSaveDirectory, string dimensionId)
        {
            string saveDirectory;
            return TryGetDimensionSaveDirectory(overworldSaveDirectory, dimensionId, out saveDirectory);
        }

        public static bool TryCreateSaveSnapshot(string overworldSaveDirectory, string dimensionId, out string error)
        {
            return TryCreateDimensionSave(overworldSaveDirectory, dimensionId, "save snapshot", null, out error);
        }

        public static bool TryCreateGeneratedDimensionSave(string overworldSaveDirectory, string dimensionId, out string error)
        {
            return TryCreateDimensionSave(
                overworldSaveDirectory,
                dimensionId,
                "generated dimension",
                PrepareGeneratedDimensionDirectory,
                out error);
        }

        private static bool TryCreateDimensionSave(
            string overworldSaveDirectory,
            string dimensionId,
            string description,
            Action<string> prepareStagingDirectory,
            out string error)
        {
            error = "";
            if (!IsValidDimensionId(dimensionId) || DimensionManager.IsOverworld(dimensionId))
            {
                error = "The dimension ID is invalid.";
                return false;
            }

            string stagingDirectory = "";
            try
            {
                string sourceDirectory = Path.GetFullPath(overworldSaveDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(sourceDirectory))
                {
                    error = "The Overworld save directory does not exist.";
                    return false;
                }

                if (!File.Exists(Path.Combine(sourceDirectory, SaveMarkerFileName)))
                {
                    error = "The Overworld save marker is missing.";
                    return false;
                }

                string targetRoot = GetDimensionRoot(sourceDirectory, dimensionId);
                if (Directory.Exists(targetRoot))
                {
                    if (File.Exists(Path.Combine(targetRoot, SaveMarkerFileName))) return true;

                    error = $"The existing dimension directory '{targetRoot}' is incomplete.";
                    return false;
                }

                string dimensionsRoot = GetDimensionsRoot(sourceDirectory);
                Directory.CreateDirectory(dimensionsRoot);
                stagingDirectory = Path.Combine(dimensionsRoot, "." + dimensionId + ".staging_" + Guid.NewGuid().ToString("N"));

                lock (storageLock)
                {
                    CopySaveDirectory(sourceDirectory, stagingDirectory);
                    prepareStagingDirectory?.Invoke(stagingDirectory);
                    Directory.Move(stagingDirectory, targetRoot);
                }

                stagingDirectory = "";
                Logger.Info($"[DimensionManager] Created {description} for '{dimensionId}' at '{targetRoot}'.");
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Logger.Error($"[DimensionManager] Failed to create {description} for '{dimensionId}': {exception.Message}");
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(stagingDirectory) && Directory.Exists(stagingDirectory))
                {
                    try
                    {
                        Directory.Delete(stagingDirectory, true);
                    }
                    catch (Exception cleanupException)
                    {
                        Logger.Warning($"[DimensionManager] Failed to clean up '{stagingDirectory}': {cleanupException.Message}");
                    }
                }
            }
        }

        private static void PrepareGeneratedDimensionDirectory(string saveDirectory)
        {
            string regionDirectory = Path.Combine(saveDirectory, "Region");
            if (Directory.Exists(regionDirectory)) Directory.Delete(regionDirectory, true);
            Directory.CreateDirectory(regionDirectory);

            foreach (string fileName in new[]
            {
                "decoration.7dt",
                "multiblocks.7dt",
                "power.dat",
                "vehicles.dat",
                "drones.dat",
                "turrets.dat"
            })
            {
                DeleteFileAndBackup(Path.Combine(saveDirectory, fileName));
            }
        }

        private static void DeleteFileAndBackup(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            string backupPath = path + ".bak";
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }

        public static bool IsValidDimensionId(string dimensionId)
        {
            if (string.IsNullOrEmpty(dimensionId) || dimensionId.Length > 48) return false;

            foreach (char character in dimensionId)
            {
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_') return false;
            }

            return true;
        }

        private static string GetDimensionRoot(string overworldSaveDirectory, string dimensionId)
        {
            return Path.Combine(GetDimensionsRoot(overworldSaveDirectory), dimensionId);
        }

        private static string GetDimensionsRoot(string overworldSaveDirectory)
        {
            DirectoryInfo saveDirectory = new DirectoryInfo(Path.GetFullPath(overworldSaveDirectory));
            if (saveDirectory.Parent == null) throw new IOException("The Overworld save directory has no parent directory.");

            return Path.Combine(saveDirectory.Parent.FullName, saveDirectory.Name + "_" + ModDataDirectoryName, DimensionsDirectoryName);
        }

        private static void CopySaveDirectory(string sourceDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);

            foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
            {
                FileInfo sourceInfo = new FileInfo(sourceFile);
                if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                string targetFile = Path.Combine(targetDirectory, sourceInfo.Name);
                File.Copy(sourceFile, targetFile, true);
                File.SetLastWriteTimeUtc(targetFile, sourceInfo.LastWriteTimeUtc);
            }

            foreach (string childDirectory in Directory.GetDirectories(sourceDirectory))
            {
                DirectoryInfo childInfo = new DirectoryInfo(childDirectory);
                if ((childInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (childInfo.Name.Equals(BackupDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;

                CopySaveDirectory(childDirectory, Path.Combine(targetDirectory, childInfo.Name));
            }
        }
    }
}