namespace LizziesMod
{
    public class NetPackagePhysicsPropCommand : NetPackage
    {
        private PropSpawnerCommand command;
        private string propId;

        public override NetPackageDirection PackageDirection
        {
            get { return NetPackageDirection.ToServer; }
        }

        public NetPackagePhysicsPropCommand Setup(PropSpawnerCommand commandType, string requestedPropId)
        {
            command = commandType;
            propId = requestedPropId ?? "";
            return this;
        }

        public override void read(PooledBinaryReader reader)
        {
            command = (PropSpawnerCommand)reader.ReadByte();
            propId = reader.ReadString();
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            writer.Write((byte)command);
            writer.Write(propId ?? "");
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer || Sender == null) return;

            EntityPlayer player = world.GetEntity(Sender.entityId) as EntityPlayer;
            if (player == null) return;

            PropSpawnerManager.ProcessServerCommand(world, player, command, propId);
        }

        public override int GetLength()
        {
            return 1 + (propId != null ? propId.Length : 0);
        }
    }
}