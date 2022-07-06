﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Net.NetworkInformation;
using System.ComponentModel;
using System.Threading;
using Ping = System.Net.NetworkInformation.Ping;
using ThunderRoad;
using System.Collections;
using AMP.Network.Client;
using AMP.Network.Server;
using System.Reflection;

namespace AMP {
    public class GUIManager : MonoBehaviour {
        public string ip = "127.0.0.1";
        public int maxPlayers = 4;
        public string port = "26950";
        public int menu = 0;

        public long ping;

        private Rect windowRect = new Rect(Screen.width - 210, 10, 200, 130);

        void pingCallback(object sender, PingCompletedEventArgs e) {
            if (e==null || e.Cancelled || e.Error!=null || e.Reply==null) return;
            ping = e.Reply.RoundtripTime;
        }

        string title = ModManager.MOD_NAME;

        /// <summary>
        /// Will display the multiplayer gui
        /// </summary>
        private void OnGUI() {
            windowRect = GUI.Window(0, windowRect, PopulateWindow, title);
        }

        private void PopulateWindow(int id) {
            if(ModManager.serverInstance != null) {
                title = "[ Server | Port: " + port + " ]";

                GUILayout.Label("Players: " + ModManager.serverInstance.connectedClients + " / " + maxPlayers);
                //GUILayout.Label("Creatures: " + Creature.all.Count + " (Active: " + Creature.allActive.Count + ")");
                GUILayout.Label("Items: " + ModManager.serverInstance.spawnedItems);
                
            } else if(ModManager.clientInstance != null) {
                title = "[ Client @ " + ip + " ]";

                if(ModManager.clientInstance.isConnected) {

                } else {
                    GUILayout.Label("Connecting...");
                }

                //GUILayout.Label("Ping: " + ping + "ms");

                if(GUI.Button(new Rect(10, 105, 180, 20), "Disconnect")) {
                    ModManager.StopClient();
                }
            } else {
                if(GUI.Button(new Rect(10, 25, 85, 20), menu == 0 ? "[ Connect ]" : "Connect")) {
                    menu = 0;
                }
                if(GUI.Button(new Rect(105, 25, 85, 20), menu == 1 ? "[ Server ]" : "Server")) {
                    menu = 1;
                }

                if(menu == 0) {
                    GUI.Label(new Rect(15, 50, 30, 20), "IP:");
                    GUI.Label(new Rect(15, 75, 30, 20), "Port:");

                    ip = GUI.TextField(new Rect(50, 50, 140, 20), ip);
                    port = GUI.TextField(new Rect(50, 75, 140, 20), port);

                    if(GUI.Button(new Rect(10, 100, 180, 20), "Connect")) {
                        ModManager.JoinServer(ip, int.Parse(port));
                    }
                } else {
                    GUI.Label(new Rect(15, 50, 30, 20), "Max:");
                    GUI.Label(new Rect(15, 75, 30, 20), "Port:");

                    maxPlayers = (int)GUI.HorizontalSlider(new Rect(53, 55, 110, 20), maxPlayers, 2, 10);
                    GUI.Label(new Rect(175, 50, 30, 20), maxPlayers.ToString());
                    //maxPlayers = GUI.TextField(new Rect(50, 50, 140, 20), maxPlayers);
                    port = GUI.TextField(new Rect(50, 75, 140, 20), port);

                    if(GUI.Button(new Rect(10, 100, 180, 20), "Start Server")) {
                        ModManager.HostServer(maxPlayers, int.Parse(port));
                    }
                }
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }


        //void OnGUI() {
        //    if(GUI.Button(new Rect(0, 0, 100, 50), "Spawn")) {
        //        CreatureData creatureData = Catalog.GetData<CreatureData>("HumanMale");
        //        if(creatureData != null) {
        //            Vector3 position = Player.local.transform.position + (Vector3.right * 2);
        //            Quaternion rotation = Player.local.transform.rotation;
        //
        //            foreach(ValueDropdownItem<string> val in creatureData.GetAllBrainID()) {
        //                Debug.Log(val.Value);
        //            }
        //
        //            creatureData.brainId = "HumanStatic";
        //            creatureData.containerID = "PlayerDefault";
        //            creatureData.factionId = 0;
        //
        //            creatureData.SpawnAsync(position, rotation, null, true, null, creature => {
        //                Debug.Log("Spawned Dummy");
        //
        //                creature.maxHealth = 100000;
        //                creature.currentHealth = creature.maxHealth;
        //
        //                creature.isPlayer = false;
        //                creature.enabled = false;
        //                creature.locomotion.enabled = false;
        //                creature.animator.enabled = false;
        //                creature.ragdoll.enabled = false;
        //                foreach(RagdollPart ragdollPart in creature.ragdoll.parts) {
        //                    foreach(HandleRagdoll hr in ragdollPart.handles) hr.enabled = false;
        //                    ragdollPart.sliceAllowed = false;
        //                    ragdollPart.enabled = false;
        //                }
        //                creature.brain.Stop();
        //                creature.StopAnimation();
        //                creature.brain.StopAllCoroutines();
        //                creature.locomotion.MoveStop();
        //                creature.animator.speed = 0f;
        //
        //                Creature.all.Remove(creature);
        //                Creature.allActive.Remove(creature);
        //
        //                StartCoroutine(moveTest(creature));
        //            });
        //        }
        //    }
        //}

        private IEnumerator moveTest(Creature creature) {
            while(true) {
                yield return new WaitForSeconds(2f);
                creature.Teleport(creature.transform.position + Vector3.right, creature.transform.rotation);
            }
        }
    }
}
