using UnityEngine;

namespace LizziesMod
{
    public class BlockJukebox : Block
    {

        private BlockActivationCommand[] cmds = new BlockActivationCommand[]
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
            return "Use Jukebox";
        }

     
        public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
        {
            
            if (_commandName != "use") return false;

            TileEntityPowered te = _world.GetTileEntity(_blockPos) as TileEntityPowered;

            if (te != null && !te.IsPowered)
            {
                GameManager.ShowTooltip(_player, "The Jukebox needs electrical power!");
                _player.PlayOneShot("ui_denied");
                return false;
            }
            JukeboxUIController.CurrentJukeboxPosition = _blockPos.ToVector3();
            _player.playerUI.windowManager.Open("windowJukebox", true);

            return true;
        }
    }
}