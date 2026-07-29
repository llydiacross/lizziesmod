using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;

namespace LizziesMod
{
    public class AudioTrack
    {
        public string Id;
        public string FilePath;
        public string Artist;
        public string Title;
        public string Album;
        public int TrackNumber;
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

        public bool HasTrack(string trackId)
        {
            return availableAudio.ContainsKey(trackId ?? "");
        }

        private void ScanForAudio()
        {
            availableAudio.Clear();
            int trackCount = 0;

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                if (mod != null)
                {
                    trackCount += LoadTracksFromMod(mod);
                }
            }

            Logger.Info($"[CustomAudioManager] Loaded {trackCount} declared custom audio track(s).");
        }

        private int LoadTracksFromMod(Mod mod)
        {
            if (string.IsNullOrEmpty(mod.Path)) return 0;

            string configPath = Path.Combine(mod.Path, "Config", "CustomAudio.xml");
            if (!File.Exists(configPath)) return 0;

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(configPath);
                if (document.DocumentElement == null || document.DocumentElement.Name != "CustomAudio")
                {
                    ReportAudioConfigError(mod.Name, "CustomAudio.xml must use <CustomAudio> as its root element.");
                    return 0;
                }

                XmlNodeList trackNodes = document.SelectNodes("/CustomAudio/Track");
                if (trackNodes == null) return 0;

                int trackCount = 0;
                foreach (XmlNode trackNode in trackNodes)
                {
                    AudioTrack track;
                    string error;
                    if (!TryCreateTrack(mod, trackNode, out track, out error))
                    {
                        ReportAudioConfigError(mod.Name, "CustomAudio.xml: " + error);
                        continue;
                    }

                    if (availableAudio.ContainsKey(track.Id))
                    {
                        ReportAudioConfigError(mod.Name, $"CustomAudio.xml: track id '{track.Id}' is declared more than once.");
                        continue;
                    }

                    availableAudio.Add(track.Id, track);
                    trackCount++;
                    Logger.Info($"[CustomAudioManager] Indexed: '{track.Title}' by '{track.Artist}' from {mod.Name}");
                }

                return trackCount;
            }
            catch (Exception exception)
            {
                ReportAudioConfigError(mod.Name, "CustomAudio.xml: " + exception.Message);
                return 0;
            }
        }

        private static bool TryCreateTrack(Mod mod, XmlNode trackNode, out AudioTrack track, out string error)
        {
            track = null;
            error = null;

            string id = trackNode.Attributes?["id"]?.Value?.Trim();
            string file = trackNode.Attributes?["file"]?.Value?.Trim();
            string title = trackNode.Attributes?["title"]?.Value?.Trim();
            string artist = trackNode.Attributes?["artist"]?.Value?.Trim();
            string album = trackNode.Attributes?["album"]?.Value?.Trim() ?? "";
            string trackNumberText = trackNode.Attributes?["track_number"]?.Value?.Trim();

            if (string.IsNullOrEmpty(id))
            {
                error = "each <Track> requires a non-empty id attribute.";
                return false;
            }

            if (id.IndexOf(':') >= 0)
            {
                error = $"track id '{id}' cannot contain ':', because track IDs are namespaced by their mod.";
                return false;
            }

            if (string.IsNullOrEmpty(file))
            {
                error = $"track '{id}' requires a non-empty file attribute.";
                return false;
            }

            string resolvedFile;
            if (!TryResolveTrackFile(mod.Path, file, out resolvedFile))
            {
                error = $"track '{id}' must reference a file inside its owning mod folder.";
                return false;
            }

            if (!IsSupportedAudioFile(resolvedFile))
            {
                error = $"track '{id}' must reference an .ogg, .wav, or .mp3 file.";
                return false;
            }

            if (!File.Exists(resolvedFile))
            {
                error = $"track '{id}' references missing file '{file}'.";
                return false;
            }

            int trackNumber = 0;
            if (!string.IsNullOrEmpty(trackNumberText) &&
                (!int.TryParse(trackNumberText, out trackNumber) || trackNumber < 0))
            {
                error = $"track '{id}' has an invalid non-negative track_number attribute.";
                return false;
            }

            track = new AudioTrack
            {
                Id = mod.Name + ":" + id,
                FilePath = resolvedFile,
                Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                Title = string.IsNullOrEmpty(title) ? id : title,
                Album = album,
                TrackNumber = trackNumber,
                ModSource = mod.Name
            };
            return true;
        }

        private static bool TryResolveTrackFile(string modPath, string configuredPath, out string resolvedPath)
        {
            resolvedPath = "";
            if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(configuredPath)) return false;

            string rootPath = Path.GetFullPath(modPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidatePath = Path.GetFullPath(Path.Combine(rootPath, configuredPath));
            string rootPrefix = rootPath + Path.DirectorySeparatorChar;
            if (!candidatePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) return false;

            resolvedPath = candidatePath;
            return true;
        }

        private static bool IsSupportedAudioFile(string filePath)
        {
            return filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                   filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                   filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReportAudioConfigError(string modName, string message)
        {
            Logger.Error($"[CustomAudioManager] [{modName}] {message}");
            ModErrorHandler.ReportXmlError(modName, message);
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