using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;



namespace LeanNet
{
	[BepInPlugin("CacoFFF.valheim.LeanNet", "Lean Networking", "1.0.0")]
	public class LeanNet : BaseUnityPlugin
	{
		// Config
		public static ConfigEntry<bool> Enabled;
		public static ConfigEntry<float> NetRatePhysics;
		public static ConfigEntry<float> NetRateNPC;
		public static ConfigEntry<float> Vec3CullSize;

		private Harmony _harmony;

		// Global State
		public static double NetTime = 0.0f;
		public static float DeltaTimeFixedPhysics = 0.02f;
		public static float DeltaTimePhysics = 0.01f;
		public static float Vec3CullSizeSq = 0.00025f;

		private void Awake()
		{
			Enabled = Config.Bind("Global", "Enabled", true, "Global toggle, this option does not require a game restart.");
			NetRatePhysics = Config.Bind("Global", "NetRatePhysics", 8.0f, "Update frequency for physics objects, such as dropped items and projectiles.");
			NetRateNPC = Config.Bind("Global", "NetRateNPC", 8.0f, "Update frequency for NPC's.");
			Vec3CullSize = Config.Bind("Global", "Vec3CullSize", 0.05f, "Cull Vector3 updates if the magnitude of the offset is smaller than this.");

			_harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
		}


		public static void UpdateState( float DeltaTime )
		{
			// Keep config values sane to prevent ruining other player's experience
			if ( NetRatePhysics.Value < 4.0f )
				NetRatePhysics.Value = 4.0f;
			if ( NetRateNPC.Value < 4.0f )
				NetRateNPC.Value = 4.0f;
			if ( Vec3CullSize.Value > 0.2f )
				Vec3CullSize.Value = 0.2f;

			Vec3CullSizeSq = Vec3CullSize.Value * Vec3CullSize.Value;

			// Update internal accumulator
			NetTime += DeltaTime;
			if ( DeltaTime > 100.0f )
				NetTime -= DeltaTime;

			// Reset temporary state
			ZDORevisionFreeze.Freeze = 0;
			ZDORevisionFreeze.Force = 0;
		}

		//
		// Calculates a somewhat unique, incremental time for a ZDO
		// This value will always be less than 200
		//
		public static double GetZDOTime( ZDO zDO )
		{
			return NetTime + 0.023 * (double)(zDO.m_uid.ID & 4095);
		}

		public static bool ShouldUpdateZDO( ZDO zDO, float NetRate, float DeltaTime )
		{
			double TimeBase = GetZDOTime(zDO);
			double TimeNext = TimeBase + DeltaTime;
			return Mathf.RoundToInt((float)(TimeBase*NetRate)) != Mathf.RoundToInt((float)(TimeNext*NetRate));
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
				DeltaTimeFixedPhysics = T;

				ZDORevisionFreeze.Reset();
				UpdateState(0.0f);
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
				UpdateState(T);
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
				if ( Enabled.Value == false)
					return true;

				// If this ZDO is being updated, allow setting the value regardless.
				if ( ZDORevisionFreeze.IsForcing() )
					return true;

				// Do not update Vectors3 if changes are below the size threshold
				Vector3 CurValue;
				if ( __instance.GetVec3(hash, out CurValue) )
				{
					Vector3 Offset = CurValue - value;
					if ( Offset.sqrMagnitude < Vec3CullSizeSq )
						return false;
				}

				return true;
			}

		}
		[HarmonyPatch(typeof(ZDO), "Set", new System.Type[] { typeof(int), typeof(Quaternion) })]
		public class ZDOUpdateDiscard_Quat
		{
			private static bool Prefix( ref ZDO __instance, int hash, Quaternion value )
			{
				if ( Enabled.Value == false )
					return true;

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
		[HarmonyPatch(typeof(ZSyncTransform), "CustomLateUpdate")]
		public class PhysicsSyncReducer
		{
			static bool Freezing = false;
			static bool Forcing = false;

			private static void Prefix( ref ZSyncTransform __instance, ref ZNetView ___m_nview )
			{
				if ( Enabled.Value == false )
					return;

				float Value;
				ZDO zDO = ___m_nview.GetZDO();

				// Do not limit rate on ships, it degrades experience too much
				if ( zDO.GetFloat(ZDOVars.s_rudder, out Value) )
					return;

				// Log util
				// ZDOUpdateLogger.ZDOPreUpdate(zDO);

				float UpdateRate = NetRatePhysics.Value;

				// If we can't update position, increase cadence of velocity updates
				if ( !__instance.m_syncPosition )
					UpdateRate *= 2.0f;

				// Force an update every 2 seconds
				// Otherwise run updates as specified via config.
				Forcing = ShouldUpdateZDO(zDO, 0.5f, DeltaTimePhysics);
				Freezing = !Forcing && !ShouldUpdateZDO(zDO, UpdateRate, DeltaTimePhysics);

				if ( Forcing ) 
					ZDORevisionFreeze.Force++;
				if ( Freezing )
					ZDORevisionFreeze.Freeze++;
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
				if ( Enabled.Value == false )
					return;

				// ZDOUpdateLogger.ZDOPreUpdate(___m_nview.GetZDO());

				if ( !__instance.IsPlayer() )
				{
					// Force an update every 2 seconds
					// Otherwise run updates as specified via config.
					ZDO zDO = ___m_nview.GetZDO();
					Forcing = ShouldUpdateZDO(zDO, 0.5f, DeltaTimePhysics);
					Freezing = !Forcing && !ShouldUpdateZDO(zDO, NetRateNPC.Value, DeltaTimePhysics);

					if ( Forcing )
						ZDORevisionFreeze.Force++;
					if ( Freezing )
						ZDORevisionFreeze.Freeze++;
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
				if ( Enabled.Value == false )
					return;

				// Do not force a ZDO update for ground tilt changes
				ZDORevisionFreeze.Freeze++;
			}

			[HarmonyPostfix]
			[HarmonyPatch("UpdateGroundTilt")]
			private static void UpdateGroundTiltPostfix( ref Character __instance )
			{
				if ( Enabled.Value == false )
					return;

				ZDORevisionFreeze.Freeze--;
			}

			[HarmonyPrefix]
			[HarmonyPatch("SyncVelocity")]
			private static bool SyncVelocityPrefix( ref Rigidbody ___m_body, ref Vector3 ___m_bodyVelocityCached )
			{
				if ( Enabled.Value == false )
					return true;

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
