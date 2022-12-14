using AMP.Network.Packets.Attributes;

namespace AMP.Network.Packets.Implementation {
    [PacketDefinition((byte) PacketType.PLAYER_HEALTH_CHANGE)]
    public class PlayerHealthChangePacket : NetPacket {
        [SyncedVar] public long  playerId;
        [SyncedVar] public float change;

        public PlayerHealthChangePacket() { }

        public PlayerHealthChangePacket(long playerId, float change) {
            this.playerId = playerId;
            this.change   = change;
        }
    }
}
