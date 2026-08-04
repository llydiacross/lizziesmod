using UnityEngine;

namespace LizziesMod
{
    public class NetPackageJukeboxCommand : NetPackage
    {
        private JukeboxCommand command;
        private Vector3i position;
        private string trackId;
        private int slotIndex;
        private int price;

        public override NetPackageDirection PackageDirection
        {
            get { return NetPackageDirection.ToServer; }
        }

        public NetPackageJukeboxCommand Setup(JukeboxCommand commandType, Vector3i jukeboxPosition, string requestedTrackId, int selectedSlotIndex, int requestedPrice)
        {
            command = commandType;
            position = jukeboxPosition;
            trackId = requestedTrackId ?? "";
            slotIndex = selectedSlotIndex;
            price = requestedPrice;
            return this;
        }

        public override void read(PooledBinaryReader reader)
        {
            command = (JukeboxCommand)reader.ReadByte();
            position = new Vector3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            trackId = reader.ReadString();
            slotIndex = reader.ReadInt32();
            price = reader.ReadInt32();
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            writer.Write((byte)command);
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            writer.Write(trackId ?? "");
            writer.Write(slotIndex);
            writer.Write(price);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer || Sender == null) return;

            EntityPlayer player = world.GetEntity(Sender.entityId) as EntityPlayer;
            if (player == null) return;

            JukeboxManager.ProcessServerCommand(world, player, command, position, trackId, slotIndex, price);
        }

        public override int GetLength()
        {
            return 21 + (trackId != null ? trackId.Length : 0);
        }
    }
}