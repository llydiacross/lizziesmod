using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class ConsoleCmdGetPosition : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new[] { "getpos" };
        }

        public override string getDescription()
        {
            return "Shows the local player's world and block coordinates.";
        }

        public override string getHelp()
        {
            return "Usage:\n  getpos";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null)
            {
                SdtdConsole.Instance.Output("No local player is available.", _senderInfo);
                return;
            }

            Vector3 position = player.position;
            Vector3i blockPosition = new Vector3i(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.y),
                Mathf.FloorToInt(position.z));

            string message = $"Position: {position.x:F3}, {position.y:F3}, {position.z:F3} | Block: {blockPosition.x}, {blockPosition.y}, {blockPosition.z}";
            Logger.Info("[GetPos] " + message);
            SdtdConsole.Instance.Output(message, _senderInfo);
        }
    }
}