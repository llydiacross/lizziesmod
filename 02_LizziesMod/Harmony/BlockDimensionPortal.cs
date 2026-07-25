using System.Collections;
using UnityEngine;

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

            return TimeManager.currentDimension == "Hell" ? "Return to Overworld" : "Enter Hell Dimension";
        }

        public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
        {
            if (_commandName != "use") return false;

    
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer || SingletonMonoBehaviour<ConnectionManager>.Instance.ClientCount() > 0)
            {
                GameManager.ShowTooltip(_player, "Dimensional shifts are too unstable for Multiplayer! Single player only.");
                _player.PlayOneShot("ui_denied");
                return false;
            }

 
            string targetDimension = TimeManager.currentDimension == "Hell" ? "Overworld" : "Hell";

            GameManager.ShowTooltip(_player, $"Initializing portal to {targetDimension}...");
            _player.PlayOneShot("weapon_electric_charge");

            GameManager.Instance.StartCoroutine(DimensionalShiftSequence(_player, _player.position, targetDimension));

            return true;
        }

        private IEnumerator DimensionalShiftSequence(EntityPlayerLocal player, Vector3 targetPos, string targetDimension)
        {
            player.Buffs.AddBuff("buffFluxTeleporting");


            Logger.Info($"[DimensionManager] Saving current state before jumping to {targetDimension}...");
            GameManager.Instance.SaveWorld();


            player.playerUI.windowManager.Open("windowLizziesLoadingScreen", true);

            player.SetPosition(targetPos, true);
            Rigidbody playerRb = player.RootTransform.GetComponent<Rigidbody>();
            if (playerRb != null) playerRb.isKinematic = true;

            TimeManager.currentDimension = targetDimension;

            Logger.Info("[DimensionManager] Flushing Chunk Cache from RAM...");
            ChunkCluster cc = GameManager.Instance.World.ChunkCache;
            if (cc != null)
            {
                System.Collections.Generic.List<long> chunksToRemove = new System.Collections.Generic.List<long>();
                foreach (Chunk chunk in cc.GetChunkArray())
                {
                    chunksToRemove.Add(chunk.Key);
                }
                foreach (long key in chunksToRemove)
                {
                    cc.RemoveChunkSync(key);
                }
            }

            Logger.Info("[DimensionManager] Waiting for engine to pull new chunks from the hard drive...");
            yield return new WaitForSeconds(1.0f);

            int chunkX = World.toChunkXZ(Mathf.FloorToInt(targetPos.x));
            int chunkZ = World.toChunkXZ(Mathf.FloorToInt(targetPos.z));



            while (cc != null && cc.GetChunkSync(chunkX, chunkZ) == null)
            {
                Logger.Info("[DimensionManager] ...");
                yield return new WaitForSeconds(1f);
            }

            yield return new WaitForSeconds(1.5f);
            if (playerRb != null) playerRb.isKinematic = false;

            player.playerUI.windowManager.Close("windowLizziesLoadingScreen");
            player.Buffs.RemoveBuff("buffFluxTeleporting");

            player.PlayOneShot("weapon_electric_charge");
            GameManager.ShowTooltip(player, "Dimensional shift complete!");
        }
    }
}