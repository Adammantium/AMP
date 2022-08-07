﻿using ThunderRoad;

namespace AMP.Data {
    public static class Config {

        public static readonly ItemData.Type[] ignoredTypes = {
            ItemData.Type.Body,
            ItemData.Type.Spell,
            //ItemData.Type.Prop,
            ItemData.Type.Wardrobe
        };


        // Channel = Head
        // Layer 6 = Hair
        // Layer 5 = Beard
        public static readonly int[] headDetailLayers = {
            6, // Hair
            5, // Beard
        };

        public const int TICK_RATE = 15;

        public const int MAX_ITEMS_FOR_CLIENT = 150; // TODO: Maybe implement a limit

        // Assume the item is the same if they are the same if they are not that much apart
        public const float ITEM_CLONE_MAX_DISTANCE = 0.2f * 0.2f; //~20cm

        // Min distance a item needs to move before its position is updated
        public const float REQUIRED_MOVE_DISTANCE = 0.0001f; // ~1cm

        // Min distance a item needs to move before its position is updated
        public const float REQUIRED_PLAYER_MOVE_DISTANCE = 0.0001f; // ~1cm

        // Min distance a item needs to move before its position is updated
        public const float REQUIRED_ROTATION_DISTANCE = 2f * 2f; // ~2°

        // Distance needed for the ragdoll to be teleported to the player (Happens when it's glitching out)
        public const float RAGDOLL_TELEPORT_DISTANCE = 2f * 2f; // ~2m


        public const long DISCORD_CLIENT_ID = 1005583334392471583;
    }
}
