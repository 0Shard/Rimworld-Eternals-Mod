// Relative Path: Eternal/Source/Eternal/Compatibility/SpaceModDetection.cs
// Creation Date: 12-11-2025
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Thin facade for space-mod compatibility detection. Delegates all SOS2 reflection
//              lookups to SOS2ReflectionCache (managed by EternalServiceContainer) instead of
//              holding any local reflection state.

using System;
using System.Linq;
using Verse;
using RimWorld.Planet;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Detects space-related mods and provides compatibility utilities.
    /// Supports the active Save Our Ship 2 and Vanilla Gravship Expanded patch gates.
    ///
    /// <para>SOS2 reflection lookups are delegated to <see cref="SOS2ReflectionCache"/> via
    /// <see cref="EternalServiceContainer.SpaceModCache"/>. This class holds no reflection state.</para>
    /// </summary>
    public static class SpaceModDetection
    {
        // Mod package IDs

        private static readonly string[] SOS2_VARIANTS = new[]
        {
            "kentington.saveourship2",
            "SaveOurShip2",
            "sos2"
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
        /// Checks if a MapParent is an SOS2 WorldObjectOrbitingShip.
        /// Delegates to <see cref="SOS2ReflectionCache.IsOrbitingShip(MapParent)"/>.
        /// </summary>
        public static bool IsOrbitingShip(MapParent mapParent)
        {
            if (mapParent == null)
            {
                return false;
            }

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
            {
                return false;
            }

            var cache = EternalServiceContainer.Instance.SpaceModCache;
            if (cache == null)
            {
                return false;
            }

            return cache.IsShipBurningUp(orbitingShip).IsTrue;
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

    }
}
