using LizziesMod;

namespace LizziesMod.Backrooms
{
    public class Main
    {
        private static readonly BackroomsChunkGenerator generator = new BackroomsChunkGenerator();

        public class Init : IModApi
        {
            public void InitMod(Mod modInstance)
            {
                if (!DimensionGeneratorRegistry.RegisterAndLoadDefinitions(modInstance, generator))
                {
                    Logger.Error("[Backrooms] Generator registration failed; definitions were not loaded.");
                    return;
                }

                Logger.Info("[Backrooms] Registered generated dimension support.");
            }
        }
    }
}