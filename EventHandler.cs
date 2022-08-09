﻿using AMP.Data;
using AMP.Extension;
using AMP.Logging;
using AMP.Network.Data;
using AMP.Network.Data.Sync;
using System;
using System.Linq;
using ThunderRoad;
using UnityEngine;

namespace AMP {
    public class EventHandler : MonoBehaviour {

        #region Global Event Registering
        private static bool registered = false;
        public static void RegisterGlobalEvents() {
            if(registered) return;
            EventManager.onLevelLoad += EventManager_onLevelLoad;
            EventManager.onItemSpawn += EventManager_onItemSpawn;
            EventManager.onItemEquip += EventManager_onItemEquip;
            EventManager.onCreatureSpawn += EventManager_onCreatureSpawn;
            EventManager.onCreatureAttacking += EventManager_onCreatureAttacking;
            EventManager.OnSpellUsed += EventManager_OnSpellUsed;
            registered = true;
        }

        public static void UnRegisterGlobalEvents() {
            if(!registered) return;
            EventManager.onLevelLoad -= EventManager_onLevelLoad;
            EventManager.onItemSpawn -= EventManager_onItemSpawn;
            EventManager.onItemEquip -= EventManager_onItemEquip;
            EventManager.onCreatureSpawn -= EventManager_onCreatureSpawn;
            EventManager.onCreatureAttacking -= EventManager_onCreatureAttacking;
            EventManager.OnSpellUsed -= EventManager_OnSpellUsed;
            registered = false;
        }
        #endregion

        #region Player Events
        private static bool alreadyRegisteredPlayerEvents = false;
        public static void RegisterPlayerEvents() {
            if(alreadyRegisteredPlayerEvents) return;

            foreach(Wearable w in Player.currentCreature.equipment.wearableSlots) {
                w.OnItemEquippedEvent += (item) => {
                    if(ModManager.clientInstance == null) return;
                    if(ModManager.clientSync == null) return;

                    ModManager.clientSync.ReadEquipment();
                    ModManager.clientInstance.nw.SendReliable(ModManager.clientSync.syncData.myPlayerData.CreateEquipmentPacket());
                };
            }
            alreadyRegisteredPlayerEvents = true;
        }
        #endregion

        #region Item Events
        public static void AddEventsToItem(ItemSync itemSync) {
            if(itemSync.clientsideItem == null) return;
            if(itemSync.registeredEvents) return;

            itemSync.clientsideItem.OnDespawnEvent += (item) => {
                if(itemSync == null) return;
                if(itemSync.networkedId <= 0) return;
                if(itemSync.networkedId > 0 && itemSync.clientsideId > 0) { // Check if the item is already networked and is in ownership of the client
                    ModManager.clientInstance.nw.SendReliable(itemSync.DespawnPacket());
                    Log.Debug($"[Client] Event: Item { itemSync.dataId } ({ itemSync.networkedId }) is despawned.");

                    ModManager.clientSync.syncData.items.Remove(itemSync.networkedId);

                    itemSync.networkedId = 0;
                }
            };

            // If the player grabs an item with telekenesis, we give him control over the position data
            itemSync.clientsideItem.OnTelekinesisGrabEvent += (handle, teleGrabber) => {
                if(itemSync == null) return;
                if(itemSync.clientsideId > 0) return;
                if(itemSync.networkedId <= 0) return;

                ModManager.clientInstance.nw.SendReliable(itemSync.TakeOwnershipPacket());
            };

            // If the player grabs an item, we give him control over the position data, and tell the others to attach it to the character
            itemSync.clientsideItem.OnGrabEvent += (handle, ragdollHand) => {
                if(itemSync == null) return;
                if(itemSync.networkedId <= 0) return;
                itemSync.UpdateFromHolder();

                if(itemSync.drawSlot != Holder.DrawSlot.None || itemSync.creatureNetworkId <= 0) return;
                if(!itemSync.holderIsPlayer && (!ModManager.clientSync.syncData.creatures.ContainsKey(itemSync.creatureNetworkId) || ModManager.clientSync.syncData.creatures[itemSync.creatureNetworkId].clientsideId <= 0)) return;

                if(itemSync.clientsideId <= 0) {
                    ModManager.clientInstance.nw.SendReliable(itemSync.TakeOwnershipPacket());
                }

                Log.Debug($"[Client] Event: Grabbed item {itemSync.dataId} by {itemSync.creatureNetworkId} with hand {itemSync.holdingSide}.");

                if(itemSync.creatureNetworkId > 0) {
                    ModManager.clientInstance.nw.SendReliable(itemSync.SnapItemPacket());
                }
            };

            // If the item is dropped by the player, drop it for everyone
            itemSync.clientsideItem.OnUngrabEvent += (handle, ragdollHand, throwing) => {
                if(itemSync == null) return;
                if(!itemSync.AllowSyncGrabEvent()) return;
                if(itemSync.creatureNetworkId <= 0) return;
                //if(!ModManager.clientSync.syncData.creatures.ContainsKey(itemSync.creatureNetworkId)) return;

                itemSync.UpdateFromHolder();

                if(itemSync.drawSlot != Holder.DrawSlot.None) return;
                if(!itemSync.holderIsPlayer && ModManager.clientSync.syncData.creatures[itemSync.creatureNetworkId].clientsideId <= 0) return;
                
                Log.Debug($"[Client] Event: Ungrabbed item {itemSync.dataId} by {itemSync.creatureNetworkId} with hand {itemSync.holdingSide}.");

                itemSync.creatureNetworkId = 0;
                ModManager.clientInstance.nw.SendReliable(itemSync.UnSnapItemPacket());
            };

            // If the item is equipped to a slot, do it for everyone
            itemSync.clientsideItem.OnSnapEvent += (holder) => {
                if(itemSync == null) return;
                if(!itemSync.AllowSyncGrabEvent()) return;

                itemSync.UpdateFromHolder();

                if(itemSync.creatureNetworkId > 0) {
                    if(!itemSync.holderIsPlayer && ModManager.clientSync.syncData.creatures[itemSync.creatureNetworkId].clientsideId <= 0) return;

                    Log.Debug($"[Client] Event: Snapped item {itemSync.dataId} to {itemSync.creatureNetworkId} in slot {itemSync.drawSlot}.");

                    ModManager.clientInstance.nw.SendReliable(itemSync.SnapItemPacket());
                }
            };

            // If the item is unequipped by the player, do it for everyone
            itemSync.clientsideItem.OnUnSnapEvent += (holder) => {
                if(itemSync == null) return;
                if(!itemSync.AllowSyncGrabEvent()) return;

                Log.Debug($"[Client] Event: Unsnapped item {itemSync.dataId} from {itemSync.creatureNetworkId}.");
                itemSync.creatureNetworkId = 0;

                ModManager.clientInstance.nw.SendReliable(itemSync.UnSnapItemPacket());
            };

            // Check if the item is already equipped, if so, tell the others players
            if(   (itemSync.clientsideItem.holder != null && itemSync.clientsideItem.holder.creature != null)
               || (itemSync.clientsideItem.mainHandler != null && itemSync.clientsideItem.mainHandler.creature != null)) {
                if(itemSync.clientsideId <= 0) ModManager.clientInstance.nw.SendReliable(itemSync.TakeOwnershipPacket());

                itemSync.UpdateFromHolder();

                if(itemSync.creatureNetworkId <= 0) return;
                if(itemSync.networkedId <= 0) return;

                ModManager.clientInstance.nw.SendReliable(itemSync.SnapItemPacket());
            }

            //Log.Debug("Registered events on item " + itemSync.networkedId);
            itemSync.registeredEvents = true;
        }
        #endregion

        #region Creature Events
        public static void AddEventsToCreature(CreatureSync creatureSync) {
            if(creatureSync.clientsideCreature == null) return;
            if(creatureSync.registeredEvents) return;

            creatureSync.clientsideCreature.OnDamageEvent += (collisionInstance) => {
                if(creatureSync.networkedId <= 0) return;

                creatureSync.health = creatureSync.clientsideCreature.currentHealth;

                ModManager.clientInstance.nw.SendReliable(creatureSync.CreateHealthPacket());
            };

            creatureSync.clientsideCreature.OnHealEvent += (heal, healer) => {
                if(creatureSync.networkedId <= 0) return;

                creatureSync.health = creatureSync.clientsideCreature.currentHealth;

                ModManager.clientInstance.nw.SendReliable(creatureSync.CreateHealthPacket());
            };

            creatureSync.clientsideCreature.OnKillEvent += (collisionInstance, eventTime) => {
                if(creatureSync.networkedId <= 0) return;

                creatureSync.health = -1;

                ModManager.clientInstance.nw.SendReliable(creatureSync.CreateHealthPacket());
            };

            //creatureSync.clientsideCreature.brain.OnAttackEvent  += (attackType, strong, target) => {
            //    // Log.Debug("OnAttackEvent " + attackType);
            //};
            //
            //creatureSync.clientsideCreature.brain.OnStateChangeEvent += (state) => {
            //    // TODO: Sync state if necessary
            //};
            //
            //creatureSync.clientsideCreature.ragdoll.OnSliceEvent += (ragdollPart, eventTime) => {
            //    // TODO: Sync the slicing - ragdollPart.type
            //};

            creatureSync.registeredEvents = true;
        }
        #endregion

        #region Global Event Handlers
        private static void EventManager_onLevelLoad(LevelData levelData, EventTime eventTime) {
            if(eventTime == EventTime.OnEnd) {
                if(ModManager.clientInstance == null) return;

                string currentLevel = levelData.id;
                string mode = Level.current.mode.name;

                if(ModManager.clientSync.syncData.serverlevel.Equals(currentLevel.ToLower()))
                    //if(ModManager.clientSync.syncData.servermode.Equals(mode.ToLower()))
                    return;

                ModManager.clientInstance.nw.SendReliable(PacketWriter.LoadLevel(levelData.id, mode));
            }
            //else if(eventTime == EventTime.OnEnd) {
            //    if(levelData.id != "Home") return;
            //    
            //    UIMap map = FindObjectOfType<UIMap>();
            //    GameObject meep = Instantiate(GameObject.Find("WorldmapBoard"));
            //    meep.isStatic = false;
            //    map.transform.position = Player.local.transform.position + Vector3.right * 3;
            //    meep.transform.position = map.transform.position;
            //}
        }

        private static void EventManager_onItemSpawn(Item item) {
            if(Config.ignoredTypes.Contains(item.data.type)) return;
            if(ModManager.clientInstance == null) return;
            if(ModManager.clientSync == null) return;

            ModManager.clientSync.SyncItemIfNotAlready(item);
        }

        private static void EventManager_onItemEquip(Item item) {
            if(Config.ignoredTypes.Contains(item.data.type)) return;
            if(ModManager.clientInstance == null) return;
            if(ModManager.clientSync == null) return;

            //Debug.Log("EventManager.onItemEquip");
            //
            //ModManager.clientSync.ReadEquipment();
            //ModManager.clientInstance.tcp.SendPacket(ModManager.clientSync.syncData.myPlayerData.CreateEquipmentPacket());
        }

        private static void EventManager_onCreatureSpawn(Creature creature) {
            if(ModManager.clientInstance == null) return;
            if(ModManager.clientSync == null) return;
            if(!creature.pooled) return;

            foreach(CreatureSync cs in ModManager.clientSync.syncData.creatures.Values) {
                if(cs.clientsideCreature == creature) return; // If creature already exists, just exit
            }

            // Check if the creature aims for the player
            bool isPlayerTheTaget = creature.brain.currentTarget == null ? false : creature.brain.currentTarget == Player.currentCreature;

            int currentCreatureId = ModManager.clientSync.syncData.currentClientCreatureId++;
            CreatureSync creatureSync = new CreatureSync() {
                clientsideCreature = creature,
                clientsideId = currentCreatureId,

                clientTarget = isPlayerTheTaget ? ModManager.clientInstance.myClientId : 0, // If the player is the target, let the server know it

                creatureId = creature.creatureId,
                containerID = creature.container.containerID,
                factionId = creature.factionId,

                equipment = creature.ReadWardrobe()
            };

            Log.Debug($"[Client] Event: Creature {creature.creatureId} has been spawned.");

            ModManager.clientSync.syncData.creatures.Add(-currentCreatureId, creatureSync);
            ModManager.clientInstance.nw.SendReliable(creatureSync.CreateSpawnPacket());

            creature.OnDespawnEvent += (eventTime) => {
                if(creatureSync.networkedId > 0 && creatureSync.clientsideId > 0) {
                    ModManager.clientInstance.nw.SendReliable(creatureSync.CreateDespawnPacket());
                    Log.Debug($"[Client] Event: Creature {creatureSync.creatureId} ({creatureSync.networkedId}) is despawned.");

                    ModManager.clientSync.syncData.creatures.Remove(creatureSync.networkedId);

                    creatureSync.networkedId = 0;
                }
            };
        }

        private static void EventManager_onCreatureAttacking(Creature attacker, Creature targetCreature, Transform targetTransform, BrainModuleAttack.AttackType type, BrainModuleAttack.AttackStage stage) {
            if(ModManager.clientInstance == null) return;
            if(ModManager.clientSync == null) return;

            if(stage == BrainModuleAttack.AttackStage.WindUp) {
                CreatureSync creatureSync = null;
                try {
                    creatureSync = ModManager.clientSync.syncData.creatures.First(entry => entry.Value.clientsideCreature == attacker).Value;
                } catch(InvalidOperationException) { return; } // Creature is not synced

                if(creatureSync == null) return;
                if(creatureSync.networkedId <= 0) return;

                AnimatorStateInfo animatorStateInfo = creatureSync.clientsideCreature.animator.GetCurrentAnimatorStateInfo(creatureSync.clientsideCreature.animator.layerCount - 1);

                ModManager.clientInstance.nw.SendReliable(PacketWriter.CreatureAnimation(creatureSync.networkedId, animatorStateInfo.fullPathHash, creatureSync.clientsideCreature.GetAttackAnimation()));
            }
        }

        private static void EventManager_OnSpellUsed(string spellId) {
            
        }
        #endregion
    }
}
