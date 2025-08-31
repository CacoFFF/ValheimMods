using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;



namespace LeanNet
{
	[BepInPlugin("CacoFFF.valheim.LeanNet", "Lean Networking", "0.0.1")]
	public class LeanNet : BaseUnityPlugin
	{
		// Config
		public static ConfigEntry<float> NetRatePhysics;
		public static ConfigEntry<float> NetRateNPC;
		public static ConfigEntry<float> Vec3CullSize;

		private Harmony _harmony;

		// Global State
		public static float DeltaTimePhysics = 0.01f;
		public static float Vec3CullSizeSq = 0.00025f;

		private void Awake()
		{
			NetRatePhysics = Config.Bind("Global", "NetRatePhysics", 4.0f, "Update frequency for physics objects, such as dropped items and projectiles.");
			NetRateNPC = Config.Bind("Global", "NetRateNPC", 8.0f, "Update frequency for NPC's.");
			Vec3CullSize = Config.Bind("Global", "Vec3CullSize", 0.05f, "Cull Vector3 updates if the magnitude of the offset is smaller than this.");

			_harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
		}


		public static void UpdateState()
		{
			// Keep config values sane
			if ( NetRatePhysics.Value < 2.0f )
				NetRatePhysics.Value = 2.0f;
			if ( NetRateNPC.Value < 2.0f )
				NetRateNPC.Value = 2.0f;
			if ( Vec3CullSize.Value > 0.2f )
				Vec3CullSize.Value = 0.2f;

			Vec3CullSizeSq = Vec3CullSize.Value * Vec3CullSize.Value;
		}


		//
		// ZDO update logging util, for development use.
		//
		public class ZDOUpdateLogger
		{
			private static uint DataRevision = 0;

			public static void ZDOPreUpdate( ZDO zDO )
			{
				if ( zDO != null )
					DataRevision = zDO.DataRevision;
			}

			public static void ZDOPostUpdate( ZDO zDO, string ObjectName )
			{
				if ( (zDO != null) && zDO.IsOwner() && (zDO.DataRevision != DataRevision) )
					ZLog.Log("Net update: " + ObjectName);
			}
		}

		//
		// Get the current DeltaTime
		//
		[HarmonyPatch(typeof(MonoUpdaters), "FixedUpdate")]
		public class GetWorldDeltaTimeFixed
		{
			private static void Prefix()
			{
				float T = UnityEngine.Time.fixedDeltaTime;
				DeltaTimePhysics = T;

				ZDORevisionFreeze.Reset();
				UpdateState();
			}
		}
		[HarmonyPatch(typeof(MonoUpdaters), "LateUpdate")]
		public class GetWorldDeltaTimeLate
		{
			private static void Prefix()
			{
				float T = UnityEngine.Time.deltaTime;
				DeltaTimePhysics = T;

				ZDORevisionFreeze.Reset();
				UpdateState();
			}
		}

		//
		// Controls whether a change to the ZDO allows queuing it for update.
		//
		[HarmonyPatch(typeof(ZDO), "IncreaseDataRevision")]
		public class ZDORevisionFreeze
		{
			public static int Freeze = 0;
			public static int Force = 0;
			public static uint DataRevision = 0;

			public static void Reset()
			{
				Freeze = 0;
				Force = 0;
				DataRevision = 0;
			}

			public static bool IsFreezing() { return Freeze > 0 && Force <= 0; }
			public static bool IsForcing() { return Force > 0; }

			private static bool Prefix()
			{
				return !IsFreezing();
			}
		}


		//
		// Culls any unwanted updates to the ZDO
		//
		[HarmonyPatch(typeof(ZDO), "Set", new System.Type[] { typeof(int), typeof(Vector3) })]
		public class ZDOUpdateDiscard_Vec3
		{
			private static bool Prefix( ref ZDO __instance, int hash, Vector3 value )
			{
				// If this ZDO is being updated, allow setting the value regardless.
				if ( ZDORevisionFreeze.IsForcing() )
					return true;

				// Do not update Vectors3 if changes are below the size threshold
				Vector3 Offset = __instance.GetVec3(hash, value) - value;
				if ( Offset.sqrMagnitude < Vec3CullSizeSq )
					return false;

				return true;
			}

		}
		[HarmonyPatch(typeof(ZDO), "Set", new System.Type[] { typeof(int), typeof(Quaternion) })]
		public class ZDOUpdateDiscard_Quat
		{
			private static bool Prefix( ref ZDO __instance, int hash, Quaternion value )
			{
				// If this ZDO is being updated, allow setting the value regardless.
				if ( ZDORevisionFreeze.IsForcing() )
					return true;

				// Do not update rotations if similar enough
				float Dot = Quaternion.Dot(__instance.GetQuaternion(hash, value), value);
				if ( Dot > 0.98f )
					return false;

				return true;
			}

		}


		//
		// Physics objects sync
		//
		[HarmonyPatch(typeof(ZSyncTransform), "OwnerSync")]
		public class PhysicsSyncReducer
		{
			static bool Freezing = false;
			static bool Forcing = false;

			private static void Prefix( ref ZSyncTransform __instance, ref ZNetView ___m_nview )
			{
				// Log util
				// ZDOUpdateLogger.ZDOPreUpdate(___m_nview.GetZDO());

				float UpdateRate = NetRatePhysics.Value;

				// Generally characters have this, double rate (for now)
				if ( __instance.m_syncRotation ) 
					UpdateRate *= 2.0f;

				float RandomValue = UnityEngine.Random.value;

				// Force an update every 2 seconds
				// Otherwise run updates as specified via config.
				if ( RandomValue < DeltaTimePhysics * 0.5f ) 
				{
					Forcing = true;
					ZDORevisionFreeze.Force++;
				}
				else if ( RandomValue > DeltaTimePhysics * UpdateRate )
				{
					Freezing = true;
					ZDORevisionFreeze.Freeze++;
				}
			}

			private static void Postfix( ref ZSyncTransform __instance, ref ZNetView ___m_nview )
			{
				// Log util
				// ZDOUpdateLogger.ZDOPostUpdate(___m_nview.GetZDO(), __instance.gameObject.name);

				if ( Freezing )
				{
					Freezing = false;
					ZDORevisionFreeze.Freeze--;
				}
				if ( Forcing )
				{
					Forcing = false;
					ZDORevisionFreeze.Force--;
				}
			}

		}


		//
		// Animator sync
		//
/*		[HarmonyPatch(typeof(ZSyncAnimation), "SyncParameters")]
		public class AnimSyncReducer
		{
			private static void Prefix( ref ZSyncTransform __instance, ref ZNetView ___m_nview )
			{
				// Log util
				ZDOUpdateLogger.ZDOPreUpdate(___m_nview.GetZDO());
			}

			private static void Postfix( ref ZSyncTransform __instance, ref ZNetView ___m_nview )
			{
				// Log util
				ZDOUpdateLogger.ZDOPostUpdate(___m_nview.GetZDO(), __instance.gameObject.name);
			}

		}*/


		//
		// Character object sync
		//
		[HarmonyPatch]
		[HarmonyPatch(typeof(Character), "CustomFixedUpdate")]
		public class CharacterNetOptimizator
		{
			public static bool Freezing = false;
			public static bool Forcing = false;

			[HarmonyPrefix]
			private static void CustomFixedUpdatePrefix( ref Character __instance, float dt, ref ZNetView ___m_nview )
			{
				// ZDOUpdateLogger.ZDOPreUpdate(___m_nview.GetZDO());

				if ( !__instance.IsPlayer() )
				{
					float RandomValue = UnityEngine.Random.value;

					// Force an update every 2 seconds
					// Otherwise run updates as specified via config.
					if ( RandomValue < DeltaTimePhysics * 0.5f )
					{
						Forcing = true;
						ZDORevisionFreeze.Force++;
					}
					else if ( RandomValue > DeltaTimePhysics * NetRateNPC.Value )
					{
						Freezing = true;
						ZDORevisionFreeze.Freeze++;
					}
				}
			}

			[HarmonyPostfix]
			private static void CustomFixedUpdatePostfix( ref Character __instance, ref ZNetView ___m_nview )
			{
				// ZDOUpdateLogger.ZDOPostUpdate(___m_nview.GetZDO(), __instance.gameObject.name);

				if ( Freezing )
				{
					Freezing = false;
					ZDORevisionFreeze.Freeze--;
				}
				if ( Forcing )
				{
					Forcing = false;
					ZDORevisionFreeze.Force--;
				}
			}

			[HarmonyPrefix]
			[HarmonyPatch("UpdateGroundTilt")]
			private static void UpdateGroundTiltPrefix( float dt )
			{
				ZDORevisionFreeze.Freeze++;
			}

			[HarmonyPostfix]
			[HarmonyPatch("UpdateGroundTilt")]
			private static void UpdateGroundTiltPostfix( ref Character __instance )
			{
				ZDORevisionFreeze.Freeze--;
			}

			[HarmonyPrefix]
			[HarmonyPatch("SyncVelocity")]
			private static bool SyncVelocityPrefix( ref Rigidbody ___m_body, ref Vector3 ___m_bodyVelocityCached )
			{
				// If this ZDO is being updated, allow setting the value regardless.
				if ( ZDORevisionFreeze.IsForcing() )
					return true;

				// Do not update velocity if change is small
				Vector3 deltaVelocity = ___m_body.velocity - ___m_bodyVelocityCached;
				if ( deltaVelocity.sqrMagnitude <= Vec3CullSizeSq )
					return false;

				return true;
			}
		}
	}
}
