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
        public const string Version = "0.0.7";
        public const string ModName = "Private Gameplay Mods";

        private static ConfigEntry<bool> _EnableSkillMod;
        private static ConfigEntry<bool> _EnableShipMod;
        private static ConfigEntry<bool> _EnablePieceMod;
        private static ConfigEntry<bool> _EnableNightServerMod;
        private static ConfigEntry<bool> _EnableDropMod;
        private static ConfigEntry<bool> _EnableItemLevels;
        private static ConfigEntry<bool> _EnableHalfMetalUpgrade;
        private static ConfigEntry<bool> _EnableLargeContainers;
        private static ConfigEntry<bool> _EnableFogReduction;
        private static ConfigEntry<float> _OriginalTamedHPScale;
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
            _EnableFogReduction = Config.Bind("Global", "EnableFogReduction", true, "Enable Fog Reduction.");
            _OriginalTamedHPScale = Config.Bind("Global", "OriginalTamedHPScale", 2.0f, "HP Scale of wild tamed creatures.");
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

                float preScaleFactor = 1.0f;
                float postScaleFactor = 1.0f;

                // Adjust skill growth curve for some skills
                switch (skillType)
                {
                    case Skills.SkillType.Jump:
                    case Skills.SkillType.Run:
                        preScaleFactor = 0.5f;
                        break;
                    case Skills.SkillType.Sneak:
                    case Skills.SkillType.Swim:
                        postScaleFactor = 1.5f;
                        break;
                    case Skills.SkillType.Fishing:
                        preScaleFactor = 0.9f;
                        postScaleFactor = 2.0f;
                        break;
                    case Skills.SkillType.Bows:
                        preScaleFactor = 1.2f;
                        break;
                }

                if( ___m_skillData.TryGetValue(skillType, out Skills.Skill skill) )
                    factor *= (1.0f + (4.0f * skill.m_level / 100.0f) * preScaleFactor) * postScaleFactor;
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
            public static GameObject FenringObject;
            public static GameObject FenrisHairObject;

            private static bool HasDrop( GameObject gameObject, string DropPrefabName)
            {
                CharacterDrop characterDrop = gameObject.GetComponent<CharacterDrop>();
                if ( characterDrop != null )
                {
                    foreach( CharacterDrop.Drop drop in characterDrop.m_drops )
                    {
                        ZLog.Log("Has drop: " + drop.m_prefab.name + " chance: " + drop.m_chance );
                        if ( (drop.m_prefab != null) && (drop.m_prefab.name == DropPrefabName) )
                            return true;
                    }
                }
                return false;
            }

            private static void AddMonsterDrops()
            {
                ZLog.Log("AddMonsterDrops()");
                if ( !_EnableDropMod.Value )
                    return;

                // Set Iron drop chance to 100% on Oozers
                if ( OozerObject != null )
                {
                    CharacterDrop characterDrop = OozerObject.GetComponent<CharacterDrop>();
                    if ( characterDrop != null )
                    {
                        foreach (CharacterDrop.Drop drop in characterDrop.m_drops )
                        {
                            if ( drop.m_prefab != null && drop.m_prefab.name == "IronScrap" )
                            {
                                ZLog.Log("Setting BlobElite drop rate for IronScrap to 1.0");
                                drop.m_chance = 1.0f;
                            }
                        }
                    }
                }

                // Add Fenris hair drop to Fenring
                if ( (FenringObject != null) && (FenrisHairObject != null) && !HasDrop(FenringObject,FenrisHairObject.name) )
                {
                    ZLog.Log("Adding Fenris hair drop to Fenring");
                    CharacterDrop characterDrop = FenringObject.GetComponent<CharacterDrop>();
                    if(characterDrop != null)
                    {
                        CharacterDrop.Drop newDrop = new CharacterDrop.Drop();
                        newDrop.m_prefab          = FenrisHairObject;
                        newDrop.m_amountMin       = 1;
                        newDrop.m_amountMax       = 1;
                        newDrop.m_chance          = 0.33f;
                        newDrop.m_levelMultiplier = true;
                        newDrop.m_onePerPlayer    = false;
                        characterDrop.m_drops.Add(newDrop);
                    }
                }
            }

            [HarmonyPostfix]
            private static void AwakePostfix()
            {
                bool RecipesFound = false;
                if ( (ObjectDB.instance != null) && (ObjectDB.instance.m_recipes != null) )
                    foreach (Recipe recipe in ObjectDB.instance.m_recipes )
                    {
                        if( (recipe != null) && (recipe.m_resources != null) )
                        {
                            RecipesFound = true;
                            if( _EnableHalfMetalUpgrade.Value )
                            {
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
//                                      if ( recipe.m_item != null )
//                                          ZLog.Log("Lowering upgrade reqs for " + recipe.m_item.name + ": " + requirement.m_resItem.name);
                                        requirement.m_amountPerLevel = (requirement.m_amountPerLevel + 1) / 2;
                                    }
                                }
                            }
                            if ( !recipe.m_enabled && (recipe.m_item != null) )
                                ZLog.Log("Disabled recipe for " + recipe.m_item.name);
                        }
                    }

                // Already initialized (?)
                if ( !RecipesFound || OozerObject != null )
                {
                    AddMonsterDrops();
                    return;
                }

                Object[] array = Resources.FindObjectsOfTypeAll(typeof(GameObject));
                List<Pickable> PlantPickables = new List<Pickable>();
                List<GameObject> AddToWorkbench = new List<GameObject>();
//                List<Fish> Fishes = new List<Fish>();
                PieceTable BuildPieces = null;

                for(int i = 0; i < array.Length; i++)
                {
                    GameObject gameObject = (GameObject)array[i];
                    if ( _EnableDropMod.Value )
                    {
                        if (gameObject.name == "BlobElite")
                            OozerObject = gameObject;
                        else if (gameObject.name == "Fenring")
                            FenringObject = gameObject;
                        else if (gameObject.name == "WolfHairBundle")
                            FenrisHairObject = gameObject;


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

                        Fish fish = gameObject.GetComponent<Fish>();
                        if ( fish != null && fish.m_extraDrops != null && fish.m_extraDrops.m_drops != null )
                            fish.m_extraDrops.m_dropChance += (1.0f - fish.m_extraDrops.m_dropChance) * 0.25f;
                    }

                    ItemDrop itemDrop = gameObject.GetComponent<ItemDrop>();
                    if ( (itemDrop != null) && (itemDrop.m_itemData != null) && (itemDrop.m_itemData.m_shared != null) )
                    {
                        if ( _EnableItemLevels.Value )
                        {
                            if ( itemDrop.m_itemData.m_shared.m_maxQuality > 1 )
                                itemDrop.m_itemData.m_shared.m_maxQuality += 10;
                        }
                        if ( (gameObject.name == "Hammer") && (itemDrop.m_itemData.m_shared.m_buildPieces != null) )
                            BuildPieces = itemDrop.m_itemData.m_shared.m_buildPieces;
                    }

                    Piece piece = gameObject.GetComponent<Piece>();
                    if ( piece != null )
                    {
                        if ( !piece.m_enabled )
                            piece.m_enabled = true;
                        if ( piece.m_canBeRemoved && (piece.m_destroyedLootPrefab == null) )
                        {
                            // Black Marble pieces
                            if ( gameObject.name == "blackmarble_2x2_enforced"
                                || gameObject.name == "piece_dvergr_wood_door"
                                || gameObject.name == "piece_dvergr_pole"
                                || gameObject.name == "stone_floor"
                                || gameObject.name == "blackmarble_slope_1x2"
                                || gameObject.name == "blackmarble_base_2"
                                || gameObject.name == "blackmarble_column_3"
                                || gameObject.name == "blackmarble_floor_large"
                                || gameObject.name.StartsWith("blackmarble_head")
                                || gameObject.name == "blackmarble_out_2"
                                || gameObject.name.StartsWith("blackmarble_stair_")
                                || gameObject.name.StartsWith("blackmarble_tile") )
                                AddToWorkbench.Add(gameObject);
                            if ( gameObject.name == "crystal_wall_1x1"
                                || gameObject.name == "piece_dvergr_wood_door"
                                || gameObject.name == "piece_dvergr_pole")
                                piece.m_category = Piece.PieceCategory.Furniture;
                            if ( gameObject.name == "blackmarble_slope_1x2"
                                || gameObject.name == "blackmarble_base_2"
                                || gameObject.name == "blackmarble_column_3"
                                || gameObject.name == "blackmarble_floor_large"
                                || gameObject.name.StartsWith("blackmarble_head")
                                || gameObject.name == "blackmarble_out_2"
                                || gameObject.name.StartsWith("blackmarble_stair_")
                                || gameObject.name.StartsWith("blackmarble_tile") )
                                piece.m_category = Piece.PieceCategory.Misc;
                        }
//                      ZLog.Log("Disabled piece: " + piece.name);
                    }
                }
                AddMonsterDrops();

                if ( BuildPieces != null )
                {
                    foreach ( GameObject addPiece in AddToWorkbench )
                    {
                        if ( BuildPieces.m_pieces.IndexOf(addPiece) < 0 )
                            BuildPieces.m_pieces.Add(addPiece);
                    }
                }

                foreach( Pickable pickable in PlantPickables )
                {
                    float NewAmount = (float)pickable.m_amount * 1.25f + 0.55f;
                    ZLog.Log("Increasing drop count for " + pickable.name + ": " + pickable.m_itemPrefab.name + " " + pickable.m_amount + " > " + NewAmount);

                    // Increase integer part
                    pickable.m_amount = (int)NewAmount;
                    NewAmount -= (float)pickable.m_amount;

                    // Add chance part
                    if(NewAmount > 0.01f)
                    {
                        if(pickable.m_extraDrops == null)
                            pickable.m_extraDrops = new DropTable();
                        DropTable.DropData NewDrop = new DropTable.DropData();
                        NewDrop.m_item = pickable.m_itemPrefab;
                        NewDrop.m_stackMin = 1;
                        NewDrop.m_stackMax = 1;
                        NewDrop.m_weight = 1.0f;
                        pickable.m_extraDrops.m_drops.Add(NewDrop);
                        pickable.m_extraDrops.m_dropChance = NewAmount;
                    }
                }
            }
        }

        //
        // Detect mountains
        // FIXME!
/*        [HarmonyPatch(typeof(WorldGenerator), "FindMountains")]
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
        }*/

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

        //
        // Fish pickup patch
        //
        [HarmonyPatch(typeof(Fish), "Interact")]
        private class FishPickupPatch
        {
            [HarmonyPostfix]
            private static void InteractPostfix( ref Fish __instance, ref bool __result, Humanoid character)
            {
                if ( !__result && (__instance != null) && (character != null) )
                {
                    if ( __instance.Pickup(character) )
                        __result = true;
                }
            }
        }

        //
        // Fish attract patch
        //
        [HarmonyPatch(typeof(Fish), "RandomizeWaypoint")]
        private class FishAttractPatch
        {
            [HarmonyPostfix]
            private static void RandomizeWaypointPostfix( ref bool __result, ref FishingFloat ___m_waypointFF, ref float ___m_swimTimer)
            {
                if ( __result && (___m_waypointFF != null) )
                    ___m_swimTimer *= 2.0f;
            }
        }

        [HarmonyPatch(typeof(Character), "SetupMaxHealth")]
        private class CharacterPatch_TamedHP
        {
            [HarmonyPostfix]
            private static void SetupMaxHealthPostfix( ref Character __instance, ref bool ___m_tamed, ref ZNetView ___m_nview)
            {
                if ( (__instance != null) && ___m_tamed && (___m_nview != null) )
                {
                    // This creature has been fully tamed
                    if ( ___m_nview.GetZDO().GetFloat("TameTimeLeft", -1.0f) == 0.0f )
                    {
                        float newHealth = __instance.m_health * (float)__instance.GetLevel() * Mathf.Max(1.0f,_OriginalTamedHPScale.Value);
                        __instance.SetMaxHealth(newHealth);
                        __instance.SetHealth(newHealth);
                        ZLog.Log("Tamed creature HP scaled");
                    }
                }
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

        [HarmonyPatch(typeof(EnvMan), "Awake")]
        private class FogPatch
        {
            private static float ScaleFog( float Value)
            {
                if ( Value > 0.01f )
                    return Mathf.Sqrt(Value * 100.0f) / 100.0f;
                return Value;
            }

            [HarmonyPrefix]
            private static void AwakePrefix( ref EnvMan __instance)
            {
                if ( !_EnableFogReduction.Value )
                    return;
                foreach(EnvSetup env in __instance.m_environments)
                {
                    env.m_fogDensityNight   = ScaleFog(env.m_fogDensityNight);
                    env.m_fogDensityMorning = ScaleFog(env.m_fogDensityMorning);
                    env.m_fogDensityDay     = ScaleFog(env.m_fogDensityDay);
                    env.m_fogDensityEvening = ScaleFog(env.m_fogDensityEvening);
                }

            }
        }
    }
}