using System.Collections.Generic;

namespace LizziesMod
{
    public class ConsoleCmdRegenerateDimension : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new[] { "lizziesregendimension" };
        }

        public override string getDescription()
        {
            return "Archives and regenerates a generated dimension's terrain while the player is in the Overworld.";
        }

        public override string getHelp()
        {
            return "Usage:\n  lizziesregendimension <dimension-id>\n\nExample:\n  lizziesregendimension PocketDimension";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            if (_params == null || _params.Count != 1)
            {
                SdtdConsole.Instance.Output(getHelp(), _senderInfo);
                return;
            }

            if (GameManager.Instance?.World?.GetPrimaryPlayer() == null)
            {
                SdtdConsole.Instance.Output("Load an Overworld save before regenerating a dimension.", _senderInfo);
                return;
            }

            if (!DimensionManager.IsOverworld(DimensionManager.ActiveDimensionId))
            {
                SdtdConsole.Instance.Output("Return to the Overworld before regenerating dimension terrain.", _senderInfo);
                return;
            }

            string dimensionId = _params[0];
            DimensionDefinition definition;
            DimensionGeneratorDefinition generator;
            if (!DimensionRegistry.TryGet(dimensionId, out definition) ||
                !DimensionGeneratorRegistry.TryGet(definition.GeneratorId, out generator) ||
                generator.SaveMode != DimensionSaveMode.Generated)
            {
                SdtdConsole.Instance.Output($"'{dimensionId}' is not a registered generated dimension.", _senderInfo);
                return;
            }

            string overworldSaveDirectory = GameIO.GetSaveGameDir();
            if (!ModSaveManager.BackupSaveDirectory(overworldSaveDirectory, "Before regenerating " + definition.DisplayName))
            {
                SdtdConsole.Instance.Output("Could not create an Overworld backup; terrain was not changed.", _senderInfo);
                return;
            }

            string archivedRegionDirectory;
            string error;
            if (!DimensionStorage.TryRegenerateGeneratedDimensionTerrain(
                overworldSaveDirectory,
                definition.Id,
                out archivedRegionDirectory,
                out error))
            {
                SdtdConsole.Instance.Output("Could not regenerate terrain: " + error, _senderInfo);
                return;
            }

            string archiveMessage = string.IsNullOrEmpty(archivedRegionDirectory)
                ? "No prior Region directory was present."
                : "Previous Region archived at " + archivedRegionDirectory + ".";
            string message = $"Regenerated '{definition.DisplayName}'. {archiveMessage} Enter the dimension again to generate fresh terrain.";
            Logger.Info("[DimensionManager] " + message);
            SdtdConsole.Instance.Output(message, _senderInfo);
        }
    }
}