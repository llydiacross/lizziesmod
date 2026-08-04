using LizziesMod;

namespace LizziesMod.PocketDimension
{
    public class Main
    {
        public class Init : IModApi
        {
            public void InitMod(Mod modInstance)
            {
                if (!DimensionGeneratorRegistry.Register(new DimensionGeneratorDefinition(
                    PocketDimensionGenerator.GeneratorId,
                    DimensionSaveMode.Generated,
                    PocketDimensionGenerator.GetEntryPosition,
                    PocketDimensionGenerator.Generate)))
                {
                    Logger.Error("[PocketDimension] Generator registration failed; definitions were not loaded.");
                    return;
                }

                DimensionRegistry.LoadDefinitions(modInstance);
                Logger.Info("[PocketDimension] Registered generated dimension support.");
            }
        }
    }
}