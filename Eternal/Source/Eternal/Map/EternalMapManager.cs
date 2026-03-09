// file path: Eternal/Source/Eternal/Map/EternalMapManager.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 21-02-2026
// Description: EternalMapManager handles temporary map retention for Eternal pawns.
//              Extended with corpse anchoring support for VGE (Vanilla Gravship Expanded) compatibility.
//              05-02: MapRemoved() override fires event-driven HandleMapRemoval for immediate corpse rescue (PERF-07).

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

// Type alias to resolve namespace shadowing (Eternal.Corpse namespace shadows Verse.Corpse type)
using CorpseType = Verse.Corpse;

namespace Eternal.Map
{
    /// <summary>
    /// EternalMapManager handles temporary map retention for Eternal pawns.
    /// Manages EternalAnchor objects and prevents map closure when Eternals die.
    /// </summary>
    public class EternalMapManager : MapComponent
    {
        private List<EternalAnchor> activeAnchors = new List<EternalAnchor>();

        // Check for unanchored corpses every 500 ticks (similar to mapCheckInterval)
        private const int CORPSE_ANCHOR_CHECK_INTERVAL = 500;
        private int ticksSinceLastCorpseCheck = 0;

        /// <summary>
        /// Initializes a new instance of EternalMapManager class.
        /// </summary>
        /// <param name="map">The map to manage.</param>
        public EternalMapManager(Verse.Map map) : base(map)
        {
        }

        /// <summary>
        /// Called every map tick to update anchor states.
        /// </summary>
        public override void MapComponentTick()
        {
            base.MapComponentTick();

            // Update all anchors and remove any that should be removed
            for (int i = activeAnchors.Count - 1; i >= 0; i--)
            {
                var anchor = activeAnchors[i];
                if (anchor.ShouldRemoveAnchor())
                {
                    RemoveAnchor(anchor);
                }
            }

            // Periodically check for unanchored Eternal corpses (VGE compatibility)
            ticksSinceLastCorpseCheck++;
            if (ticksSinceLastCorpseCheck >= CORPSE_ANCHOR_CHECK_INTERVAL)
            {
                ticksSinceLastCorpseCheck = 0;
                EnsureCorpseAnchors();
            }
        }
        
        /// <summary>
        /// Called by RimWorld immediately before the map is destroyed/unloaded.
        /// Triggers event-driven corpse rescue rather than relying on the 5000-tick fallback poll.
        /// Exception-hardened to ensure map removal always completes even if rescue fails.
        /// </summary>
        public override void MapRemoved()
        {
            base.MapRemoved();

            try
            {
                var mapProtection = EternalServiceContainer.Instance?.MapProtection;
                mapProtection?.HandleMapRemoval(this.map);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "MapRemoved", null, ex);
            }
        }

        /// <summary>
        /// Creates an anchor for a dead Eternal pawn.
        /// </summary>
        /// <param name="pawn">The dead Eternal pawn to anchor.</param>
        /// <returns>The created anchor, or null if failed.</returns>
        public EternalAnchor CreateAnchor(Pawn pawn)
        {
            if (pawn == null || !pawn.Dead)
                return null;
                
            // Check if map anchors are enabled in settings
            if (!Eternal_Mod.GetSettings().enableMapAnchors)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Map anchors disabled in settings - not creating anchor for {pawn.Name}");
                }
                return null;
            }
                
            // Check if pawn has Eternal trait (ignoring gene suppression)
            if (!pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                return null;
                
            // Check if this is a temporary map
            if (!IsTemporaryMap())
                return null;
                
            // Create anchor
            EternalAnchor anchor = (EternalAnchor)ThingMaker.MakeThing(EternalDefOf.EternalAnchor);
            anchor.SetAnchoredPawn(pawn);
            
            // Place anchor at pawn's position
            IntVec3 position = pawn.Position;
            if (!position.IsValid)
            {
                // Find a valid position near the pawn
                position = CellFinder.RandomClosewalkCellNear(pawn.Position, map, 5);
            }
            
            GenSpawn.Spawn(anchor, position, map);
            
            // Add to active anchors
            activeAnchors.Add(anchor);
            
            // Log anchor creation
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"EternalAnchor created for {pawn.Name} at {position} on map {map}");
            }
            
            return anchor;
        }

        /// <summary>
        /// Creates an anchor for an Eternal corpse on a temporary map.
        /// This supports corpses that arrive on temporary maps through transport,
        /// caravans, or gravship operations (VGE compatibility).
        /// </summary>
        /// <param name="corpse">The Eternal corpse to anchor.</param>
        /// <returns>The created anchor, or null if failed.</returns>
        public EternalAnchor CreateCorpseAnchor(CorpseType corpse)
        {
            if (corpse == null)
                return null;

            Pawn innerPawn = corpse.InnerPawn;
            if (innerPawn == null)
                return null;

            // Check if map anchors are enabled in settings
            if (!Eternal_Mod.GetSettings().enableMapAnchors)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Map anchors disabled - not creating corpse anchor for {innerPawn.Name}");
                }
                return null;
            }

            // Check if pawn has Eternal trait (ignoring gene suppression)
            if (!innerPawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                return null;

            // Check if this is a temporary map
            if (!IsTemporaryMap())
                return null;

            // Check if anchor already exists for this pawn
            if (GetAnchorForPawn(innerPawn) != null)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Anchor already exists for {innerPawn.Name}");
                }
                return null;
            }

            // Create anchor
            EternalAnchor anchor = (EternalAnchor)ThingMaker.MakeThing(EternalDefOf.EternalAnchor);
            anchor.SetAnchoredPawn(innerPawn);

            // Place anchor at corpse's position
            IntVec3 position = corpse.Position;
            if (!position.IsValid || !position.InBounds(map))
            {
                // Find a valid position near the corpse
                position = CellFinder.RandomClosewalkCellNear(map.Center, map, 10);
            }

            GenSpawn.Spawn(anchor, position, map);

            // Add to active anchors
            activeAnchors.Add(anchor);

            // Log anchor creation
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] Corpse anchor created for {innerPawn.Name} at {position} on map {map}");
            }

            return anchor;
        }

        /// <summary>
        /// Ensures all tracked Eternal corpses on this temporary map have anchors.
        /// Called periodically to catch corpses that arrived without triggering death events.
        /// </summary>
        public void EnsureCorpseAnchors()
        {
            if (!IsTemporaryMap())
                return;

            if (!Eternal_Mod.GetSettings().enableMapAnchors)
                return;

            // Get the corpse manager
            var corpseManager = EternalServiceContainer.Instance.CorpseManager;
            if (corpseManager == null || !corpseManager.HasEternalCorpses(map))
                return;

            // Check each tracked corpse
            foreach (var entry in corpseManager.GetCorpsesOnMap(map))
            {
                if (entry?.Corpse == null || entry.OriginalPawn == null)
                    continue;

                // Check if anchor already exists
                if (GetAnchorForPawn(entry.OriginalPawn) != null)
                    continue;

                // Create anchor for unanchored corpse
                if (entry.Corpse.Spawned)
                {
                    CreateCorpseAnchor(entry.Corpse);
                }
            }
        }

        /// <summary>
        /// Removes an anchor from the map.
        /// </summary>
        /// <param name="anchor">The anchor to remove.</param>
        public void RemoveAnchor(EternalAnchor anchor)
        {
            if (anchor == null)
                return;
                
            // Remove from active anchors
            if (activeAnchors.Contains(anchor))
            {
                activeAnchors.Remove(anchor);
            }
            
            // Log anchor removal
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"EternalAnchor removed for {anchor.AnchoredPawn?.Name}");
            }
        }
        
        /// <summary>
        /// Gets all active anchors on this map.
        /// </summary>
        /// <returns>List of active anchors.</returns>
        public List<EternalAnchor> GetActiveAnchors()
        {
            return new List<EternalAnchor>(activeAnchors);
        }
        
        /// <summary>
        /// Gets anchor for a specific pawn.
        /// </summary>
        /// <param name="pawn">The pawn to find anchor for.</param>
        /// <returns>The anchor for the pawn, or null if not found.</returns>
        public EternalAnchor GetAnchorForPawn(Pawn pawn)
        {
            return activeAnchors.FirstOrDefault(anchor => anchor.AnchoredPawn == pawn);
        }
        
        /// <summary>
        /// Checks if the current map is a temporary map.
        /// </summary>
        /// <returns>True if map is temporary, false otherwise.</returns>
        private bool IsTemporaryMap()
        {
            // Check if map is a temporary map (not a home map)
            if (map == null)
                return false;
                
            // Temporary maps are typically not home maps and have specific properties
            return !map.IsPlayerHome &&
                   (map.Parent != null ||
                    map.Biome?.defName == "SeaIce" ||
                    map.Biome?.defName == "IceSheet");
        }
        
        /// <summary>
        /// Checks if the map should be prevented from being removed.
        /// </summary>
        /// <returns>True if map should be retained, false otherwise.</returns>
        public bool ShouldRetainMap()
        {
            return activeAnchors.Count > 0;
        }
        
        /// <summary>
        /// Exposes data for save/load functionality.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activeAnchors, "activeAnchors", LookMode.Deep);
        }
    }
}