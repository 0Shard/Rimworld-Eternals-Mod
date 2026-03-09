// file path: Eternal/Source/Eternal/Map/EternalAnchor.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 19-02-2026
// Description: EternalAnchor prevents map closure when Eternals die on temporary maps, allowing for resurrection.

using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace Eternal.Map
{
    /// <summary>
    /// EternalAnchor prevents map closure when Eternals die on temporary maps.
    /// This ensures that temporary maps remain active to allow for resurrection.
    /// </summary>
    public class EternalAnchor : Thing, IThingHolder
    {
        private ThingOwner<Thing> innerContainer;
        private Pawn anchoredPawn;
        private int creationTick;
        private int resurrectionTick = -1;
        
        /// <summary>
        /// Gets the resurrection grace period from settings, with a fallback default.
        /// </summary>
        private int ResurrectionGracePeriod => Eternal_Mod.GetSettings().anchorGracePeriodTicks;
        
        /// <summary>
        /// Initializes a new instance of EternalAnchor class.
        /// </summary>
        public EternalAnchor()
        {
            innerContainer = new ThingOwner<Thing>(this);
            creationTick = Find.TickManager.TicksGame;
        }
        
        /// <summary>
        /// Gets the anchored pawn.
        /// </summary>
        public Pawn AnchoredPawn => anchoredPawn;
        
        /// <summary>
        /// Sets the anchored pawn.
        /// </summary>
        /// <param name="pawn">The pawn to anchor.</param>
        public void SetAnchoredPawn(Pawn pawn)
        {
            anchoredPawn = pawn;
        }
        
        /// <summary>
        /// Checks if the anchor should be removed.
        /// </summary>
        /// <returns>True if anchor should be removed, false otherwise.</returns>
        public bool ShouldRemoveAnchor()
        {
            // Remove if anchored pawn is null or destroyed
            if (anchoredPawn == null || anchoredPawn.Destroyed)
                return true;
                
            // Track when pawn resurrects (no longer dead)
            if (!anchoredPawn.Dead && resurrectionTick == -1)
            {
                resurrectionTick = Find.TickManager.TicksGame;
                
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Anchor for {anchoredPawn.Name} detected resurrection at tick {resurrectionTick}");
                }
            }
            
            // Remove after grace period expires following resurrection
            if (resurrectionTick != -1)
            {
                int currentTick = Find.TickManager.TicksGame;
                int ticksSinceResurrection = currentTick - resurrectionTick;
                int gracePeriod = ResurrectionGracePeriod;
                
                if (ticksSinceResurrection > gracePeriod)
                {
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Anchor grace period ({gracePeriod} ticks) expired for {anchoredPawn.Name} after {ticksSinceResurrection} ticks");
                    }
                    return true;
                }
            }
            
            // Anchor persists indefinitely until pawn resurrects (no timeout)
            return false;
        }
        
        /// <summary>
        /// Called every tick to update anchor state.
        /// </summary>
        protected override void Tick()
        {
            base.Tick();
            
            // Check if anchor should be removed
            if (ShouldRemoveAnchor())
            {
                RemoveAnchor();
            }
        }
        
        /// <summary>
        /// Removes the anchor and cleans up.
        /// </summary>
        private void RemoveAnchor()
        {
            // Notify map manager that anchor is being removed
            if (Map != null)
            {
                var mapManager = Map.GetComponent<EternalMapManager>();
                mapManager?.RemoveAnchor(this);
            }
            
            // Destroy the anchor
            Destroy();
        }
        
        /// <summary>
        /// Gets the thing holder for inner container.
        /// </summary>
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }
        
        /// <summary>
        /// Gets the parent thing holder.
        /// </summary>
        public new IThingHolder ParentHolder => null;
        
        /// <summary>
        /// Gets child holders for thing holder interface.
        /// </summary>
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            // Add this holder to list
            outChildren.Add(this);
        }
        
        /// <summary>
        /// Draws the anchor in the world.
        /// </summary>
        public void Draw()
        {
            // Draw a subtle visual indicator for the anchor
            if (anchoredPawn != null && anchoredPawn.Corpse != null)
            {
                // Draw a faint glow around the anchored pawn's corpse
                Vector3 drawPos = anchoredPawn.Corpse.DrawPos;
                drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                
                // Draw a pulsing effect
                float pulse = (float)Math.Sin(Find.TickManager.TicksGame * 0.01f) * 0.1f + 0.9f;
                Color glowColor = new Color(0.3f, 0.7f, 1.0f, 0.3f * pulse);
                
                // Draw glow effect
                Material glowMat = SolidColorMaterials.SimpleSolidColorMaterial(glowColor);
                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(drawPos, Quaternion.identity, new Vector3(2f, 1f, 2f));
                Graphics.DrawMesh(MeshPool.plane10, matrix, glowMat, 0);
            }
        }
        
        /// <summary>
        /// Exposes data for save/load functionality.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref anchoredPawn, "anchoredPawn");
            Scribe_Values.Look(ref creationTick, "creationTick", 0);
            Scribe_Values.Look(ref resurrectionTick, "resurrectionTick", -1);
            Scribe_Deep.Look(ref innerContainer, "innerContainer");
        }
    }
}