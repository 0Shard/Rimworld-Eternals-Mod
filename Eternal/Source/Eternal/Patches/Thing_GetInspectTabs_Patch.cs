// Relative Path: Eternal/Source/Eternal/Patches/Thing_GetInspectTabs_Patch.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Appends a shared Eternal inspect tab without changing shared ThingDef lists.
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;
using Eternal.UI.Management;

namespace Eternal.Patches
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetInspectTabs))]
    public static class Thing_GetInspectTabs_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref IEnumerable<InspectTabBase> __result)
        {
            if (!ManagementTabView.IsEligible(__instance))
            {
                return;
            }

            var tabs = __result?.ToList() ?? new List<InspectTabBase>();
            if (tabs.Any(t => t is ManagementTabView))
            {
                return;
            }

            tabs.Add(InspectTabManager.GetSharedInstance(typeof(ManagementTabView)));
            __result = tabs;
        }
    }
}
