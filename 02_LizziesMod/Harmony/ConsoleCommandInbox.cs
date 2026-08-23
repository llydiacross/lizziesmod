using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;

namespace LizziesMod
{
    public static class ConsoleCommandInbox
    {
        private const string InboxDirectoryName = "ConsoleCommandInbox";
        private const string PendingDirectoryName = "Pending";
        private const string ProcessingDirectoryName = "Processing";
        private const string ResultsDirectoryName = "Results";
        private const int MaximumCommandCharacters = 4096;
        private static readonly object sync = new object();
        private static string pendingDirectory = "";
        private static string processingDirectory = "";
        private static string resultsDirectory = "";

        public static void Initialize(Mod coreMod)
        {
            if (coreMod == null || string.IsNullOrEmpty(coreMod.Path))
            {
                Logger.Warning("[ConsoleInbox] The core mod path is unavailable; file commands are disabled.");
                return;
            }

            lock (sync)
            {
                string inboxDirectory = Path.Combine(coreMod.Path, InboxDirectoryName);
                pendingDirectory = Path.Combine(inboxDirectory, PendingDirectoryName);
                processingDirectory = Path.Combine(inboxDirectory, ProcessingDirectoryName);
                resultsDirectory = Path.Combine(inboxDirectory, ResultsDirectoryName);
                Directory.CreateDirectory(pendingDirectory);
                Directory.CreateDirectory(processingDirectory);
                Directory.CreateDirectory(resultsDirectory);
                RecoverInterruptedRequests();
            }

            Logger.Info("[ConsoleInbox] Ready at '" + pendingDirectory + "'.");
        }

        public static void ProcessNext()
        {
            string claimedRequestPath;
            if (!TryClaimNextRequest(out claimedRequestPath)) return;

            string requestId = Path.GetFileNameWithoutExtension(claimedRequestPath);
            string command = "";
            try
            {
                command = File.ReadAllText(claimedRequestPath, Encoding.UTF8).Trim();
                if (!ModSettingsManager.IsDeveloperMode)
                {
                    WriteResult(requestId, "rejected", command, null, "Developer mode is required for file-backed console commands.");
                    return;
                }

                string validationError;
                if (!TryValidateCommand(command, out validationError))
                {
                    WriteResult(requestId, "rejected", command, null, validationError);
                    return;
                }

                if (TryProcessSystemRequest(requestId, command)) return;

                SdtdConsole console = SdtdConsole.Instance;
                if (console == null)
                {
                    WriteResult(requestId, "deferred", command, null, "The console dispatcher is not ready yet.");
                    return;
                }

                List<string> output = console.ExecuteSync(command, new ClientInfo());
                WriteResult(requestId, "completed", command, output, "");
                Logger.Info("[ConsoleInbox] Executed request '" + requestId + "': " + command);
            }
            catch (Exception exception)
            {
                WriteResult(requestId, "failed", command, null, exception.ToString());
                Logger.Error("[ConsoleInbox] Request '" + requestId + "' failed: " + exception);
            }
            finally
            {
                TryDelete(claimedRequestPath);
            }
        }

        private static bool TryClaimNextRequest(out string claimedRequestPath)
        {
            claimedRequestPath = "";
            lock (sync)
            {
                if (string.IsNullOrEmpty(pendingDirectory) || !Directory.Exists(pendingDirectory)) return false;

                string[] requestPaths = Directory.GetFiles(pendingDirectory, "*.txt", SearchOption.TopDirectoryOnly);
                Array.Sort(requestPaths, StringComparer.OrdinalIgnoreCase);
                foreach (string requestPath in requestPaths)
                {
                    string claimedPath = Path.Combine(processingDirectory, Path.GetFileName(requestPath));
                    try
                    {
                        File.Move(requestPath, claimedPath);
                        claimedRequestPath = claimedPath;
                        return true;
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            return false;
        }

        private static bool TryValidateCommand(string command, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(command))
            {
                error = "The request did not contain a command.";
                return false;
            }

            if (command.Length > MaximumCommandCharacters)
            {
                error = "The command exceeds " + MaximumCommandCharacters + " characters.";
                return false;
            }

            if (command.IndexOf('\0') >= 0 || command.IndexOf('\r') >= 0 || command.IndexOf('\n') >= 0)
            {
                error = "Commands must be a single non-null line.";
                return false;
            }

            return true;
        }

        private static bool TryProcessSystemRequest(string requestId, string command)
        {
            if (!command.Equals("@spawn-world", StringComparison.OrdinalIgnoreCase)) return false;

            GameSpawnAutomation.RequestSpawn();
            WriteResult(
                requestId,
                "queued",
                command,
                new List<string>
                {
                    "Queued the native Spawn action. It will run as soon as the loading screen is ready."
                },
                "");
            Logger.Info("[ConsoleInbox] Queued native Spawn action from request '" + requestId + "'.");
            return true;
        }

        private static void RecoverInterruptedRequests()
        {
            if (string.IsNullOrEmpty(processingDirectory) || !Directory.Exists(processingDirectory)) return;

            foreach (string requestPath in Directory.GetFiles(processingDirectory, "*.txt", SearchOption.TopDirectoryOnly))
            {
                string requestId = Path.GetFileNameWithoutExtension(requestPath);
                string resultPath = Path.Combine(resultsDirectory, requestId + ".txt");
                if (File.Exists(resultPath))
                {
                    TryDelete(requestPath);
                    continue;
                }

                string pendingPath = Path.Combine(pendingDirectory, Path.GetFileName(requestPath));
                try
                {
                    File.Move(requestPath, pendingPath);
                    Logger.Warning("[ConsoleInbox] Requeued interrupted request '" + requestId + "'.");
                }
                catch (Exception exception)
                {
                    Logger.Warning("[ConsoleInbox] Could not recover request '" + requestId + "': " + exception.Message);
                }
            }
        }

        private static void WriteResult(
            string requestId,
            string status,
            string command,
            List<string> output,
            string error)
        {
            if (string.IsNullOrEmpty(resultsDirectory)) return;

            try
            {
                StringBuilder result = new StringBuilder();
                result.AppendLine("Status: " + status);
                result.AppendLine("RequestId: " + requestId);
                result.AppendLine("Command: " + command);
                if (!string.IsNullOrEmpty(error)) result.AppendLine("Error: " + error);
                result.AppendLine("Output:");
                if (output != null && output.Count > 0)
                {
                    foreach (string line in output)
                    {
                        result.AppendLine(line ?? "");
                    }
                }
                else
                {
                    result.AppendLine("(no console output)");
                }

                string resultPath = Path.Combine(resultsDirectory, requestId + ".txt");
                string temporaryPath = resultPath + ".tmp";
                File.WriteAllText(temporaryPath, result.ToString(), new UTF8Encoding(false));
                if (File.Exists(resultPath)) File.Delete(resultPath);
                File.Move(temporaryPath, resultPath);
            }
            catch (Exception exception)
            {
                Logger.Error("[ConsoleInbox] Could not write result for '" + requestId + "': " + exception);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                Logger.Warning("[ConsoleInbox] Could not clean request '" + path + "': " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SdtdConsole), "Update")]
    public class SdtdConsole_ConsoleCommandInboxPatch
    {
        public static void Postfix()
        {
            ConsoleCommandInbox.ProcessNext();
        }
    }
}