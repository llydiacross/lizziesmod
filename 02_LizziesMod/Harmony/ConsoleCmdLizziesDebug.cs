using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LizziesMod
{
    public class ConsoleCmdLizziesDebug : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new[] { "lizziesdebug", "lizziesdev" };
        }

        public override string getDescription()
        {
            return "Inspects LizziesMod dimensions, chunk state, settings, and XML diagnostics.";
        }

        public override string getHelp()
        {
            return "Usage:\n" +
                "  lizziesdebug dimensions\n" +
                "  lizziesdebug status [dimension-id]\n" +
                "  lizziesdebug region [dimension-id]\n" +
                "  lizziesdebug position\n" +
                "  lizziesdebug chunk [chunk-x chunk-z]\n" +
                "  lizziesdebug settings [mod-name]\n" +
                "  lizziesdebug diagnostics\n" +
                "  lizziesdebug enter <dimension-id>\n" +
                "  lizziesdebug return\n\n" +
                "Use lizziesregendimension <dimension-id> to archive and regenerate generated terrain.";
        }

        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters == null || parameters.Count == 0)
            {
                Output(getHelp(), senderInfo);
                return;
            }

            string operation = parameters[0].ToLowerInvariant();
            switch (operation)
            {
                case "help":
                    Output(getHelp(), senderInfo);
                    return;
                case "dimensions":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputDimensions(senderInfo);
                    return;
                case "status":
                    if (parameters.Count > 2)
                    {
                        Output(getHelp(), senderInfo);
                        return;
                    }

                    OutputDimensionStatus(parameters.Count == 2 ? parameters[1] : DimensionManager.ActiveDimensionId, senderInfo);
                    return;
                case "region":
                    if (parameters.Count > 2)
                    {
                        Output(getHelp(), senderInfo);
                        return;
                    }

                    OutputRegionStatus(parameters.Count == 2 ? parameters[1] : DimensionManager.ActiveDimensionId, senderInfo);
                    return;
                case "position":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputPosition(senderInfo);
                    return;
                case "chunk":
                    OutputChunk(parameters, senderInfo);
                    return;
                case "settings":
                    if (parameters.Count > 2)
                    {
                        Output(getHelp(), senderInfo);
                        return;
                    }

                    OutputSettings(parameters.Count == 2 ? parameters[1] : null, senderInfo);
                    return;
                case "diagnostics":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    OutputDiagnostics(senderInfo);
                    return;
                case "enter":
                    if (!HasParameterCount(parameters, 2, senderInfo)) return;
                    EnterDimension(parameters[1], senderInfo);
                    return;
                case "return":
                    if (!HasParameterCount(parameters, 1, senderInfo)) return;
                    ReturnToOverworld(senderInfo);
                    return;
                default:
                    Output("Unknown LizziesMod debug operation '" + parameters[0] + "'.\n\n" + getHelp(), senderInfo);
                    return;
            }
        }

        private static bool HasParameterCount(List<string> parameters, int expectedCount, CommandSenderInfo senderInfo)
        {
            if (parameters.Count == expectedCount) return true;

            Output("This operation expects " + (expectedCount - 1) + " argument(s). Run lizziesdebug help for usage.", senderInfo);
            return false;
        }

        private static void OutputDimensions(CommandSenderInfo senderInfo)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("LizziesMod dimensions");
            report.AppendLine("Active: " + DimensionManager.ActiveDimensionId +
                " | Default: " + DimensionRegistry.DefaultDimensionId +
                " | Transition: " + DimensionManager.IsTransitionInProgress);
            report.AppendLine("Definitions:");

            foreach (DimensionDefinition definition in DimensionRegistry.GetDefinitions())
            {
                DimensionGeneratorDefinition generator;
                bool generatorRegistered = DimensionGeneratorRegistry.TryGet(definition.GeneratorId, out generator);
                string mode = generatorRegistered ? generator.SaveMode.ToString() : "missing";
                report.AppendLine("- " + definition.Id + " | " + definition.DisplayName +
                    " | generator=" + definition.GeneratorId + " (" + mode + ")" +
                    " | supported=" + definition.IsSupported);
            }

            report.AppendLine("Registered generators:");
            foreach (DimensionGeneratorDefinition generator in DimensionGeneratorRegistry.GetRegisteredGenerators())
            {
                report.AppendLine("- " + generator.Id + " | " + generator.SaveMode +
                    " | main-thread work=" + (generator.ProcessMainThread != null));
            }

            Output(report.ToString().TrimEnd(), senderInfo);
        }

        private static void OutputDimensionStatus(string dimensionId, CommandSenderInfo senderInfo)
        {
            if (string.IsNullOrEmpty(dimensionId))
            {
                Output("A dimension ID is required.", senderInfo);
                return;
            }

            if (DimensionManager.IsOverworld(dimensionId))
            {
                StringBuilder overworldReport = new StringBuilder();
                overworldReport.AppendLine("Dimension: Overworld");
                overworldReport.AppendLine("Active: " + DimensionManager.IsOverworld(DimensionManager.ActiveDimensionId));
                overworldReport.AppendLine("Transition: " + DimensionManager.IsTransitionInProgress);
                overworldReport.AppendLine("Save directory: " + GameIO.GetSaveGameDir());
                Output(overworldReport.ToString().TrimEnd(), senderInfo);
                return;
            }

            DimensionDefinition definition;
            if (!DimensionRegistry.TryGet(dimensionId, out definition))
            {
                Output("No registered dimension named '" + dimensionId + "'. Run lizziesdebug dimensions to list definitions.", senderInfo);
                return;
            }

            DimensionGeneratorDefinition generator;
            bool generatorRegistered = DimensionGeneratorRegistry.TryGet(definition.GeneratorId, out generator);
            StringBuilder report = new StringBuilder();
            report.AppendLine("Dimension: " + definition.DisplayName + " (" + definition.Id + ")");
            report.AppendLine("Active: " + definition.Id.Equals(DimensionManager.ActiveDimensionId, StringComparison.OrdinalIgnoreCase));
            report.AppendLine("Transition: " + DimensionManager.IsTransitionInProgress);
            report.AppendLine("Generator: " + definition.GeneratorId +
                " | registered=" + generatorRegistered +
                " | mode=" + (generatorRegistered ? generator.SaveMode.ToString() : "missing"));
            report.AppendLine("Supported: " + definition.IsSupported);
            AppendRegionStatus(report, definition.Id);
            Output(report.ToString().TrimEnd(), senderInfo);
        }

        private static void OutputRegionStatus(string dimensionId, CommandSenderInfo senderInfo)
        {
            if (DimensionManager.IsOverworld(dimensionId))
            {
                Output("The Overworld uses its live save directory: " + GameIO.GetSaveGameDir(), senderInfo);
                return;
            }

            DimensionDefinition definition;
            if (!DimensionRegistry.TryGet(dimensionId, out definition))
            {
                Output("No registered dimension named '" + dimensionId + "'.", senderInfo);
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("Region storage for " + definition.DisplayName + " (" + definition.Id + "):");
            AppendRegionStatus(report, definition.Id);
            Output(report.ToString().TrimEnd(), senderInfo);
        }

        private static void AppendRegionStatus(StringBuilder report, string dimensionId)
        {
            string saveDirectory;
            string availabilityMessage;
            if (!TryGetDimensionSaveDirectory(dimensionId, out saveDirectory, out availabilityMessage))
            {
                report.AppendLine("Storage: " + availabilityMessage);
                return;
            }

            string regionDirectory = Path.Combine(saveDirectory, "Region");
            string backupsDirectory = Path.Combine(saveDirectory, "GeneratedTerrainBackups");
            try
            {
                int regionFileCount = Directory.Exists(regionDirectory)
                    ? Directory.GetFiles(regionDirectory, "*", SearchOption.TopDirectoryOnly).Length
                    : 0;
                int backupCount = Directory.Exists(backupsDirectory)
                    ? Directory.GetDirectories(backupsDirectory, "*", SearchOption.TopDirectoryOnly).Length
                    : 0;
                report.AppendLine("Save directory: " + saveDirectory);
                report.AppendLine("Region files: " + regionFileCount + " | terrain archives: " + backupCount);
            }
            catch (Exception exception)
            {
                report.AppendLine("Storage inspection failed: " + exception.Message);
            }
        }

        private static bool TryGetDimensionSaveDirectory(string dimensionId, out string saveDirectory, out string availabilityMessage)
        {
            saveDirectory = "";
            availabilityMessage = "";
            if (dimensionId.Equals(DimensionManager.ActiveDimensionId, StringComparison.OrdinalIgnoreCase) &&
                !DimensionManager.IsOverworld(dimensionId))
            {
                saveDirectory = GameIO.GetSaveGameDir();
                if (Directory.Exists(saveDirectory)) return true;

                availabilityMessage = "The active dimension save directory does not exist yet.";
                return false;
            }

            if (!DimensionManager.IsOverworld(DimensionManager.ActiveDimensionId))
            {
                availabilityMessage = "Return to the Overworld to inspect another dimension's storage.";
                return false;
            }

            if (DimensionStorage.TryGetDimensionSaveDirectory(GameIO.GetSaveGameDir(), dimensionId, out saveDirectory)) return true;

            availabilityMessage = "No saved dimension directory exists yet. Enter it once to create one.";
            return false;
        }

        private static void OutputPosition(CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            Vector3 position = player.position;
            Vector3i blockPosition = new Vector3i(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.y),
                Mathf.FloorToInt(position.z));
            int chunkX = World.toChunkXZ(blockPosition.x);
            int chunkZ = World.toChunkXZ(blockPosition.z);
            Output("Dimension: " + DimensionManager.ActiveDimensionId +
                " | Position: " + position.x.ToString("F3") + ", " + position.y.ToString("F3") + ", " + position.z.ToString("F3") +
                " | Block: " + blockPosition.x + ", " + blockPosition.y + ", " + blockPosition.z +
                " | Chunk: " + chunkX + ", " + chunkZ, senderInfo);
        }

        private static void OutputChunk(List<string> parameters, CommandSenderInfo senderInfo)
        {
            if (parameters.Count != 1 && parameters.Count != 3)
            {
                Output("Usage: lizziesdebug chunk [chunk-x chunk-z]", senderInfo);
                return;
            }

            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            int chunkX;
            int chunkZ;
            if (parameters.Count == 3)
            {
                if (!int.TryParse(parameters[1], out chunkX) || !int.TryParse(parameters[2], out chunkZ))
                {
                    Output("Chunk coordinates must be integers.", senderInfo);
                    return;
                }
            }
            else
            {
                chunkX = World.toChunkXZ(Mathf.FloorToInt(player.position.x));
                chunkZ = World.toChunkXZ(Mathf.FloorToInt(player.position.z));
            }

            Chunk chunk = world.ChunkCache != null ? world.ChunkCache.GetChunkSync(chunkX, chunkZ) : null;
            if (chunk == null)
            {
                Output("Chunk (" + chunkX + ", " + chunkZ + ") is not loaded in the active cache.", senderInfo);
                return;
            }

            Output("Chunk (" + chunk.X + ", " + chunk.Z + ") | collision=" + chunk.IsCollisionMeshGenerated +
                " | needs regeneration=" + chunk.NeedsRegeneration +
                " | needs light=" + chunk.NeedsLightCalculation +
                " | needs decoration=" + chunk.NeedsDecoration +
                " | modified=" + chunk.isModified, senderInfo);
        }

        private static void OutputSettings(string requestedModName, CommandSenderInfo senderInfo)
        {
            if (string.IsNullOrEmpty(requestedModName))
            {
                List<string> modNames = new List<string>(ModSettingsManager.AllModSettings.Keys);
                modNames.Sort(StringComparer.OrdinalIgnoreCase);
                StringBuilder report = new StringBuilder();
                report.AppendLine("Settings: developer mode=" + ModSettingsManager.IsDeveloperMode +
                    " | restart pending=" + ModSettingsManager.PendingRestart);
                foreach (string modName in modNames)
                {
                    report.AppendLine("- " + modName + " | settings=" + ModSettingsManager.AllModSettings[modName].Count);
                }

                report.AppendLine("Use lizziesdebug settings <mod-name> for effective values.");
                Output(report.ToString().TrimEnd(), senderInfo);
                return;
            }

            string matchedModName = null;
            foreach (string modName in ModSettingsManager.AllModSettings.Keys)
            {
                if (!modName.Equals(requestedModName, StringComparison.OrdinalIgnoreCase)) continue;

                matchedModName = modName;
                break;
            }

            if (matchedModName == null)
            {
                Output("No settings are loaded for '" + requestedModName + "'.", senderInfo);
                return;
            }

            List<ModSetting> settings = new List<ModSetting>(ModSettingsManager.AllModSettings[matchedModName]);
            settings.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            StringBuilder values = new StringBuilder();
            values.AppendLine("Effective settings for " + matchedModName + ":");
            foreach (ModSetting setting in settings)
            {
                values.AppendLine("- " + setting.Name + "=" + setting.Value +
                    " | persisted=" + setting.ValueForPersistence +
                    " | type=" + setting.Type +
                    " | flags=" + GetSettingFlags(setting));
            }

            Output(values.ToString().TrimEnd(), senderInfo);
        }

        private static string GetSettingFlags(ModSetting setting)
        {
            List<string> flags = new List<string>();
            if (setting.IsDeveloperDefined) flags.Add("dev-defined");
            if (setting.IsDeveloperOverridden) flags.Add("dev-override");
            if (setting.requiresRestart) flags.Add("restart");
            if (setting.Hidden) flags.Add("hidden");
            if (setting.ServerOnly) flags.Add("server-only");
            if (setting.inMenuOnly) flags.Add("menu-only");
            if (setting.Warning) flags.Add("warning");
            return flags.Count == 0 ? "none" : string.Join(",", flags.ToArray());
        }

        private static void OutputDiagnostics(CommandSenderInfo senderInfo)
        {
            int errorCount;
            int warningCount;
            ModErrorHandler.GetDiagnosticCounts(out errorCount, out warningCount);
            List<string> problematicMods = ModErrorHandler.GetProblematicMods();
            string problemModsText = problematicMods.Count == 0 ? "none" : string.Join(", ", problematicMods.ToArray());
            Output("XML diagnostics: errors=" + errorCount + " | warnings=" + warningCount +
                " | problematic mods=" + problemModsText + "\n\n" + ModErrorHandler.GetDiagnosticReport(), senderInfo);
        }

        private static void EnterDimension(string dimensionId, CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            if (!DimensionManager.IsOverworld(DimensionManager.ActiveDimensionId))
            {
                Output("Already in '" + DimensionManager.ActiveDimensionId + "'. Run lizziesdebug return first.", senderInfo);
                return;
            }

            DimensionDefinition definition;
            if (!DimensionRegistry.TryGet(dimensionId, out definition) || !definition.IsSupported)
            {
                Output("'" + dimensionId + "' is not a supported registered dimension.", senderInfo);
                return;
            }

            if (DimensionManager.TryStartDimension(player, definition.Id))
            {
                Output("Requested entry to '" + definition.DisplayName + "'.", senderInfo);
            }
            else
            {
                Output("Could not start the transition. Check the on-screen tooltip and lizziesdebug status.", senderInfo);
            }
        }

        private static void ReturnToOverworld(CommandSenderInfo senderInfo)
        {
            EntityPlayerLocal player;
            World world;
            if (!TryGetPlayerAndWorld(out player, out world, senderInfo)) return;

            if (DimensionManager.IsOverworld(DimensionManager.ActiveDimensionId))
            {
                Output("Already in the Overworld.", senderInfo);
                return;
            }

            if (DimensionManager.TryStartDimension(player, null))
            {
                Output("Requested return to the Overworld.", senderInfo);
            }
            else
            {
                Output("Could not start the return transition. Check the on-screen tooltip and lizziesdebug status.", senderInfo);
            }
        }

        private static bool TryGetPlayerAndWorld(out EntityPlayerLocal player, out World world, CommandSenderInfo senderInfo)
        {
            world = GameManager.Instance != null ? GameManager.Instance.World : null;
            player = world != null ? world.GetPrimaryPlayer() : null;
            if (player != null) return true;

            Output("Load a local save before running this operation.", senderInfo);
            return false;
        }

        private static void Output(string message, CommandSenderInfo senderInfo)
        {
            Logger.Info("[LizziesDebug] " + message);
            if (SdtdConsole.Instance != null)
            {
                SdtdConsole.Instance.Output(message, senderInfo);
            }
        }
    }
}