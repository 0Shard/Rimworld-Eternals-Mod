// Relative Path: Eternal/Source/Eternal/Compatibility/SOS2ReflectionCache.cs
// Creation Date: 21-02-2026
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Extracted, testable cache for SOS2 (Save Our Ship 2) reflection lookups.
//              Accepts an injected Func<string, Type> resolver so the class can be instantiated
//              in unit tests without loading RimWorld assemblies.
//              Managed by EternalServiceContainer; initialized eagerly in FinalizeInit() for fail-fast
//              behaviour if the SOS2 API surface changes between mod versions.
//              Fixes the pre-existing bug where ShipMapState was (incorrectly) looked up on
//              WorldObjectOrbitingShip — the field lives on ShipMapComp (a MapComponent).

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld.Planet;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Caches SOS2 reflection data needed for ship state detection.
    ///
    /// <para>Lifecycle:</para>
    /// <list type="number">
    ///   <item><see cref="SOS2ReflectionCache()"/> — wired up in <c>EternalServiceContainer.Initialize()</c></item>
    ///   <item><see cref="Initialize(bool)"/> — called from <c>Eternal_Component.FinalizeInit()</c> once game world is ready</item>
    ///   <item><c>EternalServiceContainer.Reset()</c> sets the field to null; FinalizeInit on next session creates a new instance</item>
    /// </list>
    ///
    /// <para>Bug fixed: the original <c>EnsureSOS2TypesInitialized()</c> accessed
    /// <c>AccessTools.Field(_worldObjectOrbitingShipType, "ShipMapState")</c> — that field does NOT
    /// exist on <c>WorldObjectOrbitingShip</c>. It is a public field on <c>ShipMapComp</c>.
    /// This caused <c>IsShipBurningUp()</c> to always return false.</para>
    /// </summary>
    public class SOS2ReflectionCache
    {
        private readonly Func<string, Type> _typeResolver;

        // Cached type handles
        private Type _worldObjectOrbitingShipType;
        private Type _shipMapCompType;
        private Type _shipMapStateType;

        // Field on ShipMapComp — NOT on WorldObjectOrbitingShip (see class summary)
        private FieldInfo _shipMapStateField;

        // Parsed burnUpSet enum value for fast Equals() comparison at runtime
        private object _burnUpSetValue;

        private bool _initialized;
        private bool _sos2Present;

        // ----------------------------------------------------------------
        // Constructors
        // ----------------------------------------------------------------

        /// <summary>
        /// Production constructor. Uses <c>AccessTools.TypeByName</c> as the type resolver.
        /// </summary>
        public SOS2ReflectionCache() : this(AccessTools.TypeByName) { }

        /// <summary>
        /// Test constructor. The supplied <paramref name="typeResolver"/> is called in place of
        /// <c>AccessTools.TypeByName</c>, allowing callers to provide stub types without loading
        /// RimWorld assemblies.
        /// </summary>
        /// <param name="typeResolver">Must not be null.</param>
        public SOS2ReflectionCache(Func<string, Type> typeResolver)
        {
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        // ----------------------------------------------------------------
        // Public properties
        // ----------------------------------------------------------------

        /// <summary>True after <see cref="Initialize(bool)"/> has been called.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>True when SOS2 is active (set by <see cref="Initialize(bool)"/>).</summary>
        public bool SOS2Present => _sos2Present;

        /// <summary>
        /// The resolved <c>WorldObjectOrbitingShip</c> type, or null if not available.
        /// Used by <see cref="IsOrbitingShip(MapParent)"/> and by SOS2 patches.
        /// </summary>
        public Type WorldObjectOrbitingShipType => _worldObjectOrbitingShipType;

        // ----------------------------------------------------------------
        // Initialization
        // ----------------------------------------------------------------

        /// <summary>
        /// Eagerly resolves all SOS2 types and caches the <c>ShipMapState</c> field.
        /// Call once from <c>Eternal_Component.FinalizeInit()</c> after the game world is loaded.
        ///
        /// <para>If SOS2 is present but types cannot be resolved, a one-time warning is logged.
        /// All detection methods degrade gracefully by returning <c>ReflectionFailed()</c>.</para>
        /// </summary>
        /// <param name="sos2ModActive">Pass <c>SpaceModDetection.SaveOurShip2Active</c>.</param>
        public void Initialize(bool sos2ModActive)
        {
            _initialized = true;
            _sos2Present = sos2ModActive;

            if (!sos2ModActive)
                return;

            try
            {
                _worldObjectOrbitingShipType = _typeResolver("SaveOurShip2.WorldObjectOrbitingShip");
                _shipMapCompType             = _typeResolver("SaveOurShip2.ShipMapComp");
                _shipMapStateType            = _typeResolver("SaveOurShip2.ShipMapState");

                if (_shipMapCompType == null || _shipMapStateType == null)
                {
                    Log.Warning("[Eternal] SOS2 detected but ShipMapComp/ShipMapState types not found — " +
                                "reflection cache incomplete. IsShipBurningUp/IsShipDestroyed will return false.");
                    return;
                }

                // CRITICAL BUG FIX: ShipMapState field lives on ShipMapComp, not WorldObjectOrbitingShip.
                // The original code used _worldObjectOrbitingShipType here, which always returned null.
                _shipMapStateField = AccessTools.Field(_shipMapCompType, "ShipMapState");

                if (_shipMapStateType.IsEnum)
                {
                    var burnUpName = Enum.GetNames(_shipMapStateType)
                        .FirstOrDefault(n => n.Equals("burnUpSet", StringComparison.OrdinalIgnoreCase));

                    if (burnUpName != null)
                        _burnUpSetValue = Enum.Parse(_shipMapStateType, burnUpName);
                }

                if (_shipMapStateField == null || _burnUpSetValue == null)
                {
                    Log.Warning("[Eternal] SOS2 reflection cache incomplete — ShipMapState field or burnUpSet " +
                                "value could not be resolved. IsShipBurningUp/IsShipDestroyed will return false.");
                }
                else if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] SOS2ReflectionCache initialized: " +
                        $"OrbitingShip={_worldObjectOrbitingShipType != null}, " +
                        $"ShipMapComp={_shipMapCompType != null}, " +
                        $"ShipMapStateField={_shipMapStateField != null}, " +
                        $"BurnUpValue={_burnUpSetValue != null}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SOS2ReflectionCache.Initialize", null, ex);
            }
        }

        // ----------------------------------------------------------------
        // Detection methods
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns a tri-state result for whether the given orbiting ship (a <c>MapParent</c>) is
        /// in the <c>burnUpSet</c> state — i.e., about to be destroyed and all pawns killed.
        ///
        /// <list type="bullet">
        ///   <item><c>ModNotPresent</c> — SOS2 not loaded; safe to ignore.</item>
        ///   <item><c>ReflectionFailed</c> — SOS2 loaded but types unresolvable; caller should return false (conservative).</item>
        ///   <item><c>Success</c> — state was read; check <see cref="BoolLookupResult.Value"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="orbitingShip">The <c>WorldObjectOrbitingShip</c> MapParent to inspect.</param>
        public BoolLookupResult IsShipBurningUp(MapParent orbitingShip)
        {
            if (!_sos2Present)
                return BoolLookupResult.ModNotPresent();

            if (_shipMapStateField == null || _burnUpSetValue == null || orbitingShip == null)
                return BoolLookupResult.ReflectionFailed();

            try
            {
                var map = orbitingShip.Map;
                if (map == null)
                    return BoolLookupResult.SuccessFalse();

                // Access ShipMapComp from map.components — cannot use GetComponent<T>() without a
                // compile-time reference to the SOS2 type (standard RimWorld mod-interop pattern).
                var comp = map.components?.FirstOrDefault(c => _shipMapCompType.IsInstanceOfType(c));
                if (comp == null)
                    return BoolLookupResult.ReflectionFailed();

                var currentState = _shipMapStateField.GetValue(comp);
                return currentState != null && currentState.Equals(_burnUpSetValue)
                    ? BoolLookupResult.SuccessTrue()
                    : BoolLookupResult.SuccessFalse();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SOS2ReflectionCache.IsShipBurningUp", null, ex);
                return BoolLookupResult.ReflectionFailed();
            }
        }

        /// <summary>
        /// Returns a tri-state result for whether the ship map has entered the terminal
        /// <c>burnUpSet</c> state (same detection as <see cref="IsShipBurningUp"/> — in SOS2,
        /// all destruction paths — combat loss, re-entry, manual abandon — converge on
        /// <c>ShipMapComp.ShipMapState == burnUpSet</c>).
        ///
        /// <para>Takes <c>Map</c> directly (matching the original
        /// <c>SpaceModDetection.IsShipDestroyed(Map)</c> signature).</para>
        /// </summary>
        /// <param name="map">The ship map to inspect.</param>
        public BoolLookupResult IsShipDestroyed(Verse.Map map)
        {
            if (!_sos2Present)
                return BoolLookupResult.ModNotPresent();

            if (_shipMapStateField == null || _burnUpSetValue == null || map == null)
                return BoolLookupResult.ReflectionFailed();

            try
            {
                var comp = map.components?.FirstOrDefault(c => _shipMapCompType.IsInstanceOfType(c));
                if (comp == null)
                    return BoolLookupResult.ReflectionFailed();

                var currentState = _shipMapStateField.GetValue(comp);
                return currentState != null && currentState.Equals(_burnUpSetValue)
                    ? BoolLookupResult.SuccessTrue()
                    : BoolLookupResult.SuccessFalse();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SOS2ReflectionCache.IsShipDestroyed", null, ex);
                return BoolLookupResult.ReflectionFailed();
            }
        }

        /// <summary>
        /// Returns true if <paramref name="mapParent"/> is an instance of
        /// <c>SaveOurShip2.WorldObjectOrbitingShip</c>.
        /// Returns false if SOS2 is not loaded, the type was not resolved, or mapParent is null.
        /// </summary>
        public bool IsOrbitingShip(MapParent mapParent)
        {
            if (!_sos2Present || _worldObjectOrbitingShipType == null || mapParent == null)
                return false;

            return _worldObjectOrbitingShipType.IsInstanceOfType(mapParent);
        }
    }
}
