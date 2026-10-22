using RimWorld;
using RimWorld.Planet;
using Verse;
using CaravanType = RimWorld.Planet.Caravan;

namespace Eternal.World
{
    public sealed class CaravanArrivalAction_EternalCrashSite : CaravanArrivalAction
    {
        private WorldObject_EternalCrashSite site;
        private bool pickup;

        public CaravanArrivalAction_EternalCrashSite() { }
        public CaravanArrivalAction_EternalCrashSite(WorldObject_EternalCrashSite site, bool pickup)
        { this.site = site; this.pickup = pickup; }

        public override string Label => pickup ? "RescueEternalsWithCaravan".Translate() : "EnterMap".Translate(site?.Label);
        public override string ReportString => Label;

        public override FloatMenuAcceptanceReport StillValid(CaravanType caravan, PlanetTile destinationTile)
            => site != null && site.CanRecover(caravan, destinationTile)
                && (!pickup || (!site.HasMap && site.HasRecoveryContents));

        public override void Arrived(CaravanType caravan)
        {
            if (caravan == null || !StillValid(caravan, caravan.Tile)) { return; }
            if (pickup) { site.RescueEternalsToCaravan(caravan); }
            else { site.EnterRecoveryMap(caravan); }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref site, "site");
            Scribe_Values.Look(ref pickup, "pickup");
        }
    }
}
