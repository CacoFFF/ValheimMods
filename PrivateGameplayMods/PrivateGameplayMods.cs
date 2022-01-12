using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace PrivateGameplayMods
{
    [BepInPlugin(Package, ModName, Version)]
    public class PrivateGameplayMods : BaseUnityPlugin
    {
        public const string Package = "CacoFFF.valheim.PrivateGameplayMods";
        public const string Version = "0.0.5";
        public const string ModName = "Private Gameplay Mods";

        private static ConfigEntry<bool> _EnableSkillMod;
        private static ConfigEntry<bool> _EnableShipMod;
        private static ConfigEntry<bool> _EnablePieceMod;
        private static ConfigEntry<bool> _EnableNightServerMod;
        private static ConfigEntry<bool> _EnableDropMod;
        private static ConfigEntry<bool> _EnableItemLevels;
        private static ConfigEntry<bool> _EnableHalfMetalUpgrade;
        private static ConfigEntry<bool> _EnableLargeContainers;
        private static ConfigEntry<float> _SetPlaceDistance;

        private Harmony _harmony;




        private void Awake()
        {
            _EnableSkillMod = Config.Bind("Global", "EnableSkillMod", true, "Enable skill mod.");
            _EnableShipMod = Config.Bind("Global", "EnableShipMod", true, "Enable ship mod.");
            _EnablePieceMod = Config.Bind("Global", "EnablePieceMod", true, "Enable piece mod.");
            _EnableNightServerMod = Config.Bind("Global", "EnableNightServerMod", true, "Enable night mod.");
            _EnableDropMod = Config.Bind("Global", "EnableDropMod", true, "Enable drop mod.");
            _EnableItemLevels = Config.Bind("Global", "EnableItemLevels", true, "Enable item levels.");
            _EnableHalfMetalUpgrade = Config.Bind("Global", "EnableHalfMetalUpgrade", true, "Enable half metal upgrade requirements.");
            _EnableLargeContainers = Config.Bind("Global", "EnableLargeContainers", true, "Enable larger containers.");
            _SetPlaceDistance = Config.Bind("Global", "SetPlaceDistance", 5.0f, "Set Place Distance.");

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }


        // ZDO has a 'drops' key with 'num' as value
        // Then, starting from 0 to num-1 it has 'drop_hashN' and 'drop_amountN' set after
        // drop list is generated

        // Character.SetLevel to modify monster stars

        // ZDO has a 'max_health' key, float type
        // Character.GetStandingOnShip() !!
        // Character.InInterior() for dungeon check


        [HarmonyPatch(typeof(Skills))]
        private class SkillModPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(Skills.RaiseSkill))]
            private static void RaiseSkillPrefix(ref Skills __instance, ref Skills.SkillType skillType, ref float factor, Dictionary<Skills.SkillType,Skills.Skill>  ___m_skillData)
            {
                if ( !_EnableSkillMod.Value || __instance == null ) 
                    return;

                if ( skillType == Skills.SkillType.None )
                    return;

                if ( ___m_skillData.TryGetValue(skillType, out Skills.Skill skill) )
                    factor *= 1.0f + 4.0f * skill.m_level / 100.0f;
            }
        }

        
        [HarmonyPatch(typeof(Ship),"Awake")]
        private class ShipModPatch
        {
            [HarmonyPostfix]
            private static void AwakePostFix(ref Ship __instance, ref float ___m_sailForceFactor, ref float ___m_rudderSpeed)
            {
                if( !_EnableShipMod.Value || __instance == null )
                    return;

                ___m_rudderSpeed *= 2f;
                ___m_sailForceFactor *= 2f;
            }
        }

        [HarmonyPatch(typeof(WearNTear))]
        private class PieceDamagePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(WearNTear.Damage))]
            private static void DamagePrefix(ref WearNTear __instance, ref HitData hit, ZNetView ___m_nview)
            {
                if( !_EnablePieceMod.Value || !__instance || !___m_nview.IsValid() )
                    return;

                float Factor = 0.8f;
                if ( !hit.HaveAttacker() || !hit.GetAttacker().IsPlayer() )
                    Factor *= 0.5f;
                hit.m_damage.Modify(Factor);
/*                hit.m_damage.m_damage *= Factor;
                hit.m_damage.m_blunt *= Factor;
                hit.m_damage.m_chop *= Factor;
                hit.m_damage.m_fire *= Factor;
                hit.m_damage.m_frost *= Factor;
                hit.m_damage.m_lightning *= Factor;
                hit.m_damage.m_slash *= Factor;
                hit.m_damage.m_spirit *= Factor;
                hit.m_damage.m_poison *= Factor;
                hit.m_damage.m_pickaxe *= Factor;
                hit.m_damage.m_pierce *= Factor;*/
            }
        }

        [HarmonyPatch(typeof(EnvMan), "RescaleDayFraction")]
        private class EnvManPatch
        {
            [HarmonyPostfix]
            private static void RescaleDayFractionPostfix( float fraction, ref float __result)
            {
                // Make nights slightly longer
                if (_EnableNightServerMod.Value )
                    __result = fraction * 0.1f + __result * 0.9f;
            }
        }

        [HarmonyPatch(typeof(ObjectDB),"Awake")]
        private class DropPatch
        {
            public static GameObject OozerObject;

            [HarmonyPostfix]
            private static void AwakePostfix()
            {
                bool RecipesFound = false;
                if ( (ObjectDB.instance != null) && (ObjectDB.instance.m_recipes != null) )
                    foreach (Recipe recipe in ObjectDB.instance.m_recipes )
                    {
                        if ( _EnableHalfMetalUpgrade.Value )
                        {
                            if( (recipe != null) && (recipe.m_resources != null) )
                            {
                                RecipesFound = true;
                                foreach(Piece.Requirement requirement in recipe.m_resources)
                                {
                                    if((requirement.m_amountPerLevel > 1)
                                        && (requirement.m_resItem != null)
                                        && ((requirement.m_resItem.name == "Silver")
                                          || (requirement.m_resItem.name == "Iron")
                                          || (requirement.m_resItem.name == "Bronze")
                                          || (requirement.m_resItem.name == "Tin")
                                          || (requirement.m_resItem.name == "Copper")
                                          || (requirement.m_resItem.name == "Flametal")
                                          || (requirement.m_resItem.name == "BlackMetal")))
                                    {
 //                                       if ( recipe.m_item != null )
  //                                          ZLog.Log("Lowering upgrade reqs for " + recipe.m_item.name + ": " + requirement.m_resItem.name);
                                        requirement.m_amountPerLevel = (requirement.m_amountPerLevel + 1) / 2;
                                    }
                                }

                            }
                        }
                    }

                // Already initialized (?)
                if ( !RecipesFound || OozerObject != null )
                    return;

                Object[] array = Resources.FindObjectsOfTypeAll(typeof(GameObject));
                List<Pickable> PlantPickables = new List<Pickable>();
                for(int i = 0; i < array.Length; i++)
                {
                    GameObject gameObject = (GameObject)array[i];
                    if ( _EnableDropMod.Value )
                    {
                        if (gameObject.name == "BlobElite")
                        {
                            OozerObject = gameObject;
                            CharacterDrop characterDrop = gameObject.GetComponent<CharacterDrop>();
                            if ( characterDrop != null )
                            {
                                foreach ( CharacterDrop.Drop drop in characterDrop.m_drops )
                                {
                                    if ( drop.m_prefab != null && drop.m_prefab.name == "IronScrap" )
                                        drop.m_chance = 1.0f;
                                }
                            }
                        }
                    

                        Plant plant = gameObject.GetComponent<Plant>();
                        if ( plant != null )
                        {
                            foreach ( GameObject prefab in plant.m_grownPrefabs )
                            {
                                if ( prefab == null )
                                    continue;
//                                ZLog.Log("Plant Prefab for " + plant.name + ": " + prefab);
                                Pickable pickablePl = prefab.GetComponent<Pickable>();
                                if (pickablePl != null && pickablePl.m_itemPrefab != null && pickablePl.m_amount > 2 )
                                    PlantPickables.Add(pickablePl);
                             }
                        }

                        Pickable pickable = gameObject.GetComponent<Pickable>();
                        if ( pickable != null && pickable.m_itemPrefab != null 
                            && (pickable.m_itemPrefab.name == "Flax" || pickable.m_itemPrefab.name == "Barley") )
                            PlantPickables.Add(pickable);
                    }

                    if ( _EnableItemLevels.Value )
                    {
                        ItemDrop itemDrop = gameObject.GetComponent<ItemDrop>();
                        if( (itemDrop != null) && (itemDrop.m_itemData != null) && (itemDrop.m_itemData.m_shared != null) && (itemDrop.m_itemData.m_shared.m_maxQuality > 1) )
                            itemDrop.m_itemData.m_shared.m_maxQuality += 10;
                    }

 
                }

                foreach ( Pickable pickable in PlantPickables )
                {
                    float NewAmount = (float)pickable.m_amount * 1.25f + 0.55f;
                    ZLog.Log("Increasing drop count for " + pickable.name + ": " + pickable.m_itemPrefab.name + " " + pickable.m_amount + " > " + NewAmount);

                    // Increase integer part
                    pickable.m_amount = (int)NewAmount;
                    NewAmount -= (float)pickable.m_amount;

                    // Add chance part (doesn't appear to work)
                    if(NewAmount > 0.01f)
                    {
                        if(pickable.m_extraDrops == null)
                            pickable.m_extraDrops = new DropTable();
                        DropTable.DropData NewDrop = new DropTable.DropData();
                        NewDrop.m_item = pickable.m_itemPrefab;
                        NewDrop.m_stackMin = 1;
                        NewDrop.m_stackMax = 1;
                        NewDrop.m_weight = 1.0f;
                        pickable.m_extraDrops.m_drops.AddItem(NewDrop);
                        pickable.m_extraDrops.m_dropChance = NewAmount;
                    }
                }
            }
        }

        //
        // Detect mountains
        //
        [HarmonyPatch(typeof(WorldGenerator), "FindMountains")]
        private class MountainDetectorPatch
        {
            [HarmonyPostfix]
            private static void FindMountainsPostfix( ref WorldGenerator __instance)
            {
                List<Vector2> Mountains = __instance.GetMountains();
                foreach ( Vector2 Location in Mountains )
                {
                    float H = __instance.GetHeight(Location.x, Location.y);
                    if ( H > 200.0f )
                        ZLog.Log("Large Mountain at " + (int)(Location.x) + "," + (int)(Location.y) + " => " + (int)H);
                }
            }
        }

        //
        // Piece awake mods:
        // Ship, Cart larger containers
        // Auto-expand containers instead of deleting items
        // Reduce weight
        //
        [HarmonyPatch(typeof(Vagon), "UpdateLoadVisualization")]
        private class PieceContainerExpandOnCreate
        {
            [HarmonyPostfix]
            private static void UpdateLoadVisualizationPostfix(ref Vagon __instance)
            {
                if ( !_EnableLargeContainers.Value )
                    return;
                
                Container CT = __instance.m_container;
                if ( CT != null && CT.GetInventory() != null )
                {
                    Traverse.Create(CT.GetInventory()).Field("m_width").SetValue(8);
                    Traverse.Create(CT.GetInventory()).Field("m_height").SetValue(5);
                }
            }
        }
        [HarmonyPatch(typeof(Inventory), "AddItem", new System.Type[] {typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int)})]
        private class PieceContainerExpandOnLoad
        {
            [HarmonyPrefix]
            private static void AddItemPrefix(ItemDrop.ItemData item, int amount, int x, int y, ref int ___m_width, ref int ___m_height)
            {
                if ( x >= ___m_width )
                    ___m_width = x+1;
                if ( y >= ___m_height )
                    ___m_height = y+1;
            }
        }
        [HarmonyPatch(typeof(Vagon), "SetMass")]
        private class ReduceVagonWeight
        {
            [HarmonyPrefix]
            private static void SetMassPrefix(ref float mass)
            {
                if ( _EnableLargeContainers.Value )
                    mass *= 0.3f;
            }
        }


        //
        // Update the Place Distance in runtime
        //
        [HarmonyPatch(typeof(Player), "UpdateHover")]
        private class PlayerPlacePatch
        {
            [HarmonyPostfix]
            private static void UpdateHoverPostfix( ref Player __instance)
            {
                if ( __instance )
                    __instance.m_maxPlaceDistance = _SetPlaceDistance.Value;
            }
        }

        [HarmonyPatch(typeof(Terminal), "TryRunCommand")]
        private class TerminalPatch
        {
            [HarmonyPostfix]
            private static void TryRunCommandPostfix(ref Terminal __instance, string text)
            {
                if ( __instance == null )
                    return;

                string[] array = text.Split(' ');
                if ( array[0] == "resetterrain" )
                {
                    ZLog.Log("Command: resetterrain");
                    try
                    {
                        float radius = 10.0f;
                        float.TryParse(array[1], out radius);
                        if ( radius <= 0.0f )
                            radius = 10.0f;
                        foreach(TerrainModifier allInstance in TerrainModifier.GetAllInstances())
                        {
                            if( allInstance != null && Utils.DistanceXZ(Player.m_localPlayer.transform.position, allInstance.transform.position) < radius )
                            {
                                ZNetView component = Traverse.Create(allInstance).Field("m_nview").GetValue() as ZNetView;
                                if( component != null && component.IsValid() )
                                {
                                    component.ClaimOwnership();
                                    component.Destroy();
                                }
                            }
                        }
                        TerrainComp compiler = TerrainComp.FindTerrainCompiler(Player.m_localPlayer.transform.position);
                        if ( compiler != null )
                        {
                            ZNetView component = Traverse.Create(compiler).Field("m_nview").GetValue() as ZNetView;
                            if ( component != null && component.IsValid() )
                            {
                                component.ClaimOwnership();
                                component.Destroy();
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        ZLog.Log("Terrain exception");
                    }
                }
            }
        }
    }
}