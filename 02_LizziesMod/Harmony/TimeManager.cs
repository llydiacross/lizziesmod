using HarmonyLib;
using UnityEngine;

namespace LizziesMod
{
    public static class TimeManager
    {

        private static int startingYear = 2013;
        public static int currentYear = 0;
        public static string currentDimension = DimensionManager.currentDimension;

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
        private const float YearDisplayDuration = 3f;
        private const float YearFadeDuration = 1.5f;

        private static bool displayFadeRequested;

        private XUiV_Label lblYear;
        private XUiV_Label lblYearShadow;
        private float remainingDisplayTime = -1f;

        public static void RequestDisplayFade()
        {
            displayFadeRequested = true;
        }

        public override void Init()
        {
            base.Init();
            lblYear = GetChildById("lblHUDYear")?.viewComponent as XUiV_Label;
            lblYearShadow = GetChildById("lblHUDYearShadow")?.viewComponent as XUiV_Label;
            SetYearAlpha(0f);
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);

            if (displayFadeRequested)
            {
                displayFadeRequested = false;
                remainingDisplayTime = YearDisplayDuration + YearFadeDuration;
                SetYearAlpha(1f);
            }

            if (remainingDisplayTime > 0f)
            {
                remainingDisplayTime -= _dt;
                float alpha = remainingDisplayTime > YearFadeDuration
                    ? 1f
                    : Mathf.Clamp01(remainingDisplayTime / YearFadeDuration);
                SetYearAlpha(alpha);
            }
            else if (remainingDisplayTime != -1f)
            {
                remainingDisplayTime = -1f;
                SetYearAlpha(0f);
            }

            if (lblYear != null && GameManager.Instance.World != null)
            {
                int gameYear = TimeManager.GetGameYear();
                string gameYearText = $"{gameYear}";
                lblYear.Text = gameYearText;

                if (lblYearShadow != null)
                {
                    lblYearShadow.Text = gameYearText;
                }
            }
        }

        private void SetYearAlpha(float alpha)
        {
            if (lblYear != null)
            {
                lblYear.Color = new Color(1f, 180f / 255f, 0f, alpha);
            }

            if (lblYearShadow != null)
            {
                lblYearShadow.Color = new Color(0f, 0f, 0f, 220f / 255f * alpha);
            }
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), "OnAddedToWorld")]
    public class YearHUD_OnAddedToWorld_Patch
    {
        public static void Postfix()
        {
            YearHUDUIController.RequestDisplayFade();
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), "AfterPlayerRespawn")]
    public class YearHUD_AfterPlayerRespawn_Patch
    {
        public static void Postfix()
        {
            YearHUDUIController.RequestDisplayFade();
        }
    }

    [HarmonyPatch(typeof(AIDirectorBloodMoonComponent), "StartBloodMoon")]
    public class YearHUD_BloodMoonStarted_Patch
    {
        public static void Postfix()
        {
            YearHUDUIController.RequestDisplayFade();
        }
    }
}