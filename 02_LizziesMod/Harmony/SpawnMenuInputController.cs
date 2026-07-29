namespace LizziesMod
{
    public static class SpawnMenuInputController
    {
        private const string OpenInputName = "openSpawnMenu";
        private static bool isRegistered;

        public static void Register()
        {
            if (isRegistered) return;

            isRegistered = true;
            CustomInputManager.Subscribe(
                SpawnMenuManager.ModName,
                OpenInputName,
                CustomInputTrigger.Pressed,
                OpenSpawnMenu);
        }

        private static void OpenSpawnMenu()
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || player.playerUI == null || !SpawnMenuManager.CanUse(player)) return;

            XUi xui = player.playerUI.xui;
            if (xui == null) return;

            if (SpawnMenuUIController.IsSpawnMenuOpen) return;

            xui.playerUI.windowManager.Open(SpawnMenuUIController.WindowName, true);
        }
    }
}