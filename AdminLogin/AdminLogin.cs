using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace AdminLogin
{
    [BepInPlugin(Package, ModName, Version)]
    public class AdminLogin : BaseUnityPlugin
    {
        public const string Package = "CacoFFF.valheim.AdminLogin";
        public const string Version = "0.0.1";
        public const string ModName = "Admin Login";

        private static List<string> _AdminList;

        private static ConfigEntry<string> _AdminPassword;

        private Harmony _harmony;

        private void Awake()
        {
            _AdminList = new List<string>();

            _AdminPassword = Config.Bind("Global", "AdminPassword", "", "Password required to grant admin rights.");

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        public static bool IsAdmin( string ID)        { return _AdminList.Contains(ID); }
        private static void AddAdmin( string ID)      { if ( !IsAdmin(ID) ) _AdminList.Add(ID); }
        private static void RemoveAdmin( string ID)   { _AdminList.Remove(ID); }


        // Aliases
        public static bool IsAdmin( ZNetPeer Peer)    { if ( Peer != null ) return IsAdmin(Peer.m_socket?.GetHostName()); return false; }


        public static ZNetPeer GetLocalPeer()
        {
            if ( ZNet.instance && ZNet.m_isServer && Player.m_localPlayer && (ZNet.instance.GetPeers().Count > 0) )
                return ZNet.instance.GetPeers()[0];
            return null;
        }

        public static string GetPlayerID( ZNetPeer Peer)
        {
            if ( Peer == null )
                Peer = GetLocalPeer();
            if ( Peer != null && (Peer.m_socket != null) )
                return Peer.m_socket.GetHostName();
            return "";
        }

        private static void InternalAdminLogin( ZNetPeer Peer, string AdminPassword)
        {
            string ID = GetPlayerID(Peer);
            ZLog.Log("Compare " + AdminPassword + " vs " + _AdminPassword.Value);
            if ( AdminPassword.Equals(_AdminPassword.Value) )
            {
                AddAdmin(ID);
                if ( Peer == null )
                    Peer = GetLocalPeer();
                string Message = Peer != null ? Peer.m_playerName : "Local Player" + " has logged in as administrator.";
                // TODO: Broadcast
                ZLog.Log(Message + " (" + ID + ")");
            }
        }

        private static void InternalAdminLogout( ZNetPeer Peer)
        {
            string ID = GetPlayerID(Peer);
            if( IsAdmin(ID) )
            {
                RemoveAdmin(ID);
                if ( Peer == null )
                    Peer = GetLocalPeer();
                string Message = Peer != null ? Peer.m_playerName : "Local Player" + " has given up administrator rights.";
                // TODO: Broadcast
                ZLog.Log(Message + " (" + ID + ")");
            }
        }


        public static void RPC_AdminLogin( ZRpc rpc, string AdminPassword)
        {
            ZLog.Log("AdminLogin received: " + AdminPassword);
            ZNetPeer Peer = ZNet.instance?.GetPeer(rpc);
            if ( Peer != null )
                InternalAdminLogin( Peer, AdminPassword);
        }

        public static void RPC_AdminLogout( ZRpc rpc)
        {
            ZLog.Log("AdminLogout received");
            ZNetPeer Peer = ZNet.instance?.GetPeer(rpc);
            if ( Peer != null )
                InternalAdminLogout( Peer);
        }


        [HarmonyPatch(typeof(Console))]
        private class ConsolePatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(Console.InputText))]
            private static void InputTextPostfix(ref Console __instance)
            {
                if ( __instance == null || ZNet.instance == null )
                    return;

                string text = __instance.m_input.text;
                ZLog.Log(string.Format("Console command: {0}", __instance.m_input.text));

                if ( ZNet.instance == null )
                    return;

                string[] array = text.Split(' ');
                if ( array[0] == "adminlogin" )
                {
                    if ( ZNet.instance.IsServer() )
                        InternalAdminLogin( null, text.Substring(11));
                    else
                        ZNet.instance.GetServerRPC()?.Invoke("adminlogin", text.Substring(11));
                }
                else if ( array[0] == "adminlogout" )
                {
                    if ( ZNet.instance.IsServer() )
                        InternalAdminLogout(null);
                    else
                        ZNet.instance.GetServerRPC()?.Invoke("adminlogout");
                }
            }
        }

        [HarmonyPatch(typeof(ZNet))]
        private class ZNetPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(ZNet.RPC_PeerInfo))]
            private static void RPC_PeerInfoPostfix(ref ZNet __instance, ZRpc rpc)
            {
                if ( __instance == null )
                    return;

                ZNetPeer peer = __instance.GetPeer(rpc);
                if ( peer == null || !peer.IsReady() )
                    return;

                if ( ZNet.m_isServer )
                {
                    rpc.Register<string>("adminlogin", RPC_AdminLogin);
                    rpc.Register("adminlogout", RPC_AdminLogout);
                }
            }
        }

    }
}