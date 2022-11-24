﻿using AMP.Data;
using AMP.GameInteraction;
using AMP.Logging;
using AMP.Network.Client;
using AMP.Network.Handler;
using AMP.Network.Server;
using AMP.Overlay;
using AMP.Threading;
using AMP.Useless;
using AMP.Web;
using System;
using System.IO;
using ThunderRoad;
using UnityEngine;

namespace AMP {
    public class ModManager : MonoBehaviour {
        public static ModManager instance;

        public static Server serverInstance;

        internal static Client clientInstance;
        internal static ClientSync clientSync;

        internal static GUIManager guiManager;
        internal static DiscordGUIManager discordGuiManager;

        internal static bool discordNetworking = true;

        public static SafeFile safeFile;

        void Awake() {
            if (instance != null) {
                Destroy(gameObject);
                return;
            } else {
                instance = this;
                DontDestroyOnLoad(gameObject);

                Initialize();
            }
        }

        internal void Initialize() {
            Log.loggerType = Log.LoggerType.UNITY;

            safeFile = SafeFile.Load(Path.Combine(Application.streamingAssetsPath, "Mods", "MultiplayerMod", "config.json"));

            discordGuiManager = gameObject.AddComponent<DiscordGUIManager>();
            guiManager = gameObject.AddComponent<GUIManager>();

            discordGuiManager.enabled = false;

            if(safeFile.modSettings.useBrowserIntegration) {
                WebSocketInteractor.Start();
            }

            gameObject.AddComponent<EventHandler>();

            EventManager.onLevelLoad += (levelData, eventTime) => {
                if(eventTime == EventTime.OnEnd) {
                    SecretLoader.DoLevelStuff();
                }
            };

            Log.Info($"<color=#FF8C00>[AMP] { Defines.MOD_NAME } has been initialized.</color>");
        }

        void Update() {
            Dispatcher.UpdateTick();
        }

        #if TEST_BUTTONS
        void OnGUI() {
            if(GUI.Button(new Rect(0, 0, 100, 30), "Dump scenes")) {
                LevelLayoutExporter.Export();
            }
        }
        #endif

        void OnApplicationQuit() {
            Exit();
        }

        void OnDestroy() {
            Exit();
        }

        private void Exit() {
            WebSocketInteractor.Stop();
            if(clientInstance != null) {
                StopClient();
            }
            if(serverInstance != null && serverInstance.isRunning) {
                StopHost();
            }
        }


        internal static void JoinServer(NetworkHandler networkHandler, string password = "") {
            StopClient();

            clientInstance = new Client(networkHandler);
            networkHandler.Connect(password);

            if(!networkHandler.isConnected) {
                clientInstance = null;
            } else {
                if(instance.gameObject.GetComponent<ClientSync>() == null) {
                    clientSync = instance.gameObject.AddComponent<ClientSync>();
                }
                EventHandler.RegisterGlobalEvents();
                LevelFunc.EnableRespawning();
            }
        }

        internal static bool HostServer(uint maxPlayers, int port, string password = "") {
            StopClient();
            StopHost();

            serverInstance = new Server(maxPlayers, port, password);
            serverInstance.Start();

            if(serverInstance.isRunning) {
                return true;
            } else {
                serverInstance.Stop();
                serverInstance = null;
                throw new Exception("[Server] Server start failed. Check if an other program is running on that port.");
            }
        }

        public static bool HostDedicatedServer(uint maxPlayers, int port, string password = "") {
            new Dispatcher();

            if(HostServer(maxPlayers, port, password)) {
                return true;
            }
            return false;
        }

        internal static void StopClient() {
            if(clientInstance == null) return;

            EventHandler.UnRegisterGlobalEvents();
            LevelFunc.DisableRespawning();

            clientInstance?.Disconnect();
            clientSync?.Stop();
            if(clientSync != null) Destroy(clientSync);

            clientInstance = null;
            clientSync = null;
        }

        public static void StopHost() {
            if(serverInstance == null) return;
            serverInstance.Stop();
            serverInstance = null;
        }

    }
}
