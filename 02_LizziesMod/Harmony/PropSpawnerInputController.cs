using UnityEngine;

namespace LizziesMod
{
    public class PropSpawnerInputController : MonoBehaviour
    {
        private static bool isInitialized;

        public static void Initialize()
        {
            if (isInitialized) return;

            isInitialized = true;
            GameObject inputObject = new GameObject("LizziesPropSpawnerInput");
            DontDestroyOnLoad(inputObject);
            inputObject.AddComponent<PropSpawnerInputController>();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Tab)) return;

            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || player.playerUI == null || !PropSpawnerManager.CanUse(player)) return;

            XUi xui = player.playerUI.xui;
            if (xui == null) return;

            if (PropSpawnerUIController.IsPropSpawnerOpen)
            {
                xui.playerUI.windowManager.Close(PropSpawnerUIController.WindowName);
                return;
            }

            if (XUi.InGameMenuOpen || LocalPlayerUI.AnyModalWindowOpen()) return;
            xui.playerUI.windowManager.Open(PropSpawnerUIController.WindowName, true);
        }
    }
}