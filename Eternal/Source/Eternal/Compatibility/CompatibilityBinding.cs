// Relative Path: Eternal/Source/Eternal/Compatibility/CompatibilityBinding.cs
// Creation Date: 13-10-2026
// Author: 0Shard
// Description: Mechanical cache access and diagnostics shared by ISF, RI and CTR bindings.

using System;
using Verse;

namespace Eternal.Compatibility
{
    internal static class CompatibilityBinding
    {
        internal static T Cached<T>(Action initialize, ref T member)
        {
            // Read by reference after initialization so the first lookup sees the populated cache.
            initialize();
            return member;
        }

        internal static T WarnMissing<T>(T member, string warning) where T : class
        {
            if (member == null)
            {
                Log.Warning(warning);
            }

            return member;
        }

        internal static bool Prepare(bool active, string inactiveMessage, string activeMessage)
        {
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message(active ? activeMessage : inactiveMessage);
            }

            return active;
        }
    }
}
