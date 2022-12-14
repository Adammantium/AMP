using AMP.Network.Packets.Attributes;

namespace AMP.Network.Packets.Implementation {
    [PacketDefinition((byte) PacketType.WELCOME)]
    public class WelcomePacket : NetPacket {
        [SyncedVar] public long playerId;

        public WelcomePacket() { }

        public WelcomePacket(long playerId) {
            this.playerId = playerId;
        }
    }
}
