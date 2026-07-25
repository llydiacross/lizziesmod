using HarmonyLib;
using InControl;
using UnityEngine;

namespace LizziesMod
{

    public class DimensionManager
    {
        public static string currentDimension = "Overworld";
        public static void SetCurrentDimension(string dimension)
        {
            currentDimension = dimension;
            Logger.Info($"[DimensionManager] Current dimension set to: {currentDimension}");
        }
        public static string GetCurrentDimension()
        {
            return currentDimension;
        }
    }

    [HarmonyPatch(typeof(ChunkProviderGenerateWorld), "GenerateSingleChunk")]
    public class HellDimension_ChunkGen_Patch
    {
        public static void Postfix(ChunkCluster cc, long key)
        {


            Chunk chunk = cc.GetChunkSync(key);
            if (chunk == null || TimeManager.currentDimension != "Hell") return;

            int idDirt = Block.GetBlockByName("terrDirt", true).blockID;
            int idGrass = Block.GetBlockByName("terrForestGround", true).blockID;
            int idSnow = Block.GetBlockByName("terrSnow", true).blockID;
            int idSand = Block.GetBlockByName("terrDesertGround", true).blockID;
            int idStone = Block.GetBlockByName("terrStone", true).blockID;
            int idWater = Block.GetBlockByName("water", true).blockID;

            BlockValue hellGround = Block.GetBlockValue("terrBurntForestGround");
            BlockValue hellStone = Block.GetBlockValue("terrDestroyedStone");


            BlockValue hellLiquid = Block.GetBlockValue("terrDestroyedWoodDebris");


            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {

                    int terrainHeight = chunk.GetTerrainHeight(x, z);

                    for (int y = 0; y <= terrainHeight; y++)
                    {
                        BlockValue currentBlock = chunk.GetBlock(x, y, z);

                        if (currentBlock.type == idGrass || currentBlock.type == idDirt ||
                            currentBlock.type == idSnow || currentBlock.type == idSand)
                        {
                            chunk.SetBlock(GameManager.Instance.World, x, y, z, hellGround);
                        }

                        else if (currentBlock.type == idStone)
                        {
                            chunk.SetBlock(GameManager.Instance.World, x, y, z, hellStone);
                        }

                        else if (currentBlock.type == idWater)
                        {
                            chunk.SetBlock(GameManager.Instance.World, x, y, z, hellLiquid);
                        }
                    }
                }
            }

            Logger.Info($"[DimensionManager] Successfully corrupted Chunk ({chunk.X}, {chunk.Z}).");
        }
    }
}