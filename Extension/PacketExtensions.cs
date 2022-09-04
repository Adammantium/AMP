﻿using AMP.Network.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace AMP.Extension {
    public static class PacketExtensions {

        public static void SendToServerReliable(this Packet packet) {
            ModManager.clientInstance.nw.SendReliable(packet);
        }

        public static void SendToServerUnreliable(this Packet packet) {
            ModManager.clientInstance.nw.SendUnreliable(packet);
        }

    }
}