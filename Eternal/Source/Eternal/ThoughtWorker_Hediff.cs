// file path: Eternal/Source/Eternal/ThoughtWorker_Hediff.cs
// Author Name: 0Shard
// Date Created: 30-10-2024
// Date Last Modified: 30-10-2024
// Description: ThoughtWorker implementation for hediff-based thoughts in the Eternal mod. This class checks if a pawn has specific hediffs and generates appropriate thoughts.

using RimWorld;
using Verse;
using System.Collections.Generic;

namespace Eternal
{
    /// <summary>
    /// ThoughtWorker that generates thoughts based on the presence of specific hediffs on a pawn.
    /// This is used for the Eternal mood buff thought that activates when a pawn has the Eternal_Essence hediff.
    /// </summary>
    public class ThoughtWorker_Hediff : ThoughtWorker
    {
        /// <summary>
        /// Determines the current state of the thought for a given pawn.
        /// Checks if the pawn has any of the required hediffs defined in the ThoughtDef.
        /// </summary>
        /// <param name="p">The pawn to evaluate</param>
        /// <returns>The thought state (active/inactive) based on hediff presence</returns>
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            // Validate input parameter
            if (p == null)
            {
                return ThoughtState.Inactive;
            }

            // Check if the ThoughtDef has required hediffs defined
            if (def.requiredHediffs == null || def.requiredHediffs.Count == 0)
            {
                return ThoughtState.Inactive;
            }

            // Check if the pawn has any of the required hediffs
            foreach (HediffDef requiredHediff in def.requiredHediffs)
            {
                if (p.health.hediffSet.HasHediff(requiredHediff))
                {
                    return ThoughtState.ActiveAtStage(0);
                }
            }

            return ThoughtState.Inactive;
        }
    }
}