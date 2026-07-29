namespace LizziesMod
{
    public class NetPackagePhysicsPropCommand : NetPackage
    {
        private PropSpawnerCommand command;
        private string entryId;

        public override NetPackageDirection PackageDirection
        {
            get { return NetPackageDirection.ToServer; }
        }

        public NetPackagePhysicsPropCommand Setup(PropSpawnerCommand commandType, string requestedEntryId)
        {
            command = commandType;
            entryId = requestedEntryId ?? "";
            return this;
        }

        public override void read(PooledBinaryReader reader)
        {
            command = (PropSpawnerCommand)reader.ReadByte();
            entryId = reader.ReadString();
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            writer.Write((byte)command);
            writer.Write(entryId ?? "");
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer || Sender == null) return;

            EntityPlayer player = world.GetEntity(Sender.entityId) as EntityPlayer;
            if (player == null) return;

            SpawnMenuManager.ProcessServerCommand(world, player, command, entryId);
        }

        public override int GetLength()
        {
            return 1 + (entryId != null ? entryId.Length : 0);
        }
    }
}