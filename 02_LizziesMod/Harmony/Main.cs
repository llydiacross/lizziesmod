using HarmonyLib;
using LizziesMod.Harmony;
using UnityEngine;

namespace LizziesMod
{
    public class Main
    {
        public static bool IsPlayerInGame()
        {
            return GameManager.Instance != null && GameManager.Instance.World != null;
        }

        public class Init : IModApi
        {
            public void InitMod(Mod modInstance)
            {

                // Load our custom changes
                ModTextures.Init(modInstance);

                // Load Mod Settings Manager so we get our settings first
                ModSettingsManager.LoadAllModSettings();

                // Register Callbacks for mod manager (must be after we load settings)
                ModSettingsManager.RegisterCallback("LizziesMod", "StartingYear", (newValue) =>
                {
                    if (int.TryParse(newValue, out int parsed))
                    {
                        TimeManager.Init(parsed);
                        try
                        {
                            TimeManager.UpdateCurrentYear();
                        }
                        catch
                        {
                            Logger.Warning("Failed to update starting year");
                        }
                    }
                });

                // Create the audio manager game object and add the CustomAudioManager component
                GameObject audioManagerGO = new GameObject("LizziesAudioManager");
                audioManagerGO.AddComponent<CustomAudioManager>();

                // Patch Harmony
                const string id = "uk.co.llydia.7daystodie.mods.lizziesmod";
                var harmony = new HarmonyLib.Harmony(id);
                harmony.PatchAll();

                // Initialize ModManager to block other mods based on settings
                ModManager.Initialize(harmony);

                Logger.Info("Success");
            }
        }
    }
}