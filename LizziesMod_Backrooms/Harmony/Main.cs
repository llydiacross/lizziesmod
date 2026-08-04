using LizziesMod;

namespace LizziesMod.Backrooms
{
    public class Main
    {
        public class Init : IModApi
        {
            public void InitMod(Mod modInstance)
            {
                if (!DimensionGeneratorRegistry.Register(new DimensionGeneratorDefinition(
                    BackroomsChunkGenerator.GeneratorId,
                    DimensionSaveMode.Generated,
                    BackroomsChunkGenerator.GetEntryPosition,
                    BackroomsChunkGenerator.Generate,
                    BackroomsChunkGenerator.ProcessDeferredPainting)))
                {
                    Logger.Error("[Backrooms] Generator registration failed; definitions were not loaded.");
                    return;
                }

                DimensionRegistry.LoadDefinitions(modInstance);
                Logger.Info("[Backrooms] Registered generated dimension support.");
            }
        }
    }
}