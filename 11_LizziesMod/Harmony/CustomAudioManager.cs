using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace LizziesMod
{
    public class AudioTrack
    {
        public string FilePath;
        public string Artist;
        public string Title;
        public string ModSource;
    }

    public class CustomAudioManager : MonoBehaviour
    {
        public static CustomAudioManager Instance;
        private Dictionary<string, AudioTrack> availableAudio = new Dictionary<string, AudioTrack>(StringComparer.OrdinalIgnoreCase);

        private AudioSource walkmanSource;
        private string currentlyPlayingTrack = "";

        public void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            walkmanSource = gameObject.AddComponent<AudioSource>();
            walkmanSource.spatialBlend = 0f;
            walkmanSource.volume = 0.5f;

            ScanForAudio();
        }


        public Dictionary<string, AudioTrack> GetAvailableAudio()
        {
            return availableAudio;
        }

        private void ScanForAudio()
        {
            availableAudio.Clear();

            foreach (Mod mod in ModManager.GetLoadedMods())
            {
                string audioDir = Path.Combine(mod.Path, "CustomAudio");
                if (Directory.Exists(audioDir))
                {
                    string[] files = Directory.GetFiles(audioDir, "*.*", SearchOption.AllDirectories);
                    foreach (string file in files)
                    {
                        if (file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);

                   
                            string artistName = "Unknown Artist";
                            string trackTitle = fileName;
  
                            int splitIndex = fileName.IndexOf(" - ");
                            if (splitIndex != -1)
                            {
                                artistName = fileName.Substring(0, splitIndex).Trim();
                                trackTitle = fileName.Substring(splitIndex + 3).Trim();
                            }

                            string namespacedKey = $"{mod.Name}:{fileName}";

                            AudioTrack newTrack = new AudioTrack()
                            {
                                FilePath = file,
                                Artist = artistName,
                                Title = trackTitle,
                                ModSource = mod.Name
                            };

                            availableAudio[namespacedKey] = newTrack;
                            Logger.Info($"[CustomAudioManager] Indexed: '{newTrack.Title}' by '{newTrack.Artist}' from {mod.Name}");
                        }
                    }
                }
            }
        }

        public void PlayJukeboxTrack(Vector3 position, string trackName)
        {

            if (availableAudio.TryGetValue(trackName, out AudioTrack trackData))
            {
    
                GameObject speakerGO = new GameObject($"JukeboxSpeaker_{trackName}");
                speakerGO.transform.position = position;

    
                AudioSource source = speakerGO.AddComponent<AudioSource>();
                source.spatialBlend = 1f; 
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = ModSettingsManager.GetSetting<int>("LizziesMod_JukeboxAndWalkman", "MinJukeboxDistance", 2); 
                source.maxDistance = ModSettingsManager.GetSetting<int>("LizziesMod_JukeboxAndWalkman", "MaxJukeboxDistance", 25);
                source.volume = 1f;
                StartCoroutine(StreamAndPlay3D(trackData.FilePath, source, speakerGO));
            }
            else
            {
                Logger.Warning($"[CustomAudioManager] Jukebox Track not found: {trackName}");
            }
        }

        private IEnumerator StreamAndPlay3D(string filePath, AudioSource source, GameObject speakerGO)
        {
            string uri = "file:///" + filePath.Replace("\\", "/");

            AudioType audioType = AudioType.UNKNOWN;
            if (filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) audioType = AudioType.OGGVORBIS;
            else if (filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) audioType = AudioType.WAV;
            else if (filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) audioType = AudioType.MPEG;

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    source.clip = clip;
                    source.Play();
                    Destroy(speakerGO, clip.length);
                }
                else
                {
                    Logger.Error($"[CustomAudioManager] Failed to load 3D audio: {www.error}");
                    Destroy(speakerGO);
                }
            }
        }

        public void ToggleWalkmanTrack(string trackName, EntityPlayerLocal player)
        {
            if (walkmanSource.isPlaying && currentlyPlayingTrack == trackName)
            {
                walkmanSource.Stop();
                currentlyPlayingTrack = "";
                GameManager.ShowTooltip(player, "Walkman: [FF0000]Stopped[-]");
                return;
            }

            if (availableAudio.TryGetValue(trackName, out AudioTrack trackData))
            {
                string message = $"Now Playing: [00FF00]{trackData.Title}[-] by [FFFF00]{trackData.Artist}[-]";
                GameManager.ShowTooltip(player, message);
                StartCoroutine(StreamAndPlay(trackData.FilePath, trackName));
            }
            else
            {
                GameManager.ShowTooltip(player, $"[FF0000]Track corrupted or missing: {trackName}[-]");
                Logger.Warning($"[CustomAudioManager] Track not found: {trackName}");
            }
        }

        private IEnumerator StreamAndPlay(string filePath, string trackName)
        {
            if (walkmanSource.isPlaying) walkmanSource.Stop();

            string uri = "file:///" + filePath.Replace("\\", "/");

            AudioType audioType = AudioType.UNKNOWN;
            if (filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) audioType = AudioType.OGGVORBIS;
            else if (filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) audioType = AudioType.WAV;
            else if (filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) audioType = AudioType.MPEG;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    walkmanSource.clip = clip;
                    walkmanSource.Play();
                    currentlyPlayingTrack = trackName;
                    Logger.Info($"[CustomAudioManager] Now playing Walkman: {trackName}");
                }
                else
                {
                    Logger.Error($"[CustomAudioManager] Failed to load audio: {www.error}");
                }
            }
        }
    }
}