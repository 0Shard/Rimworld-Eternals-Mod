// Relative Path: Eternal/Source/Eternal/Compatibility/SpaceModDetection.cs
// Creation Date: 12-11-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Thin facade for space-mod compatibility detection. Delegates all SOS2 reflection
//              lookups to SOS2ReflectionCache (managed by EternalServiceContainer) instead of
//              holding any local reflection state.
//              Phase 4: Removed SOS2 Type Cache region; replaced with delegation to SpaceModCache.
//              IsShipDestroyed() now returns real results via ShipMapComp.ShipMapState == burnUpSet
//              instead of the previous hardcoded false stub.
//              Retains full public API surface — all existing call sites compile unchanged.

using System;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld.Planet;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Detects space-related mods and provides compatibility utilities.
    /// Supports Save Our Ship 2, Odyssey, and Vanilla Gravship Expanded.
    ///
    /// <para>SOS2 reflection lookups are delegated to <see cref="SOS2ReflectionCache"/> via
    /// <see cref="EternalServiceContainer.SpaceModCache"/>. This class holds no reflection state.</para>
    /// </summary>
    public static class SpaceModDetection
    {
        // Mod package IDs
        private const string SOS2_PACKAGE_ID = "kentington.saveourship2";
        private const string ODYSSEY_PACKAGE_ID = "Odyssey";

        private static readonly string[] SOS2_VARIANTS = new[]
        {
            "kentington.saveourship2",
            "SaveOurShip2",
            "sos2"
        };

        private static readonly string[] ODYSSEY_VARIANTS = new[]
        {
            "Odyssey",
            "VanillaExpanded.VFESpace",
            "VFE.Space"
        };

        private static readonly string[] VGE_VARIANTS = new[]
        {
            "OskarPotocki.VanillaGravshipExpanded",
            "VanillaExpanded.Gravship",
            "vanillaexpanded.gravship",
            "VGE"
        };

        // ----------------------------------------------------------------
        // SOS2 detection — delegated to SOS2ReflectionCache
        // ----------------------------------------------------------------

        /// <summary>
        /// Gets the WorldObjectOrbitingShip type from SOS2, or null if not available.
        /// Delegates to <see cref="SOS2ReflectionCache.WorldObjectOrbitingShipType"/>.
        /// </summary>
        public static Type WorldObjectOrbitingShipType =>
            EternalServiceContainer.Instance.SpaceModCache?.WorldObjectOrbitingShipType;

        /// <summary>
        /// Checks if a MapParent is an SOS2 WorldObjectOrbitingShip.
        /// Delegates to <see cref="SOS2ReflectionCache.IsOrbitingShip(MapParent)"/>.
        /// </summary>
        public static bool IsOrbitingShip(MapParent mapParent)
        {
            if (mapParent == null)
                return false;

            return EternalServiceContainer.Instance.SpaceModCache?.IsOrbitingShip(mapParent) ?? false;
        }

        /// <summary>
        /// Checks if an SOS2 orbiting ship is in "burn up" state (about to kill all pawns).
        /// Returns false on <c>ReflectionFailed</c> — conservative fallback per locked decision.
        /// Delegates to <see cref="SOS2ReflectionCache.IsShipBurningUp(MapParent)"/>.
        /// </summary>
        public static bool IsShipBurningUp(MapParent orbitingShip)
        {
            if (orbitingShip == null)
                return false;

            var cache = EternalServiceContainer.Instance.SpaceModCache;
            if (cache == null)
                return false;

            return cache.IsShipBurningUp(orbitingShip).IsTrue;
        }

        /// <summary>
        /// Checks if a ship map has been destroyed (all destruction paths in SOS2 set
        /// <c>ShipMapComp.ShipMapState == burnUpSet</c>).
        ///
        /// <para>Replaces the previous stub that unconditionally returned false. Now delegates to
        /// <see cref="SOS2ReflectionCache.IsShipDestroyed(Verse.Map)"/>.</para>
        /// </summary>
        public static bool IsShipDestroyed(Verse.Map map)
        {
            if (map == null)
                return false;

            var cache = EternalServiceContainer.Instance.SpaceModCache;
            if (cache == null)
                return false;

            return cache.IsShipDestroyed(map).IsTrue;
        }

        // ----------------------------------------------------------------
        // Mod active checks
        // ----------------------------------------------------------------

        /// <summary>
        /// Checks if Save Our Ship 2 mod is active.
        /// </summary>
        public static bool SaveOurShip2Active
        {
            get
            {
                try
                {
                    return SOS2_VARIANTS.Any(id => ModsConfig.IsActive(id));
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "SaveOurShip2Active", null, ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if Odyssey (or Vanilla Expanded Space) mod is active.
        /// </summary>
        public static bool OdysseyActive
        {
            get
            {
                try
                {
                    return ODYSSEY_VARIANTS.Any(id => ModsConfig.IsActive(id));
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "OdysseyActive", null, ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if Vanilla Gravship Expanded mod is active.
        /// VGE is a comprehensive gravship overhaul that requires Odyssey DLC.
        /// </summary>
        public static bool VanillaGravshipExpandedActive
        {
            get
            {
                try
                {
                    return VGE_VARIANTS.Any(id => ModsConfig.IsActive(id));
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "VanillaGravshipExpandedActive", null, ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if any space-related mods are active.
        /// </summary>
        public static bool AnySpaceModActive => SaveOurShip2Active || OdysseyActive || VanillaGravshipExpandedActive;

        // ----------------------------------------------------------------
        // Map utilities
        // ----------------------------------------------------------------

        /// <summary>
        /// Determines if a map is a space map based on its MapParent def name.
        /// </summary>
        public static bool IsSpaceMap(Verse.Map map)
        {
            try
            {
                if (map == null)
                    return false;

                if (!AnySpaceModActive)
                    return false;

                MapParent parent = map.Parent;
                if (parent?.def == null)
                    return false;

                string defName = parent.def.defName?.ToLower() ?? "";

                bool isSpaceRelated = defName.Contains("ship") ||
                                     defName.Contains("space") ||
                                     defName.Contains("orbit") ||
                                     defName.Contains("shuttle") ||
                                     defName.Contains("vessel") ||
                                     defName.Contains("spacecraft");

                if (isSpaceRelated && Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Detected space map: {map} (parent def: {parent.def.defName})");
                }

                return isSpaceRelated;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "IsSpaceMap", null, ex);
                return false;
            }
        }

        /// <summary>
        /// Checks if a player home base is available for emergency teleportation.
        /// </summary>
        public static bool IsPlayerBaseAvailable()
        {
            try
            {
                Verse.Map homeMap = Find.AnyPlayerHomeMap;
                return homeMap != null;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "IsPlayerBaseAvailable", null, ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the primary player home map for emergency teleportation.
        /// </summary>
        public static Verse.Map GetPlayerHomeMap()
        {
            try
            {
                return Find.AnyPlayerHomeMap;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetPlayerHomeMap", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets all player-controlled maps (home maps).
        /// </summary>
        public static System.Collections.Generic.List<Verse.Map> GetAllPlayerMaps()
        {
            try
            {
                return Find.Maps.Where(m => m.IsPlayerHome).ToList();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetAllPlayerMaps", null, ex);
                return new System.Collections.Generic.List<Verse.Map>();
            }
        }

        // ----------------------------------------------------------------
        // Debug logging
        // ----------------------------------------------------------------

        /// <summary>
        /// Logs information about detected space mods (for debugging).
        /// </summary>
        public static void LogSpaceModStatus()
        {
            if (Eternal_Mod.settings?.debugMode != true)
                return;

            Log.Message($"[Eternal] Space Mod Detection Status:");
            Log.Message($"  - Save Our Ship 2: {(SaveOurShip2Active ? "Active" : "Not Found")}");
            Log.Message($"  - Odyssey/VFE Space: {(OdysseyActive ? "Active" : "Not Found")}");
            Log.Message($"  - Vanilla Gravship Expanded: {(VanillaGravshipExpandedActive ? "Active" : "Not Found")}");
            Log.Message($"  - Player Base Available: {(IsPlayerBaseAvailable() ? "Yes" : "No")}");
        }
    }
}
