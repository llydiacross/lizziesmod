using LizziesMod;

namespace LizziesMod.Backrooms
{
    public class BlockBackroomsPortal : Block
    {
        private static readonly BlockActivationCommand[] Commands =
        {
            new BlockActivationCommand("use", "electric_switch", true)
        };

        public override bool HasBlockActivationCommands(WorldBase world, BlockValue blockValue, Vector3i blockPosition, EntityAlive entityFocusing)
        {
            return true;
        }

        public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world, BlockValue blockValue, Vector3i blockPosition, EntityAlive entityFocusing)
        {
            return Commands;
        }

        public override string GetActivationText(WorldBase world, BlockValue blockValue, Vector3i blockPosition, EntityAlive player)
        {
            return DimensionManager.GetDimensionActivationText("Backrooms");
        }

        public override bool OnBlockActivated(string commandName, WorldBase world, Vector3i blockPosition, BlockValue blockValue, EntityPlayerLocal player)
        {
            return commandName == "use" && DimensionManager.TryStartDimension(player, "Backrooms");
        }
    }
}