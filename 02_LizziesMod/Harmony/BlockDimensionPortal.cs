namespace LizziesMod
{
    public class BlockDimensionalPortal : Block
    {
        private new BlockActivationCommand[] cmds = new BlockActivationCommand[]
        {
            new BlockActivationCommand("use", "electric_switch", true)
        };

        public override bool HasBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
        {
            return true;
        }

        public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
        {
            return cmds;
        }

        public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
        {
            return DimensionManager.GetPortalActivationText();
        }

        public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
        {
            if (_commandName != "use") return false;

            return DimensionManager.TryStartConfiguredDimension(_player);
        }
    }
}