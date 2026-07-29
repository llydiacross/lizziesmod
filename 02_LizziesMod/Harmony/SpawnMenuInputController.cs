using UnityEngine;

namespace LizziesMod
{
    public class SpawnMenuInputController : MonoBehaviour
    {
        private const KeyCode SpawnMenuKey = KeyCode.P;
        private static bool isInitialized;

        public static void Initialize()
        {
            if (isInitialized) return;

            isInitialized = true;
            GameObject inputObject = new GameObject("LizziesSpawnMenuInput");
            DontDestroyOnLoad(inputObject);
            inputObject.AddComponent<SpawnMenuInputController>();
        }

        private void Update()
        {
            bool controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!controlHeld || !Input.GetKeyDown(SpawnMenuKey)) return;

            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || player.playerUI == null || !SpawnMenuManager.CanUse(player)) return;

            XUi xui = player.playerUI.xui;
            if (xui == null) return;

            if (SpawnMenuUIController.IsSpawnMenuOpen) return;

            xui.playerUI.windowManager.Open(SpawnMenuUIController.WindowName, true);
        }
    }
}