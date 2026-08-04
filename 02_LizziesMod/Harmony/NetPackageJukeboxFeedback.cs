namespace LizziesMod
{
    public class NetPackageJukeboxFeedback : NetPackage
    {
        private string message;
        private bool denied;

        public override NetPackageDirection PackageDirection
        {
            get { return NetPackageDirection.ToClient; }
        }

        public NetPackageJukeboxFeedback Setup(string feedbackMessage, bool isDenied)
        {
            message = feedbackMessage ?? "";
            denied = isDenied;
            return this;
        }

        public override void read(PooledBinaryReader reader)
        {
            message = reader.ReadString();
            denied = reader.ReadBoolean();
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            writer.Write(message ?? "");
            writer.Write(denied);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;

            JukeboxUIController.ShowFeedback(message, denied);
        }

        public override int GetLength()
        {
            return 1 + (message == null ? 0 : message.Length);
        }
    }
}