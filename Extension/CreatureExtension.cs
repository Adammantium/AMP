using AMP.Data;
using AMP.Logging;
using AMP.Network.Helper;
using System;
using System.Collections.Generic;
using System.Reflection;
using ThunderRoad;
using UnityEngine;

namespace AMP.Extension {
    internal static class CreatureExtension {

        internal static bool IsOtherPlayer(this Creature creature) {
            bool isOtherPlayer;
            long networkId;
            SyncFunc.GetCreature(creature, out isOtherPlayer, out networkId);

            if(isOtherPlayer && networkId == ModManager.clientInstance.myPlayerId) isOtherPlayer = false;

            return isOtherPlayer;
        }

        internal static string[] ReadWardrobe(this Creature creature) {
            List<string> equipment_list = new List<string>();

            foreach(ContainerData.Content content in creature.container.contents) {
                if(content.itemData.type == ItemData.Type.Wardrobe) {
                    equipment_list.Add(content.referenceID);
                }
            }

            return equipment_list.ToArray();
        }

        internal static void ApplyWardrobe(this Creature creature, string[] equipment_list) {
            bool changed = false;

            foreach(string referenceID in equipment_list) {
                bool found = false;
                foreach(ContainerData.Content content in creature.container.contents) {
                    if(content.itemData.type != ItemData.Type.Wardrobe) continue;
                    if(content.referenceID.Equals(referenceID)) {
                        found = true;
                        break;
                    }
                }
                if(!found) {
                    ItemData itemData = Catalog.GetData<ItemData>(referenceID);
                    if(itemData == null) {
                        Log.Err(Defines.CLIENT, $"Equipment { referenceID } for { creature.creatureId } not found, please check you mods.");
                    }
                    if(itemData != null && itemData.type == ItemData.Type.Wardrobe) {
                        ContainerData.Content content = new ContainerData.Content(itemData);
                        creature.equipment.EquipWardrobe(content, false);
                        changed = true;
                    }
                }
            }
            if(changed) {
                creature.equipment.UpdateParts();
            }
        }

        internal static Color[] ReadColors(this Creature creature) {
            List<Color> color_list = new List<Color>();

            color_list.Add(creature.GetColor(Creature.ColorModifier.Hair));
            color_list.Add(creature.GetColor(Creature.ColorModifier.HairSecondary));
            color_list.Add(creature.GetColor(Creature.ColorModifier.HairSpecular));
            color_list.Add(creature.GetColor(Creature.ColorModifier.EyesIris));
            color_list.Add(creature.GetColor(Creature.ColorModifier.EyesSclera));
            color_list.Add(creature.GetColor(Creature.ColorModifier.Skin));

            return color_list.ToArray();
        }


        internal static void ApplyColors(this Creature creature, Color[] colors) {
            int i = 0;
            creature.SetColor(colors[i++], Creature.ColorModifier.Hair);
            creature.SetColor(colors[i++], Creature.ColorModifier.HairSecondary);
            creature.SetColor(colors[i++], Creature.ColorModifier.HairSpecular);
            creature.SetColor(colors[i++], Creature.ColorModifier.EyesIris);
            creature.SetColor(colors[i++], Creature.ColorModifier.EyesSclera);
            creature.SetColor(colors[i++], Creature.ColorModifier.Skin, true);
        }

        internal static string GetAttackAnimation(this Creature creature) {
            // Use Reflection to read the current animationClipOverrides
            Type typecontroller = typeof(Creature);
            FieldInfo finfo = typecontroller.GetField("animationClipOverrides", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
            
            KeyValuePair<AnimationClip, AnimationClip>[] animationClipOverrides;
            if(finfo != null) {
                animationClipOverrides = (KeyValuePair<AnimationClip, AnimationClip>[])finfo.GetValue(creature);

                foreach(KeyValuePair<AnimationClip, AnimationClip> kvp in animationClipOverrides) {
                    return kvp.Value.name; // Return the first value, because it should be the attack animation (Have to check that sometime, BowtAI told me there are 2)
                }
            }
            return "";
        }

        private static Dictionary<string, AnimationClip> animationClips = new Dictionary<string, AnimationClip>();
        internal static void PlayAttackAnimation(this Creature creature, string clipName) {
            // Cache all animations from the AnimationData
            if(animationClips.Count == 0) {
                List<AnimationData> data = Catalog.GetDataList<AnimationData>();
                foreach(AnimationData ad in data) {
                    foreach(AnimationData.Clip adc in ad.animationClips) {
                        string name = adc.animationClip.name.ToLower();
                        if(!animationClips.ContainsKey(name)) {
                            animationClips.Add(name, adc.animationClip);
                        }
                    }
                }
                Log.Debug(Defines.CLIENT, "AnimationClips populated " + animationClips.Count + "\n" + string.Join("\n", animationClips.Keys));
            }
            
            // Check if the animation clip is inside the cache
            clipName = clipName.ToLower();
            if(!animationClips.ContainsKey(clipName)) {
                Log.Err(Defines.CLIENT, $"Attack animation { clipName } not found, please check you mods.");
                return;
            }
            
            // Play the animation
            creature.PlayAnimation(animationClips[clipName], false);
            //creature.UpdateOverrideClip(new KeyValuePair<int, AnimationClip>(0, animationClips[clipName]));
        }

        internal static Vector3[] ReadRagdoll(this Creature creature) {
            List<Vector3> result = new List<Vector3>();
            foreach(Ragdoll.Bone bone in creature.ragdoll.bones) {
                if(bone.part == null) continue;
                result.Add(bone.part.transform.position);
                result.Add(bone.part.transform.eulerAngles);
            }
            return result.ToArray();
        }

        internal static void ApplyRagdoll(this Creature creature, Vector3[] vectors) {
            int i = 0;
            foreach(Ragdoll.Bone bone in creature.ragdoll.bones) {
                if(bone.part == null) continue;
                if(vectors.Length <= i) continue; // Prevent errors when the supplied vectors dont match the creatures
                
                bone.part.transform.position = vectors[i++];
                bone.part.transform.eulerAngles = vectors[i++];
            }
        }

        internal static void SmoothDampRagdoll(this Creature creature, Vector3[] vectors, ref Vector3[] velocities) { creature.SmoothDampRagdoll(vectors, ref velocities, Vector3.zero); }

        internal static void SmoothDampRagdoll(this Creature creature, Vector3[] vectors, ref Vector3[] velocities, Vector3 pos_offset) {
            Vector3[] new_vectors = new Vector3[vectors.Length];
            int i = 0;
            foreach(Ragdoll.Bone bone in creature.ragdoll.bones) {
                if(bone.part == null) continue;
                if(vectors.Length <= i) continue; // Prevent errors when the supplied vectors dont match the creatures

                new_vectors[i] = bone.part.transform.position.InterpolateTo(vectors[i] + pos_offset, ref velocities[i], Config.MOVEMENT_DELTA_TIME); i++;
                new_vectors[i] = bone.part.transform.eulerAngles.InterpolateEulerTo(vectors[i], ref velocities[i], Config.MOVEMENT_DELTA_TIME); i++;
            }
            creature.ApplyRagdoll(new_vectors);
        }

        internal static bool IsRagdolled(this Creature creature) {
            return (ModManager.safeFile.modSettings.useAdvancedNpcSyncing && creature.ragdoll.state != Ragdoll.State.NoPhysic)
                || creature.isKilled
                || (creature.spawnTime + 2 > Time.time && creature.ragdoll != null && creature.ragdoll.state == Ragdoll.State.Inert)
                ;
        }
    }
}
