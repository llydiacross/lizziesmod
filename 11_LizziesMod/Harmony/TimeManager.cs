using HarmonyLib;

namespace LizziesMod
{
    public static class TimeManager
    {

        private static int startingYear = 1973; // cannot change
        public static int currentYear = 0;

        public static void Init(int _startingYear)
        {

            Logger.Info("TimeManger Instantiated at year " + _startingYear);
            startingYear = _startingYear;
            currentYear = 0;
        }

        public static int CalculateYearFromDays(long totalDays)
        {
            return (int)(totalDays / 357);
        }

        public static int GetStartingYear()
        {
            return startingYear;
        }

        public static void UpdateCurrentYear()
        {
            long totalDays = GameUtils.WorldTimeToDays(GameManager.Instance.World.worldTime);
            currentYear = TimeManager.CalculateYearFromDays(totalDays);
        }

        public static int GetGameYear()
        {
            
            long totalDays = GameUtils.WorldTimeToDays(GameManager.Instance.World.worldTime);
            return startingYear + TimeManager.CalculateYearFromDays(totalDays);
        }
    }

    public class YearHUDUIController : XUiController
    {
        private XUiV_Label lblYear;

        public override void Init()
        {
            base.Init();
            lblYear = GetChildById("lblHUDYear")?.viewComponent as XUiV_Label;
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);

            if (lblYear != null && GameManager.Instance.World != null)
            {
                int gameYear = TimeManager.GetGameYear();
                lblYear.Text = $"{gameYear}";
            }
        }
    }
}