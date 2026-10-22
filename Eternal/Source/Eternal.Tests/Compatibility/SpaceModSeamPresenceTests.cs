// Author: 0Shard
// Description: Installed-mod metadata contracts with discovery-visible absence and strict policy.
using System.IO;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    internal sealed class OptionalModFactAttribute : FactAttribute
    {
        public OptionalModFactAttribute(string mod) : this(mod, CompiledBuildReferences.OptionalPath(mod), CompiledBuildReferences.RequireOptional) { }

        internal OptionalModFactAttribute(string mod, string path, bool required)
        {
            if (!File.Exists(path) && !required)
            {
                Skip = mod + " unavailable: configured assembly absent at " + path + "; no integration verified";
            }
        }
    }

    public class SpaceModSeamPresenceTests
    {
        [OptionalModFact("SOS2")]
        public void SOS2Seams_AllPresent()
        {
            using var assembly = OpenRequired("SOS2");
            foreach (var seam in new[]
            {
                ("SaveOurShip2.WorldObjectOrbitingShip", "ShouldRemoveMapNow"),
                ("SaveOurShip2.WorldObjectOrbitingShip", "Destroy"),
                ("SaveOurShip2.ShipMapComp", "KillAllOffShip"),
                ("SaveOurShip2.ShipMapComp", "DeRegisterShuttleMission"),
                ("SaveOurShip2.ShipMapComp", "get_MapShipCells"),
                ("SaveOurShip2.ShipMapComp", "get_ShipsOnMap"),
                ("SaveOurShip2.ShipInteriorMod2", "RemoveShipOrArea"),
                ("SaveOurShip2.ShipInteriorMod2", "MoveShip"),
                ("SaveOurShip2.SpaceShipCache", "FloatAndDestroy"),
                ("SaveOurShip2.SpaceShipCache", "get_Map")
            })
            {
                assembly.Method(seam.Item1, seam.Item2, inherited: true);
            }

            RequireField(assembly, "SaveOurShip2.ShipMapComp+ShuttleMissionData", "shuttle");
            RequireField(assembly, "SaveOurShip2.SpaceShipCache", "Area");
        }

        [OptionalModFact("VGE")]
        public void VGESeams_AllPresent()
        {
            using var assembly = OpenRequired("VGE");
            RequireField(assembly, "VanillaGravshipExpanded.GravshipMapGenUtility", "BlockingThings");
            // LandingEnded is resolved on the game's GravshipController, not in the VGE DLL.
            using var game = new CompiledAssemblyContract(CompiledBuildReferences.GameReferencePath);
            game.Method("Verse.WorldComponent_GravshipController", "LandingEnded", inherited: true);
        }

        private static CompiledAssemblyContract OpenRequired(string mod)
        {
            return OpenRequiredPath(mod, CompiledBuildReferences.OptionalPath(mod));
        }

        internal static CompiledAssemblyContract OpenRequiredPath(string mod, string path)
        {
            Assert.True(File.Exists(path), mod + " required optional assembly missing: " + path);
            return new CompiledAssemblyContract(path);
        }

        private static void RequireField(CompiledAssemblyContract assembly, string type, string name)
        {
            var fields = assembly.Reader.GetTypeDefinition(assembly.Type(type)).GetFields();
            Assert.Contains(fields, handle => assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(handle).Name) == name);
        }
    }
}
