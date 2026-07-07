using UnityEngine;

namespace LizziesMod
{
    public class NetPackageJukeboxPlay : NetPackage
    {
        private Vector3 blockPosition;
        private string trackName;


        public NetPackageJukeboxPlay() { }


        public NetPackageJukeboxPlay Setup(Vector3 _position, string _trackName)
        {
            this.blockPosition = _position;
            this.trackName = _trackName;
            return this;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
 
            _writer.Write(blockPosition.x);
            _writer.Write(blockPosition.y);
            _writer.Write(blockPosition.z);
            _writer.Write(trackName);
        }


        public override void read(PooledBinaryReader _reader)
        {
   
            blockPosition = new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
            trackName = _reader.ReadString();
        }

        
        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (_world == null) return;

   
            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
             
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
                    NetPackageManager.GetPackage<NetPackageJukeboxPlay>().Setup(blockPosition, trackName),
                    false, 
                    -1, -1, -1, null);

            
                if (!GameManager.IsDedicatedServer && CustomAudioManager.Instance != null)
                {
                    CustomAudioManager.Instance.PlayJukeboxTrack(blockPosition, trackName);
                }
            }
            else
            {
       
                if (CustomAudioManager.Instance != null)
                {
                    CustomAudioManager.Instance.PlayJukeboxTrack(blockPosition, trackName);
                }
            }
        }

        public override int GetLength()
        {
            return 12 + (trackName != null ? trackName.Length : 0);
        }
    }
}