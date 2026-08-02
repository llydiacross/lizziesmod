using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace LizziesMod
{
    public sealed class JukeboxLibraryState
    {
        public string OwnerId = "";
        public int Price;
        public readonly HashSet<string> UnlockedTrackIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class JukeboxLibraryManager
    {
        private const string FileName = "JukeboxLibraries.xml";
        private const int MaxPrice = 10000;
        private static readonly Dictionary<string, JukeboxLibraryState> librariesByPosition =
            new Dictionary<string, JukeboxLibraryState>(StringComparer.Ordinal);
        private static string loadedSaveDirectory = "";

        public static JukeboxLibraryState GetLibrary(Vector3i position)
        {
            EnsureLoaded();

            string key = GetPositionKey(position);
            JukeboxLibraryState state;
            if (!librariesByPosition.TryGetValue(key, out state))
            {
                state = new JukeboxLibraryState();
                librariesByPosition.Add(key, state);
            }

            return state;
        }

        public static bool TryUnlockTrack(Vector3i position, string ownerId, string trackId)
        {
            if (string.IsNullOrEmpty(trackId)) return false;

            JukeboxLibraryState state = GetLibrary(position);
            if (state.UnlockedTrackIds.Contains(trackId)) return false;

            if (string.IsNullOrEmpty(state.OwnerId)) state.OwnerId = ownerId ?? "";
            state.UnlockedTrackIds.Add(trackId);
            Save();
            return true;
        }

        public static bool IsOwner(Vector3i position, string playerId)
        {
            JukeboxLibraryState state = GetLibrary(position);
            return !string.IsNullOrEmpty(state.OwnerId) &&
                   state.OwnerId.Equals(playerId ?? "", StringComparison.Ordinal);
        }

        public static bool TrySetPrice(Vector3i position, string playerId, int price)
        {
            if (price < 0 || price > MaxPrice || !IsOwner(position, playerId)) return false;

            JukeboxLibraryState state = GetLibrary(position);
            if (state.Price == price) return true;

            state.Price = price;
            Save();
            return true;
        }

        public static List<string> GetUnlockedTrackIds(Vector3i position)
        {
            JukeboxLibraryState state = GetLibrary(position);
            List<string> trackIds = new List<string>(state.UnlockedTrackIds);
            trackIds.Sort(StringComparer.OrdinalIgnoreCase);
            return trackIds;
        }

        private static void EnsureLoaded()
        {
            string saveDirectory = GameIO.GetSaveGameDir() ?? "";
            if (saveDirectory.Equals(loadedSaveDirectory, StringComparison.OrdinalIgnoreCase)) return;

            librariesByPosition.Clear();
            loadedSaveDirectory = saveDirectory;
            if (string.IsNullOrEmpty(saveDirectory)) return;

            string path = Path.Combine(saveDirectory, FileName);
            if (!File.Exists(path)) return;

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlNodeList libraryNodes = document.SelectNodes("/JukeboxLibraries/Library");
                if (libraryNodes == null) return;

                foreach (XmlNode libraryNode in libraryNodes)
                {
                    string position = libraryNode.Attributes?["position"]?.Value;
                    if (string.IsNullOrEmpty(position) || librariesByPosition.ContainsKey(position)) continue;

                    int price;
                    int.TryParse(libraryNode.Attributes?["price"]?.Value, out price);

                    JukeboxLibraryState state = new JukeboxLibraryState
                    {
                        OwnerId = libraryNode.Attributes?["ownerId"]?.Value ?? "",
                        Price = Math.Max(0, Math.Min(MaxPrice, price))
                    };

                    foreach (XmlNode trackNode in libraryNode.SelectNodes("Track"))
                    {
                        string trackId = trackNode.Attributes?["id"]?.Value;
                        if (!string.IsNullOrEmpty(trackId)) state.UnlockedTrackIds.Add(trackId);
                    }

                    librariesByPosition.Add(position, state);
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[Jukebox] Failed to load library data: {exception.Message}");
            }
        }

        private static void Save()
        {
            if (string.IsNullOrEmpty(loadedSaveDirectory)) return;

            try
            {
                Directory.CreateDirectory(loadedSaveDirectory);
                string path = Path.Combine(loadedSaveDirectory, FileName);
                using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("JukeboxLibraries");
                    writer.WriteAttributeString("version", "1");

                    foreach (KeyValuePair<string, JukeboxLibraryState> entry in librariesByPosition)
                    {
                        JukeboxLibraryState state = entry.Value;
                        writer.WriteStartElement("Library");
                        writer.WriteAttributeString("position", entry.Key);
                        writer.WriteAttributeString("ownerId", state.OwnerId ?? "");
                        writer.WriteAttributeString("price", state.Price.ToString());

                        List<string> trackIds = new List<string>(state.UnlockedTrackIds);
                        trackIds.Sort(StringComparer.OrdinalIgnoreCase);
                        foreach (string trackId in trackIds)
                        {
                            writer.WriteStartElement("Track");
                            writer.WriteAttributeString("id", trackId);
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[Jukebox] Failed to save library data: {exception.Message}");
            }
        }

        private static string GetPositionKey(Vector3i position)
        {
            return position.x + "," + position.y + "," + position.z;
        }
    }
}