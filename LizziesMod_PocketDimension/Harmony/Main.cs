using LizziesMod;

namespace LizziesMod.PocketDimension
{
    public class Main
    {
        private static readonly PocketDimensionGenerator generator = new PocketDimensionGenerator();

        public class Init : IModApi
        {
            public void InitMod(Mod modInstance)
            {
                if (!DimensionGeneratorRegistry.RegisterAndLoadDefinitions(modInstance, generator))
                {
                    Logger.Error("[PocketDimension] Generator registration failed; definitions were not loaded.");
                    return;
                }

                Logger.Info("[PocketDimension] Registered generated dimension support.");
            }
        }
    }
}