using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class NetPackageJukeboxLibrary : NetPackage
    {
        private Vector3 position;
        private List<string> trackIds = new List<string>();
        private int price;
        private bool isOwner;

        public override NetPackageDirection PackageDirection
        {
            get { return NetPackageDirection.ToClient; }
        }

        public NetPackageJukeboxLibrary Setup(Vector3 jukeboxPosition, List<string> unlockedTrackIds, int playbackPrice, bool playerIsOwner)
        {
            position = jukeboxPosition;
            trackIds = unlockedTrackIds == null ? new List<string>() : new List<string>(unlockedTrackIds);
            price = playbackPrice;
            isOwner = playerIsOwner;
            return this;
        }

        public override void read(PooledBinaryReader reader)
        {
            position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            price = reader.ReadInt32();
            isOwner = reader.ReadBoolean();
            int count = reader.ReadInt32();
            trackIds.Clear();
            for (int index = 0; index < count; index++) trackIds.Add(reader.ReadString());
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            writer.Write(price);
            writer.Write(isOwner);
            writer.Write(trackIds.Count);
            foreach (string trackId in trackIds) writer.Write(trackId ?? "");
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;

            JukeboxUIController.ReceiveLibrarySnapshot(position, trackIds, price, isOwner);
        }

        public override int GetLength()
        {
            int length = 21;
            foreach (string trackId in trackIds) length += trackId == null ? 0 : trackId.Length;
            return length;
        }
    }
}