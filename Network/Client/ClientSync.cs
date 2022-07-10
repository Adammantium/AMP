﻿using AMP.Data;
using AMP.Logging;
using AMP.Network.Data;
using AMP.Network.Data.Sync;
using AMP.Network.Helper;
using AMP.SupportFunctions;
using Chabuk.ManikinMono;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ThunderRoad;
using UnityEngine;

namespace AMP.Network.Client {
    public class ClientSync : MonoBehaviour {
        public SyncData syncData = new SyncData();

        void Start () {
            if(!ModManager.clientInstance.isConnected) {
                Destroy(this);
                return;
            }
            StartCoroutine(onUpdateTick());
        }

        public int packetsSentPerSec = 0;
        public int packetsReceivedPerSec = 0;

        float time = 0f;
        void FixedUpdate() {
            if(!ModManager.clientInstance.isConnected) {
                Destroy(this);
                return;
            }
            if(ModManager.clientInstance.myClientId <= 0) return;

            time += Time.fixedDeltaTime;
            if(time > 1f) {
                time = 0f;

                // Packet Stats
                #if DEBUG_NETWORK
                packetsSentPerSec = (ModManager.clientInstance.tcp != null ? ModManager.clientInstance.tcp.GetPacketsSent() : 0)
                                  + (ModManager.clientInstance.udp != null ? ModManager.clientInstance.udp.GetPacketsSent() : 0);
                packetsReceivedPerSec = (ModManager.clientInstance.tcp != null ? ModManager.clientInstance.tcp.GetPacketsReceived() : 0)
                                      + (ModManager.clientInstance.udp != null ? ModManager.clientInstance.udp.GetPacketsReceived() : 0);
                #endif
            }
        }

        // Check player and item position about 60/sec
        IEnumerator onUpdateTick() {
            float time = Time.time;
            while(true) {
                float wait = 1f / Config.TICK_RATE;
                if(wait > Time.time - time) wait -= Time.time - time;
                yield return new WaitForSeconds(wait);
                time = Time.time;

                if(ModManager.clientInstance.myClientId <= 0) continue;

                if(syncData.myPlayerData == null) syncData.myPlayerData = new PlayerSync();
                if(Player.local != null && Player.currentCreature != null) {
                    if(syncData.myPlayerData.creature == null) {
                        syncData.myPlayerData.creature = Player.currentCreature;

                        syncData.myPlayerData.clientId = ModManager.clientInstance.myClientId;
                        syncData.myPlayerData.name = UserData.GetUserName();


                        syncData.myPlayerData.height = Player.currentCreature.GetHeight();
                        syncData.myPlayerData.creatureId = Player.currentCreature.creatureId;

                        syncData.myPlayerData.playerPos = Player.local.transform.position;
                        syncData.myPlayerData.playerRot = Player.local.transform.eulerAngles.y;

                        ModManager.clientInstance.tcp.SendPacket(syncData.myPlayerData.CreateConfigPacket());

                        ReadEquipment();
                        ModManager.clientInstance.tcp.SendPacket(syncData.myPlayerData.CreateEquipmentPacket());

                        SendMyPos(true);

                        yield return CheckUnsynchedItems(); // Send the item when the player first connected
                    } else {
                        SendMyPos();
                    }
                }
                SendMovedItems();
                SendMovedCreatures();
            }
        }

        internal void Stop() {
            StopAllCoroutines();
            foreach(PlayerSync ps in syncData.players.Values) {
                LeavePlayer(ps);
            }
        }

        /// <summary>
        /// Checking if the player has any unsynched items that the server needs to know about
        /// </summary>
        private IEnumerator CheckUnsynchedItems() {
            // Get all items that are not synched
            List<Item> unsynced_items = Item.allActive.Where(item => syncData.items.All(entry => !item.Equals(entry.Value.clientsideItem))).ToList();

            foreach(Item item in unsynced_items) {
                if(!Config.ignoredTypes.Contains(item.data.type)) {
                    SyncItemIfNotAlready(item);

                    yield return new WaitForEndOfFrame();
                } else {
                    // Despawn all props until better syncing system, so we dont spam the other clients
                    item.Despawn();
                }
            }
        }

        // TODO: Fix: Player rotation does not match with headset rotation / Current Bugfix, use head Rotation, but need to find proper way for that
        // TODO: Fix: Player position is a bit buggy and doesnt always match
        private float lastPosSent = Time.time;
        public void SendMyPos(bool force = false) {
            if(Time.time - lastPosSent > 0.25f) force = true;
            if(!force) {
                if(!SyncFunc.hasPlayerMoved()) return;
            }

            syncData.myPlayerData.handLeftPos = Player.local.handLeft.transform.position;
            syncData.myPlayerData.handLeftRot = Player.currentCreature.handLeft.transform.eulerAngles;// += new Vector3(0, 0, 90);

            syncData.myPlayerData.handRightPos = Player.local.handRight.transform.position;
            syncData.myPlayerData.handRightRot = Player.currentCreature.handRight.transform.eulerAngles;// += new Vector3(-90, 0, 0);

            syncData.myPlayerData.headPos = Player.currentCreature.ragdoll.headPart.transform.position;
            syncData.myPlayerData.headRot = Player.currentCreature.ragdoll.headPart.transform.eulerAngles;

            syncData.myPlayerData.playerPos = Player.currentCreature.transform.position;
            syncData.myPlayerData.playerRot = Player.local.head.transform.eulerAngles.y;
            syncData.myPlayerData.playerVel = Player.local.locomotion.rb.velocity;

            syncData.myPlayerData.health = Player.currentCreature.currentHealth / Player.currentCreature.maxHealth;

            ModManager.clientInstance.udp.SendPacket(syncData.myPlayerData.CreatePosPacket());
            
            lastPosSent = Time.time;
        }

        public void SendMovedItems() {
            foreach(KeyValuePair<int, ItemSync> entry in syncData.items) {
                if(entry.Value.clientsideId <= 0 || entry.Value.networkedId <= 0) continue;

                if(SyncFunc.hasItemMoved(entry.Value)) {
                    entry.Value.UpdatePositionFromItem();
                    ModManager.clientInstance.udp.SendPacket(entry.Value.CreatePosPacket());
                }
            }
        }

        public void SendMovedCreatures() {
            foreach(KeyValuePair<int, CreatureSync> entry in syncData.creatures) {
                if(entry.Value.clientsideId <= 0 || entry.Value.networkedId <= 0) continue;

                if(SyncFunc.hasCreatureMoved(entry.Value)) {
                    entry.Value.UpdatePositionFromCreature();
                    ModManager.clientInstance.udp.SendPacket(entry.Value.CreatePosPacket());
                }
            }
        }

        public void LeavePlayer(PlayerSync ps) {
            if(ps == null) return;

            if(ps.creature != null) {
                Destroy(ps.creature.gameObject);
            }
        }

        private Dictionary<int, float> times = new Dictionary<int, float>();
        public void MovePlayer(int clientId, PlayerSync newPlayerSync) {
            PlayerSync playerSync = ModManager.clientSync.syncData.players[clientId];

            if(playerSync != null && playerSync.creature != null) {
                playerSync.ApplyPos(newPlayerSync);

                playerSync.creature.transform.eulerAngles = new Vector3(0, playerSync.playerRot, 0);
                playerSync.creature.transform.position = playerSync.playerPos + (playerSync.creature.transform.forward * 0.2f); // TODO: Better solution, it seems like the positions are a bit off
                playerSync.creature.locomotion.rb.velocity = playerSync.playerVel;
                playerSync.creature.locomotion.velocity = playerSync.playerVel;

                playerSync.leftHandTarget.position = playerSync.handLeftPos;
                playerSync.leftHandTarget.eulerAngles = playerSync.handLeftRot;

                playerSync.rightHandTarget.position = playerSync.handRightPos;
                playerSync.rightHandTarget.eulerAngles = playerSync.handRightRot;

                playerSync.headTarget.position = playerSync.headPos;
                playerSync.headTarget.eulerAngles = playerSync.headRot;
                playerSync.headTarget.Translate(Vector3.forward);
            }
        }

        public void SpawnPlayer(int clientId) {
            PlayerSync playerSync = ModManager.clientSync.syncData.players[clientId];

            if(playerSync.creature != null || playerSync.isSpawning) return;

            CreatureData creatureData = Catalog.GetData<CreatureData>(playerSync.creatureId);

            /*CreatureData creatureData = new CreatureData() {
                prefabLocation = playerData.prefabLocation,
                health         = playerData.health,
                ragdollData    = playerData.ragdollData
            };*/

            if(creatureData != null) {
                playerSync.isSpawning = true;
                Vector3 position = playerSync.playerPos;
                float rotationY = playerSync.playerRot;

                //creatureData.brainId = "HumanStatic";
                creatureData.containerID = "Empty";
                //creatureData.factionId = -1;

                creatureData.SpawnAsync(position, rotationY, null, false, null, creature => {
                    playerSync.creature = creature;

                    creature.factionId = -1;

                    IKControllerFIK ik = creature.GetComponentInChildren<IKControllerFIK>();

                    Transform handLeftTarget = new GameObject("HandLeftTarget" + playerSync.clientId).transform;
                    handLeftTarget.parent = creature.transform;
                    #if DEBUG_INFO
                    TextMesh textMesh = handLeftTarget.gameObject.AddComponent<TextMesh>();
                    textMesh.text = "L";
                    textMesh.alignment = TextAlignment.Center;
                    textMesh.anchor = TextAnchor.MiddleCenter;
                    textMesh.characterSize = 0.01f;
                    #endif
                    ik.SetHandAnchor(Side.Left, handLeftTarget);
                    playerSync.leftHandTarget = handLeftTarget;

                    Transform handRightTarget = new GameObject("HandRightTarget" + playerSync.clientId).transform;
                    handRightTarget.parent = creature.transform;
                    #if DEBUG_INFO
                    textMesh = handRightTarget.gameObject.AddComponent<TextMesh>();
                    textMesh.text = "R";
                    textMesh.alignment = TextAlignment.Center;
                    textMesh.anchor = TextAnchor.MiddleCenter;
                    textMesh.characterSize = 0.01f;
                    #endif
                    ik.SetHandAnchor(Side.Right, handRightTarget);
                    playerSync.rightHandTarget = handRightTarget;

                    Transform headTarget = new GameObject("HeadTarget" + playerSync.clientId).transform;
                    headTarget.parent = creature.transform;
                    #if DEBUG_INFO
                    textMesh = headTarget.gameObject.AddComponent<TextMesh>();
                    textMesh.text = "H";
                    textMesh.alignment = TextAlignment.Center;
                    textMesh.anchor = TextAnchor.MiddleCenter;
                    #endif
                    //ik.SetHeadAnchor(headTarget);
                    ik.SetLookAtTarget(headTarget);
                    playerSync.headTarget = headTarget;

                    ik.handLeftEnabled = true;
                    ik.handRightEnabled = true;
                    //ik.headEnabled = true;


                    Transform playerNameTag = new GameObject("PlayerNameTag" + playerSync.clientId).transform;
                    playerNameTag.parent = creature.transform;
                    playerNameTag.transform.localPosition = new Vector3(0, 2.5f, 0);
                    playerNameTag.transform.localEulerAngles = new Vector3(0, 180, 0);
                    #if !DEBUG_INFO
                    TextMesh 
                    #endif
                    textMesh = playerNameTag.gameObject.AddComponent<TextMesh>();
                    textMesh.text = playerSync.name;
                    textMesh.alignment = TextAlignment.Center;
                    textMesh.anchor = TextAnchor.MiddleCenter;
                    textMesh.fontSize = 500;
                    textMesh.characterSize = 0.0025f;

                    Transform playerHealthBar = new GameObject("PlayerHealthBar" + playerSync.clientId).transform;
                    playerHealthBar.parent = creature.transform;
                    playerHealthBar.transform.localPosition = new Vector3(0, 2.35f, 0);
                    playerHealthBar.transform.localEulerAngles = new Vector3(0, 180, 0);
                    textMesh = playerHealthBar.gameObject.AddComponent<TextMesh>();
                    textMesh.text = HealthBar.calculateHealthBar(1f);
                    textMesh.alignment = TextAlignment.Center;
                    textMesh.anchor = TextAnchor.MiddleCenter;
                    textMesh.fontSize = 500;
                    textMesh.characterSize = 0.001f;
                    playerSync.healthBar = textMesh;


                    creature.gameObject.name = "Player #" + playerSync.clientId;

                    creature.maxHealth = 100000;
                    creature.currentHealth = creature.maxHealth;

                    creature.isPlayer = false;
                    creature.enabled = false;
                    //creature.locomotion.enabled = false;
                    creature.climber.enabled = false;
                    creature.mana.enabled = false;
                    //creature.animator.enabled = false;
                    creature.ragdoll.enabled = false;
                    //creature.ragdoll.SetState(Ragdoll.State.Standing);
                    foreach(RagdollPart ragdollPart in creature.ragdoll.parts) {
                        foreach(HandleRagdoll hr in ragdollPart.handles){ Destroy(hr.gameObject); }// hr.enabled = false;
                        ragdollPart.sliceAllowed = false;
                        ragdollPart.enabled = false;
                    }
                    creature.brain.Stop();
                    //creature.StopAnimation();
                    creature.brain.StopAllCoroutines();
                    creature.locomotion.MoveStop();
                    //creature.animator.speed = 0f;
                    creature.SetHeight(playerSync.height);

                    creature.gameObject.AddComponent<CustomCreature>();

                    // Trying to despawn equipet items | TODO: Doesn't seem to work right now, maybe try delayed?
                    foreach(Holder holder in creature.holders) {
                        foreach(Item item in holder.items) item.Despawn();
                    }

                    GameObject.DontDestroyOnLoad(creature.gameObject);

                    Creature.all.Remove(creature);
                    Creature.allActive.Remove(creature);

                    //File.WriteAllText("C:\\Users\\mariu\\Desktop\\log.txt", GUIManager.LogLine(creature.gameObject, ""));

                    playerSync.isSpawning = false;

                    UpdateEquipment(playerSync);

                    Log.Debug("[Client] Spawned Character for Player " + playerSync.clientId);
                });

            }
        }

        public void SpawnCreature(CreatureSync creatureSync) {
            if(creatureSync.clientsideCreature != null) return;

            CreatureData creatureData = Catalog.GetData<CreatureData>(creatureSync.creatureId);
            if(creatureData != null) {
                Vector3 position = creatureSync.position;
                float rotationY = creatureSync.rotation.y;

                creatureData.SpawnAsync(position, rotationY, null, false, null, creature => {
                    creatureSync.clientsideCreature = creature;

                    creature.factionId = creatureSync.factionId;

                    UpdateCreature(creatureSync);
                });
            }
        }

        public void UpdateCreature(CreatureSync creatureSync) {
            if(creatureSync.clientsideCreature == null) return;
            if(creatureSync.clientsideId > 0) return; // Don't update a creature we have control over

            if(creatureSync.clientTarget >= 0 && !syncData.players.ContainsKey(creatureSync.clientTarget)) {
                // Stop the brain if no target found
                creatureSync.clientsideCreature.brain.Stop();
            } else {
                if(creatureSync.clientTarget == 0) return; // Creature is not attacking player

                // Restart the brain if its stopped
                if(creatureSync.clientsideCreature.brain.instance != null && !creatureSync.clientsideCreature.brain.instance.isActive) creatureSync.clientsideCreature.brain.instance.Start();

                creatureSync.clientsideCreature.brain.currentTarget = syncData.players[creatureSync.clientTarget].creature;
            }
        }

        public void SyncItemIfNotAlready(Item item) {
            if(ModManager.clientInstance == null) return;
            if(ModManager.clientSync == null) return;

            foreach(ItemSync sync in ModManager.clientSync.syncData.items.Values) {
                if(item.Equals(sync.clientsideItem)) {
                    return;
                }
            }

            ModManager.clientSync.syncData.currentClientItemId++;

            ItemSync itemSync = new ItemSync() {
                dataId = item.data.id,
                clientsideItem = item,
                clientsideId = ModManager.clientSync.syncData.currentClientItemId,
                position = item.transform.position,
                rotation = item.transform.eulerAngles
            };
            ModManager.clientInstance.tcp.SendPacket(itemSync.CreateSpawnPacket());

            ModManager.clientSync.syncData.items.Add(-ModManager.clientSync.syncData.currentClientItemId, itemSync);

            Log.Debug("[Client] Found new item " + item.data.id + " - Trying to spawn...");
        }

        public void ReadEquipment() {
            if(Player.currentCreature == null) return;

            syncData.myPlayerData.equipment.Clear();

            Equipment equipment = Player.currentCreature.equipment;

            syncData.myPlayerData.colors[0] = Player.currentCreature.GetColor(Creature.ColorModifier.Hair);
            syncData.myPlayerData.colors[1] = Player.currentCreature.GetColor(Creature.ColorModifier.HairSecondary);
            syncData.myPlayerData.colors[2] = Player.currentCreature.GetColor(Creature.ColorModifier.HairSpecular);
            syncData.myPlayerData.colors[3] = Player.currentCreature.GetColor(Creature.ColorModifier.EyesIris);
            syncData.myPlayerData.colors[4] = Player.currentCreature.GetColor(Creature.ColorModifier.EyesSclera);
            syncData.myPlayerData.colors[5] = Player.currentCreature.GetColor(Creature.ColorModifier.Skin);

            for(int i = 0; i < equipment.wearableSlots.Count; i++) {
                for(int j = equipment.wearableSlots[i].wardrobeLayers.Length - 1; j >= 0; j--) {
                    if(equipment.wearableSlots[i].IsEmpty()) {
                        continue;
                    }

                    ContainerData.Content equipmentOnLayer = equipment.wearableSlots[i].GetEquipmentOnLayer(equipment.wearableSlots[i].wardrobeLayers[j].layer);
                    if(equipmentOnLayer == null) {
                        continue;
                    }

                    ItemModuleWardrobe module = equipmentOnLayer.itemData.GetModule<ItemModuleWardrobe>();
                    if(module == null || equipment.wearableSlots[i].IsEmpty()) {
                        continue;
                    }

                    string str = equipment.wearableSlots[i].wardrobeChannel + ";" + equipment.wearableSlots[i].wardrobeLayers[j].layer + ";" + module.itemData.id;
                    if(!syncData.myPlayerData.equipment.Contains(str)) syncData.myPlayerData.equipment.Add(str);
                    break;
                }
            }
        }

        public void UpdateEquipment(PlayerSync playerSync) {
            if(playerSync == null) return;
            if(playerSync.creature == null) return;

            Player.currentCreature.SetColor(syncData.myPlayerData.colors[0], Creature.ColorModifier.Hair);
            Player.currentCreature.SetColor(syncData.myPlayerData.colors[1], Creature.ColorModifier.HairSecondary);
            Player.currentCreature.SetColor(syncData.myPlayerData.colors[2], Creature.ColorModifier.HairSpecular);
            Player.currentCreature.SetColor(syncData.myPlayerData.colors[3], Creature.ColorModifier.EyesIris);
            Player.currentCreature.SetColor(syncData.myPlayerData.colors[4], Creature.ColorModifier.EyesSclera);
            Player.currentCreature.SetColor(syncData.myPlayerData.colors[5], Creature.ColorModifier.Skin, true);

            List<string> to_fill = playerSync.equipment.ToArray().ToList();
            Equipment equipment = playerSync.creature.equipment;
            for(int i = 0; i < equipment.wearableSlots.Count; i++) {
                bool already_done = false;
                for(int j = 0; j < equipment.wearableSlots[i].wardrobeLayers.Length; j++) {
                    if(already_done) continue;

                    do {
                        if(equipment.wearableSlots[i].IsEmpty()) {
                            continue;
                        }

                        ContainerData.Content equipmentOnLayer = equipment.wearableSlots[i].GetEquipmentOnLayer(equipment.wearableSlots[i].wardrobeLayers[j].layer);
                        if(equipmentOnLayer == null) {
                            continue;
                        }

                        ItemModuleWardrobe module = equipmentOnLayer.itemData.GetModule<ItemModuleWardrobe>();
                        if(module == null || equipment.wearableSlots[i].IsEmpty()) {
                            continue;
                        }

                        if(playerSync.equipment.Contains(equipment.wearableSlots[i].wardrobeChannel + ";" + equipment.wearableSlots[i].wardrobeLayers[j].layer + ";" + module.itemData.id))
                            already_done = true; // Item is already equiped
                    }while(false);

                    if(already_done) continue;

                    // Unequip item
                    if(!equipment.wearableSlots[i].IsEmpty()) equipment.wearableSlots[i].UnEquip(equipment.wearableSlots[i].wardrobeLayers[j].layer, (item) => { item.Despawn(); });

                    // Check if a item is in the slot otherwise leave it empty
                    foreach(string line in playerSync.equipment) {
                        if(!to_fill.Contains(line)) continue;
                        if(line.StartsWith(equipment.wearableSlots[i].wardrobeChannel + ";" + equipment.wearableSlots[i].wardrobeLayers[j].layer + ";")) {
                            string itemId = line.Split(';')[2];

                            if(equipment.wearableSlots[i].IsEmpty()) {
                                ItemData itemData = Catalog.GetData<ItemData>(itemId);
                                if(itemData != null) {
                                    Wearable wearable = equipment.wearableSlots[i];
                                    itemData.SpawnAsync((item) => {
                                        wearable.EquipItem(item);
                                    });
                                }
                            }
                            to_fill.Remove(line);
                            break;
                        }
                    }
                }
            }
            Debug.Log(string.Join("\n", syncData.myPlayerData.equipment));
        }
    }
}
