﻿using AMP.Data;
using AMP.Logging;
using AMP.Network.Data;
using AMP.Network.Data.Sync;
using AMP.Network.Connection;
using AMP.Network.Packets;
using AMP.Network.Packets.Implementation;
using AMP.SupportFunctions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ThunderRoad;
using AMP.Network.Helper;
using System.Threading;

namespace AMP.Network.Server {
    public class Server {
        internal bool isRunning = false;

        public uint maxClients = 4;
        private int port = 13698;

        private static TcpListener tcpListener;
        private static UdpClient udpListener;

        internal string currentLevel = null;
        internal string currentMode = null;
        internal Dictionary<string, string> currentOptions = new Dictionary<string, string>();

        private long currentPlayerId = 1;
        internal Dictionary<long, ClientData> clients = new Dictionary<long, ClientData>();
        private Dictionary<string, long> endPointMapping = new Dictionary<string, long>();

        private long currentItemId = 1;
        internal Dictionary<long, ItemNetworkData> items = new Dictionary<long, ItemNetworkData>();
        internal Dictionary<long, long> item_owner = new Dictionary<long, long>();
        internal long currentCreatureId = 1;
        internal Dictionary<long, long> creature_owner = new Dictionary<long, long>();
        internal Dictionary<long, CreatureNetworkData> creatures = new Dictionary<long, CreatureNetworkData>();

        public static string DEFAULT_MAP = "Home";
        public static string DEFAULT_MODE = "Default";

        public int connectedClients {
            get { return clients.Count; }
        }
        public int spawnedItems {
            get { return items.Count; }
        }
        public int spawnedCreatures {
            get { return creatures.Count; }
        }
        public Dictionary<long, string> connectedClientList {
            get {
                Dictionary<long, string> test = new Dictionary<long, string>();
                foreach (var item in clients) {
                    test.Add(item.Key, item.Value.name);
                }
                return test;
            }
        }

        public Server(uint maxClients, int port) {
            this.maxClients = maxClients;
            this.port = port;
        }

        private Thread timeoutThread;

        internal void Stop() {
            Log.Info("[Server] Stopping server...");

            foreach(ClientData clientData in clients.Values) {
                SendReliableTo(clientData.playerId, new DisconnectPacket(clientData.playerId, "Server closed"));
            }

            if(ModManager.discordNetworking) {
                DiscordNetworking.DiscordNetworking.instance.Disconnect();
            } else {
                tcpListener.Stop();
                udpListener.Dispose();

                tcpListener = null;
                udpListener = null;
            }

            if(timeoutThread != null) {
                try {
                    timeoutThread.Abort();
                } catch { }
            }

            Log.Info("[Server] Server stopped.");
        }

        internal void Start() {
            int ms = DateTime.UtcNow.Millisecond;
            Log.Info("[Server] Starting server...");

            if(port > 0) {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
                tcpListener.BeginAcceptTcpClient(TCPRequestCallback, null);

                udpListener = new UdpClient(port);
                udpListener.BeginReceive(UDPRequestCallback, null);
            }

            Dictionary<string, string> options = new Dictionary<string, string>();
            bool levelInfoSuccess = LevelInfo.ReadLevelInfo(ref currentLevel, ref currentMode, ref options);

            if(!levelInfoSuccess || currentLevel.Equals("CharacterSelection")) {
                currentLevel = DEFAULT_MAP;
                currentMode = DEFAULT_MODE;
            }

            isRunning = true;

            timeoutThread = new Thread(() => {
                while(isRunning) {
                    long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    foreach(ClientData cd in clients.Values.ToArray()) {
                        if(cd.last_time < now - 30000) { // 30 Sekunden
                            try {
                                LeavePlayer(cd, "Played timed out");
                            } catch { }
                        }
                    }
                    Thread.Sleep(1000);
                }
            });
            timeoutThread.Start();
            
            Log.Info($"[Server] Server started after {DateTime.UtcNow.Millisecond - ms}ms.\n" +
                     $"\t Level: {currentLevel} / Mode: {currentMode}\n" +
                     $"\t Options:\n\t{string.Join("\n\t", options.Select(p => p.Key + " = " + p.Value))}\n" +
                     $"\t Max-Players: {maxClients} / Port: {port}"
                     );
        }

        internal int packetsSent = 0;
        internal int packetsReceived = 0;
        private int udpPacketSent = 0;
        internal void UpdatePacketCount() {
            packetsSent = udpPacketSent;
            packetsReceived = 0;
            foreach(ClientData cd in clients.Values) {
                packetsSent += (cd.tcp != null ? cd.tcp.GetPacketsSent() : 0)
                                + (cd.udp != null ? cd.udp.GetPacketsSent() : 0);
                packetsReceived += (cd.tcp != null ? cd.tcp.GetPacketsReceived() : 0)
                                    + (cd.udp != null ? cd.udp.GetPacketsReceived() : 0);
            }
            udpPacketSent = 0;
        }

        internal void GreetPlayer(ClientData cd, bool loadedLevel = false) {
            if(!clients.ContainsKey(cd.playerId)) {
                clients.Add(cd.playerId, cd);

                SendReliableTo(cd.playerId, new WelcomePacket(cd.playerId));
            }

            if(currentLevel.Length > 0 && !loadedLevel) {
                Log.Debug($"[Server] Waiting for player {cd.name} to load into the level.");
                SendReliableTo(cd.playerId, new LevelChangePacket(currentLevel, currentMode, currentOptions));
                return;
            }

            // Send all player data to the new client
            foreach(ClientData other_client in clients.Values) {
                if(other_client.playerSync == null) continue;
                SendReliableTo(cd.playerId, new PlayerDataPacket(other_client.playerSync));
                SendReliableTo(cd.playerId, new PlayerEquipmentPacket(other_client.playerSync));
            }

            SendItemsAndCreatures(cd);

            Log.Debug("[Server] Welcoming player " + cd.name);

            cd.greeted = true;
        }

        private void SendItemsAndCreatures(ClientData cd) {
            // Send all spawned creatures to the client
            foreach(KeyValuePair<long, CreatureNetworkData> entry in creatures) {
                SendReliableTo(cd.playerId, new CreatureSpawnPacket(entry.Value));
            }

            // Send all spawned items to the client
            foreach(KeyValuePair<long, ItemNetworkData> entry in items) {
                SendReliableTo(cd.playerId, new ItemSpawnPacket(entry.Value));
                if(entry.Value.creatureNetworkId > 0) {
                    SendReliableTo(cd.playerId, new ItemSnapPacket(entry.Value));
                }
            }

            SendReliableTo(cd.playerId, new WelcomePacket(-1));
        }

        #region TCP/IP Callbacks
        private void TCPRequestCallback(IAsyncResult _result) {
            if(tcpListener == null) return;
            TcpClient tcpClient = tcpListener.EndAcceptTcpClient(_result);
            tcpListener.BeginAcceptTcpClient(TCPRequestCallback, null);
            //Log.Debug($"[Server] Incoming connection from {tcpClient.Client.RemoteEndPoint}...");

            TcpSocket socket = new TcpSocket(tcpClient);

            socket.onPacket += (packet) => {
                WaitForConnection(socket, packet);
            };
        }

        private void WaitForConnection(TcpSocket socket, NetPacket p) {
            if(p is ServerPingPacket) {
                ServerPingPacket serverPingPacket = new ServerPingPacket();

                socket.SendPacket(serverPingPacket);
                socket.Disconnect();
            } else if(p is EstablishConnectionPacket) {
                EstablishConnectionPacket establishConnectionPacket = new EstablishConnectionPacket();

                if(connectedClients >= maxClients) {
                    Log.Warn($"[Server] Client {establishConnectionPacket.name} tried to join full server.");
                    socket.SendPacket(new ErrorPacket("server is full"));
                    socket.Disconnect();
                    return;
                }

                ClientData cd = new ClientData(currentPlayerId++);
                cd.tcp = socket;
                cd.tcp.onPacket += (packet) => {
                    OnPacket(cd, packet);
                };
                cd.name = establishConnectionPacket.name;

                GreetPlayer(cd);
            }
        }

        private void UDPRequestCallback(IAsyncResult _result) {
            if(udpListener == null) return;
            try {
                IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpListener.EndReceive(_result, ref clientEndPoint);
                udpListener.BeginReceive(UDPRequestCallback, null);

                if(data.Length < 8) return;

                // Check if its a welcome package and if the user is not linked up
                using(NetPacket packet = NetPacket.ReadPacket(data)) {
                    //packet.ReadInt(); // Flush length away //TODO?
                    if(packet is WelcomePacket) {
                        long clientId = ((WelcomePacket) packet).playerId;

                        // If no udp is connected, then link up
                        if(clients[clientId].udp == null) {
                            clients[clientId].udp = new UdpSocket(clientEndPoint);
                            endPointMapping.Add(clientEndPoint.ToString(), clientId);
                            clients[clientId].udp.onPacket += (p) => {
                                if(clients.ContainsKey(clientId)) OnPacket(clients[clientId], p);
                            };

                            //Log.Debug("[Server] Linked UDP for " + clientId);
                            return;
                        }
                    }
                }

                // Determine client id by EndPoint
                if(endPointMapping.ContainsKey(clientEndPoint.ToString())) {
                    long clientId = endPointMapping[clientEndPoint.ToString()];
                    if(!clients.ContainsKey(clientId)) {
                        Log.Err("[Server] This should not happen... #SNHE001"); // SNHE = Should not happen error
                    } else {
                        if(clients[clientId].udp.endPoint.ToString() == clientEndPoint.ToString()) {
                            clients[clientId].udp.HandleData(NetPacket.ReadPacket(data));
                        }
                    }
                } else {
                    Log.Err("[Server] Invalid UDP client tried to connect " + clientEndPoint.ToString());
                }
            } catch(Exception e) {
                Log.Err($"[Server] Error receiving UDP data: {e}");
            }
        }
        #endregion

        internal void OnPacket(ClientData client, NetPacket p) {
            client.last_time = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            PacketType type = (PacketType) p.getPacketType();

            switch(type) {
                #region Connection handling and stuff
                case PacketType.WELCOME:
                    WelcomePacket welcomePacket = (WelcomePacket) p;

                    // Other user is sending multiple messages, one should reach the server
                    // Debug.Log($"[Server] UDP {client.name}...");
                    break;

                case PacketType.MESSAGE:
                    MessagePacket messagePacket = (MessagePacket) p;

                    Log.Debug($"[Server] Message from {client.name}: { messagePacket.message }");
                    break;

                case PacketType.DISCONNECT:
                    DisconnectPacket disconnectPacket = (DisconnectPacket) p;

                    LeavePlayer(clients[client.playerId]);
                    break;
                #endregion

                #region Player Packets
                case PacketType.PLAYER_DATA:
                    PlayerDataPacket playerDataPacket = (PlayerDataPacket) p;

                    if(client.playerSync == null) {
                        Log.Info($"[Server] Player {playerDataPacket.name} ({client.playerId}) joined the server.");

                        client.playerSync = new PlayerNetworkData() { clientId = client.playerId };
                    }
                    client.playerSync.Apply(playerDataPacket);

                    client.playerSync.name = Regex.Replace(client.playerSync.name, @"[^\u0000-\u007F]+", string.Empty);

                    client.playerSync.clientId = client.playerId;
                    client.name = client.playerSync.name;

                    #if DEBUG_SELF
                    // Just for debug to see yourself
                    SendReliableToAll(new PlayerDataPacket(client.playerSync));//, client.playerId);
                    #else
                    SendReliableToAllExcept(new PlayerDataPacket(client.playerSync), client.playerId);
                    #endif
                    break;

                case PacketType.PLAYER_POSITION:
                    PlayerPositionPacket playerPositionPacket = (PlayerPositionPacket) p;

                    if(client.playerSync == null) break;

                    client.playerSync.Apply(playerPositionPacket);
                    client.playerSync.clientId = client.playerId;

                    #if DEBUG_SELF
                    // Just for debug to see yourself
                    SendUnreliableToAll(new PlayerPositionPacket(client.playerSync));//, client.playerId);
                    #else
                    SendUnreliableToAllExcept(new PlayerPositionPacket(client.playerSync), client.playerId);
                    #endif
                    break;

                case PacketType.PLAYER_EQUIPMENT:
                    PlayerEquipmentPacket playerEquipmentPacket = (PlayerEquipmentPacket) p;

                    client.playerSync.Apply(playerEquipmentPacket);

                    #if DEBUG_SELF
                    // Just for debug to see yourself
                    SendReliableToAll(new PlayerEquipmentPacket(client.playerSync));
                    #else
                    SendReliableToAllExcept(new PlayerEquipmentPacket(client.playerSync), client.playerId);
                    #endif
                    break;

                case PacketType.PLAYER_RAGDOLL:
                    PlayerRagdollPacket playerRagdollPacket = (PlayerRagdollPacket) p;

                    if(client.playerSync == null) break;
                    if(client.playerId != playerRagdollPacket.playerId) return;

                    client.playerSync.Apply(playerRagdollPacket);

                    #if DEBUG_SELF
                    // Just for debug to see yourself
                    SendReliableToAll(playerRagdollPacket);
                    #else
                    SendReliableToAllExcept(playerRagdollPacket, client.playerId);
                    #endif
                    break;

                case PacketType.PLAYER_HEALTH_SET:
                    PlayerHealthSetPacket playerHealthSetPacket = (PlayerHealthSetPacket) p;

                    client.playerSync.Apply(playerHealthSetPacket);

                    #if DEBUG_SELF
                    // Just for debug to see yourself
                    SendReliableToAll(new PlayerHealthSetPacket(client.playerSync));
                    #else
                    SendReliableToAllExcept(new PlayerHealthSetPacket(client.playerSync), client.playerId);
                    #endif
                    break;

                case PacketType.PLAYER_HEALTH_CHANGE:
                    PlayerHealthChangePacket playerHealthChangePacket = (PlayerHealthChangePacket) p;
                    
                    if(!ServerConfig.pvpEnable) break;
                    if(ServerConfig.pvpDamageMultiplier <= 0) break;

                    if(clients.ContainsKey(playerHealthChangePacket.playerId)) {
                        playerHealthChangePacket.change *= ServerConfig.pvpDamageMultiplier;

                        SendReliableTo(playerHealthChangePacket.playerId, playerHealthChangePacket);
                    }
                    break;
                #endregion

                #region Item Packets
                case PacketType.ITEM_SPAWN:
                    ItemSpawnPacket itemSpawnPacket = (ItemSpawnPacket) p;

                    ItemNetworkData ind = new ItemNetworkData();
                    ind.Apply(itemSpawnPacket);

                    ind.networkedId = SyncFunc.DoesItemAlreadyExist(ind, items.Values.ToList());
                    bool was_duplicate = false;
                    if(ind.networkedId <= 0) {
                        ind.networkedId = currentItemId++;
                        items.Add(ind.networkedId, ind);
                        UpdateItemOwner(ind, client.playerId);
                        Log.Debug($"[Server] {client.name} has spawned item {ind.dataId} ({ind.networkedId})" );
                    } else {
                        ind.clientsideId = -ind.clientsideId;
                        Log.Debug($"[Server] {client.name} has duplicate of {ind.dataId} ({ind.networkedId})");
                        was_duplicate = true;
                    }

                    SendReliableTo(client.playerId, new ItemSpawnPacket(ind));

                    if(was_duplicate) return; // If it was a duplicate, dont send it to other players

                    ind.clientsideId = 0;
                    
                    SendReliableToAllExcept(new ItemSpawnPacket(ind), client.playerId);
                    break;

                case PacketType.ITEM_DESPAWN:
                    ItemDespawnPacket itemDespawnPacket = (ItemDespawnPacket) p;

                    if(items.ContainsKey(itemDespawnPacket.itemId)) {
                        ind = items[itemDespawnPacket.itemId];

                        Log.Debug($"[Server] {client.name} has despawned item {ind.dataId} ({ind.networkedId})");

                        SendReliableToAllExcept(itemDespawnPacket, client.playerId);

                        items.Remove(itemDespawnPacket.itemId);
                        if(item_owner.ContainsKey(itemDespawnPacket.itemId)) item_owner.Remove(itemDespawnPacket.itemId);
                    }

                    break;

                case PacketType.ITEM_POSITION:
                    ItemPositionPacket itemPositionPacket = (ItemPositionPacket) p;

                    if(items.ContainsKey(itemPositionPacket.itemId)) {
                        ind = items[itemPositionPacket.itemId];

                        ind.Apply(itemPositionPacket);

                        SendUnreliableToAllExcept(itemPositionPacket, client.playerId);
                    }
                    break;

                case PacketType.ITEM_OWNER:
                    ItemOwnerPacket itemOwnerPacket = (ItemOwnerPacket) p;

                    if(itemOwnerPacket.itemId > 0 && items.ContainsKey(itemOwnerPacket.itemId)) {
                        UpdateItemOwner(items[itemOwnerPacket.itemId], client.playerId);

                        SendReliableTo(client.playerId, new ItemOwnerPacket(itemOwnerPacket.itemId, true));
                        SendReliableToAllExcept(new ItemOwnerPacket(itemOwnerPacket.itemId, false), client.playerId);
                    }
                    break;

                case PacketType.ITEM_SNAPPING_SNAP:
                    ItemSnapPacket itemSnapPacket = (ItemSnapPacket) p;

                    if(itemSnapPacket.itemId > 0 && items.ContainsKey(itemSnapPacket.itemId)) {
                        ind = items[itemSnapPacket.itemId];

                        ind.Apply(itemSnapPacket);

                        Log.Debug($"[Server] Snapped item {ind.dataId} to {ind.creatureNetworkId} to { (ind.drawSlot == Holder.DrawSlot.None ? "hand " + ind.holdingSide : "slot " + ind.drawSlot) }.");
                        SendReliableToAllExcept(itemSnapPacket, client.playerId);
                    }
                    break;

                case PacketType.ITEM_SNAPPING_UNSNAP:
                    ItemUnsnapPacket itemUnsnapPacket = (ItemUnsnapPacket) p;

                    if(itemUnsnapPacket.itemId > 0 && items.ContainsKey(itemUnsnapPacket.itemId)) {
                        ind = items[itemUnsnapPacket.itemId];
                        Log.Debug($"[Server] Unsnapped item {ind.dataId} from {ind.creatureNetworkId}.");

                        ind.Apply(itemUnsnapPacket);

                        SendReliableToAllExcept(itemUnsnapPacket, client.playerId);
                    }
                    break;
                #endregion

                #region Imbues
                case PacketType.ITEM_IMBUE:
                    ItemImbuePacket itemImbuePacket = (ItemImbuePacket) p;

                    SendReliableToAllExcept(p, client.playerId); // Just forward them atm
                    break;
                #endregion

                #region Level Changing
                case PacketType.LEVEL_CHANGE:
                    LevelChangePacket levelChangePacket = (LevelChangePacket) p;

                    if(!client.greeted) {
                        GreetPlayer(client, true);
                        return;
                    }

                    if(levelChangePacket.level == null) return;
                    if(levelChangePacket.mode  == null) return;

                    if(levelChangePacket.level.Equals("characterselection", StringComparison.OrdinalIgnoreCase)) return;

                    if(!(levelChangePacket.level.Equals(currentLevel, StringComparison.OrdinalIgnoreCase) && levelChangePacket.mode.Equals(currentMode, StringComparison.OrdinalIgnoreCase))) { // Player is the first to join that level
                        if(!ServerConfig.allowMapChange) {
                            Log.Err("[Server] Player " + client.name + " tried changing level.");
                            SendReliableTo(client.playerId, new DisconnectPacket(client.playerId, "Map changing is not allowed by the server!"));
                            LeavePlayer(client);
                            return;
                        }
                        currentLevel = levelChangePacket.level;
                        currentMode = levelChangePacket.mode;

                        currentOptions = levelChangePacket.option_dict;

                        Log.Info($"[Server] Client { client.playerId } loaded level {currentLevel} with mode {currentMode}.");
                        SendReliableToAllExcept(levelChangePacket, client.playerId);
                    } else { // Player joined after another is already in it, so we send all items and stuff
                        SendItemsAndCreatures(client);
                    }
                    break;
                #endregion

                #region Creature Packets
                case PacketType.CREATURE_SPAWN:
                    CreatureSpawnPacket creatureSpawnPacket = (CreatureSpawnPacket) p;

                    CreatureNetworkData cnd = new CreatureNetworkData();
                    cnd.Apply(creatureSpawnPacket);

                    if(cnd.networkedId > 0) return;

                    cnd.networkedId = currentCreatureId++;

                    UpdateCreatureOwner(cnd, client);
                    creatures.Add(cnd.networkedId, cnd);
                    Log.Debug($"[Server] {client.name} has summoned {cnd.creatureType} ({cnd.networkedId})");

                    SendReliableTo(client.playerId, creatureSpawnPacket);

                    creatureSpawnPacket.clientsideId = 0;

                    SendReliableToAllExcept(creatureSpawnPacket, client.playerId);
                    break;


                case PacketType.CREATURE_POSITION:
                    CreaturePositionPacket creaturePositionPacket = (CreaturePositionPacket) p;

                    if(creatures.ContainsKey(creaturePositionPacket.creatureId)) {
                        cnd = creatures[creaturePositionPacket.creatureId];
                        cnd.Apply(creaturePositionPacket);

                        SendUnreliableToAllExcept(creaturePositionPacket, client.playerId);
                    }
                    break;

                case PacketType.CREATURE_HEALTH_SET:
                    CreatureHealthSetPacket creatureHealthSetPacket = (CreatureHealthSetPacket) p;

                    if(creatures.ContainsKey(creatureHealthSetPacket.creatureId)) {
                        cnd = creatures[creatureHealthSetPacket.creatureId];
                        cnd.Apply(creatureHealthSetPacket);

                        SendReliableToAllExcept(creatureHealthSetPacket, client.playerId);
                    }
                    break;

                case PacketType.CREATURE_HEALTH_CHANGE:
                    CreatureHealthChangePacket creatureHealthChangePacket = (CreatureHealthChangePacket) p;

                    if(creatures.ContainsKey(creatureHealthChangePacket.creatureId)) {
                        cnd = creatures[creatureHealthChangePacket.creatureId];
                        cnd.Apply(creatureHealthChangePacket);

                        SendReliableToAllExcept(creatureHealthChangePacket, client.playerId);

                        // If the damage the player did is more than 30% of the already dealt damage,
                        // then change the npc to that players authority
                        if(creatureHealthChangePacket.change / (cnd.maxHealth - cnd.health) > 0.3) {
                            UpdateCreatureOwner(cnd, client);
                        }
                    }
                    break;

                case PacketType.CREATURE_DESPAWN:
                    CreatureDepawnPacket creatureDepawnPacket = (CreatureDepawnPacket) p;

                    if(creatures.ContainsKey(creatureDepawnPacket.creatureId)) {
                        cnd = creatures[creatureDepawnPacket.creatureId];

                        Log.Debug($"[Server] {client.name} has despawned creature {cnd.creatureType} ({cnd.networkedId})");
                        SendReliableToAllExcept(creatureDepawnPacket, client.playerId);

                        creatures.Remove(creatureDepawnPacket.creatureId);
                    }
                    break;

                case PacketType.CREATURE_PLAY_ANIMATION:
                    CreatureAnimationPacket creatureAnimationPacket = (CreatureAnimationPacket) p;

                    if(creatures.ContainsKey(creatureAnimationPacket.creatureId)) {
                        SendReliableToAllExcept(creatureAnimationPacket, client.playerId);
                    }
                    break;

                case PacketType.CREATURE_RAGDOLL:
                    CreatureRagdollPacket creatureRagdollPacket = (CreatureRagdollPacket) p;

                    if(creatures.ContainsKey(creatureRagdollPacket.creatureId)) {
                        cnd = creatures[creatureRagdollPacket.creatureId];
                        cnd.Apply(creatureRagdollPacket);

                        SendUnreliableToAllExcept(p, client.playerId);
                    }
                    break;

                case PacketType.CREATURE_SLICE:
                    CreatureSlicePacket creatureSlicePacket = (CreatureSlicePacket) p;

                    if(creatures.ContainsKey(creatureSlicePacket.creatureId)) {
                        SendReliableToAllExcept(p, client.playerId);
                    }
                    break;

                case PacketType.CREATURE_OWNER:
                    CreatureOwnerPacket creatureOwnerPacket = (CreatureOwnerPacket) p;

                    if(creatureOwnerPacket.creatureId > 0 && creatures.ContainsKey(creatureOwnerPacket.creatureId)) {
                        UpdateCreatureOwner(creatures[creatureOwnerPacket.creatureId], client);
                    }
                    break;
                #endregion

                default: break;
            }
        }

        internal void UpdateItemOwner(ItemNetworkData itemNetworkData, long playerId) {
            if(item_owner.ContainsKey(itemNetworkData.networkedId)) {
                item_owner[itemNetworkData.networkedId] = playerId;
            } else {
                item_owner.Add(itemNetworkData.networkedId, playerId);
            }
        }

        internal void UpdateCreatureOwner(CreatureNetworkData creatureNetworkData, ClientData client) {
            if(creature_owner.ContainsKey(creatureNetworkData.networkedId)) {
                if(creature_owner[creatureNetworkData.networkedId] != client.playerId) {
                    creature_owner[creatureNetworkData.networkedId] = client.playerId;

                    SendReliableTo(client.playerId, new CreatureOwnerPacket(creatureNetworkData.networkedId, true));
                    SendReliableToAllExcept(new CreatureOwnerPacket(creatureNetworkData.networkedId, false), client.playerId);

                    Log.Debug($"[Server] {client.name} has taken ownership of creature {creatureNetworkData.creatureType} ({creatureNetworkData.networkedId})");
                }
            } else {
                creature_owner.Add(creatureNetworkData.networkedId, client.playerId);
            }
        }


        internal void LeavePlayer(ClientData client, string reason = "Player disconnected") {
            if(client == null) return;

            if(clients.Count <= 1) {
                creatures.Clear();
                creature_owner.Clear();
                items.Clear();
                item_owner.Clear();
                Log.Info($"[Server] Clearing server because last player disconnected.");
            } else {
                try {
                    ClientData migrateUser = clients.First(entry => entry.Value.playerId != client.playerId).Value;
                    try {
                        KeyValuePair<long, long>[] entries = item_owner.Where(entry => entry.Value == client.playerId).ToArray();

                        foreach(KeyValuePair<long, long> entry in entries) {
                            if(items.ContainsKey(entry.Key)) {
                                item_owner[entry.Key] = migrateUser.playerId;
                                SendReliableTo(migrateUser.playerId, new ItemOwnerPacket(entry.Key, true));
                            }
                        }
                        Log.Info($"[Server] Migrated items from { client.name } to { migrateUser.name }.");
                    } catch(Exception e) {
                        Log.Err($"[Server] Couldn't migrate items from {client.name} to { migrateUser.name }.\n{e}");
                    }

                    try {
                        KeyValuePair<long, long>[] entries = creature_owner.Where(entry => entry.Value == client.playerId).ToArray();

                        foreach(KeyValuePair<long, long> entry in entries) {
                            if(creatures.ContainsKey(entry.Key)) {
                                creature_owner[entry.Key] = migrateUser.playerId;
                                SendReliableTo(migrateUser.playerId, new CreatureOwnerPacket(entry.Key, true));
                            }
                        }
                        Log.Info($"[Server] Migrated creatures from {client.name} to {migrateUser.name}.");
                    } catch(Exception e) {
                        Log.Err($"[Server] Couldn't migrate creatures from {client.name} to {migrateUser.name}.\n{e}");
                    }
                } catch(Exception e) {
                    Log.Err($"[Server] Couldn't migrate stuff from { client.name } to other client.\n{e}");
                }
            }

            if(client.udp != null && endPointMapping.ContainsKey(client.udp.endPoint.ToString())) endPointMapping.Remove(client.udp.endPoint.ToString());
            clients.Remove(client.playerId);
            try {
                try {
                    SendReliableTo(client.playerId, new DisconnectPacket(client.playerId, reason));
                } catch { }
                client.Disconnect();
            } catch { }

            SendReliableToAll(new DisconnectPacket(client.playerId, reason));

            Log.Info($"[Server] {client.name} disconnected. {reason}");
        }

        // TCP
        internal void SendReliableToAll(NetPacket p) {
            SendReliableToAllExcept(p);
        }

        internal void SendReliableTo(long clientId, NetPacket p) {
            if(!clients.ContainsKey(clientId)) return;

            if(ModManager.discordNetworking) {
                DiscordNetworking.DiscordNetworking.instance.SendReliable(p, clientId, true);
            } else {
                clients[clientId].tcp.SendPacket(p);
            }
        }

        internal void SendReliableToAllExcept(NetPacket p, params long[] exceptions) {
            foreach(KeyValuePair<long, ClientData> client in clients.ToArray()) {
                if(exceptions.Contains(client.Key)) continue;

                if(ModManager.discordNetworking) {
                    DiscordNetworking.DiscordNetworking.instance.SendReliable(p, client.Key, true);
                } else {
                    client.Value.tcp.SendPacket(p);
                }
            }
        }

        // UDP
        internal void SendUnreliableToAll(NetPacket p) {
            SendUnreliableToAllExcept(p);
        }

        internal void SendUnreliableTo(long clientId, NetPacket p) {
            if(!clients.ContainsKey(clientId)) return;

            if(ModManager.discordNetworking) {
                DiscordNetworking.DiscordNetworking.instance.SendReliable(p, clientId, true);
            } else {
                try {
                    if(clients[clientId].udp.endPoint != null) {
                        byte[] data = p.GetData();
                        udpListener.Send(data, data.Length, clients[clientId].udp.endPoint);
                        udpPacketSent++;
                    }
                } catch(Exception e) {
                    Log.Err($"Error sending data to {clients[clientId].udp.endPoint} via UDP: {e}");
                }
            }
        }

        internal void SendUnreliableToAllExcept(NetPacket p, params long[] exceptions) {
            //p.WriteLength();
            foreach(KeyValuePair<long, ClientData> client in clients.ToArray()) {
                if(exceptions.Contains(client.Key)) continue;

                if(ModManager.discordNetworking) {
                    DiscordNetworking.DiscordNetworking.instance.SendUnreliable(p, client.Key, true);
                } else {
                    try {
                        if(client.Value.udp != null && client.Value.udp.endPoint != null) {
                            byte[] data = p.GetData();
                            udpListener.Send(data, data.Length, client.Value.udp.endPoint);
                            udpPacketSent++;
                        }
                    } catch(Exception e) {
                        Log.Err($"Error sending data to {client.Value.udp.endPoint} via UDP: {e}");
                    }
                }
            }
        }
    }
}
