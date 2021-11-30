using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace GlobalWardControl
{
    [BepInPlugin(Package, ModName, Version)]
    public class GlobalWardControl : BaseUnityPlugin
    {
        public const string Package = "CacoFFF.valheim.GlobalWardControl";
        public const string Version = "0.0.1";
        public const string ModName = "Global Ward Control";

        private static ConfigEntry<bool> _DisableWardsOnBoot;

        private Harmony _harmony;

        private void Awake()
        {
            _DisableWardsOnBoot = Config.Bind("Global", "DisableWardsOnBoot", true, "Disable all wards in the world upon server boot.");

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private void OnDestroy()
        {
            if ( _harmony != null )
                _harmony.UnpatchSelf();
        }

        // ZDO values to consider...
        // m_type
        // m_prefab
        //m_nview.GetZDO().Set("enabled", enabled); //enabled is 'bool'

        // ZDO.Load postfix could be used to change 'enabled' on ZDO's where m_prefab is a ward
        // ZDOMan is the global ZDO manager!!
        // ZDOMan.Load is the loader


        [HarmonyPatch(typeof(ZDO))]
        private class ZDOPatch
        {
            private static readonly int _enabledHashcode = "enabled".GetStableHashCode();
            private static readonly int _guardstone_Hashcode = "guard_stone".GetStableHashCode();

            [HarmonyPostfix]
            [HarmonyPatch(nameof(ZDO.Load))]
            private static void LoadPostfix(ref ZDO __instance)
            {
                if (!_DisableWardsOnBoot.Value || (__instance == null) )
                    return;

                if ( (__instance.m_prefab == _guardstone_Hashcode) && __instance.GetBool(_enabledHashcode) )
                {
                    __instance.Set(_enabledHashcode, false);
                    ZLog.Log(string.Format("Disabling ward {0}", __instance.m_uid.ToString() ));
                }
            }
        }
    }
}
