# Eternal

**Biological Immortality for RimWorld 1.6**

Eternal gives your pawns true biological immortality. When an Eternal dies, their corpse is preserved indefinitely -- click a gizmo to begin resurrection, and watch as missing limbs regrow through four distinct biological phases while injuries heal in parallel. Every bit of healing costs nutrition, so your colony pays the price in food. Death is never permanent, but it is never free.

## Features

- **4-Phase Body Part Regrowth** -- Missing limbs and organs regenerate through a biologically-inspired progression: Initial Formation, Tissue Development, Nerve Integration, and Functional Completion. Each phase is visible and trackable.
- **Nutrition Debt System** -- All healing accumulates a food debt at a configurable ratio (default 250:1). After resurrection, Eternals eat more until the debt is repaid. Heavy injuries mean heavy grocery bills.
- **Full Corpse Preservation** -- Eternal corpses never rot. Take as long as you need before starting resurrection.
- **Map Protection and Teleportation** -- Temporary maps (quest sites, encounters) won't close while an Eternal corpse is present. If the map must close, corpses are automatically teleported to your home map.
- **Caravan Death Support** -- Eternals who die in caravans can be resurrected while traveling. Work priorities, policies, and schedules are captured at death and restored on resurrection.
- **Accelerated Living Healing** -- Living Eternals passively heal injuries and even permanent scars over time.
- **Configurable Everything** -- Healing rates, nutrition costs, tick intervals, per-hediff behavior, and debug logging are all adjustable through mod settings.

## How It Works

### Becoming Eternal

Pawns gain immortality through the **Eternal_GeneticMarker** trait. This trait does not spawn naturally and must be assigned via character creation, dev tools, or custom scenarios.

### The Resurrection Flow

1. An Eternal pawn dies -- their corpse is automatically preserved and a healing snapshot is captured
2. A **Resurrect Eternal** gizmo appears on the corpse, showing the total healing cost
3. Clicking the gizmo begins the healing process on the dead pawn
4. Critical parts regrow in strict sequence: **Neck -> Head -> Skull -> Brain** (prevents death loops)
5. All other missing parts regrow in parallel once their parent parts exist
6. Injuries and hediffs heal simultaneously using stage-based multipliers
7. Once healing is complete, the pawn resurrects with all assignments restored

### Food Debt

Every point of severity healed costs nutrition at a ratio of 250:1 (250 severity healed = 1 nutrition owed). After resurrection, the pawn's food consumption increases until the debt is cleared. Pawns with food need disabled (via genes, hediffs, or traits) have all costs waived.

## Requirements

- **RimWorld 1.6**
- **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)** (must load before Eternal)

## Installation

1. Subscribe to [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) on Steam Workshop
2. Download Eternal and place it in your RimWorld `Mods/` folder
3. In the mod menu, ensure **Harmony loads before Eternal**
4. Start a new game or load an existing save

## Compatibility

Eternal includes dedicated Harmony patches for each of the following:

- **Odyssey DLC** -- Full space travel support. Eternals left behind during ship departure safely crash-land rather than being destroyed.
- **Vanilla Genetics Expanded** -- Compatible hediff handling ensures gene-related health conditions interact correctly with Eternal healing.
- **Save Our Ship 2** -- Ship launch compatibility so Eternals survive the journey.
- **RimImmortal** -- Coexistence support allowing both immortality systems to operate without conflicts.
- **Expanded Body Framework** -- Compatible body part handling for custom body structures.

## Configuration

Access mod settings via **Options > Mod Settings > Eternal** to adjust:

- Base healing rate and tick intervals
- Nutrition cost multiplier and severity-to-nutrition ratio
- Per-hediff healing behavior (enable/disable, custom rates)
- Corpse check and map protection intervals
- Debug logging

## Known Limitations

- The Eternal trait must be manually assigned (does not spawn naturally)
- Resurrection requires the corpse to exist (no recovery from total destruction)
- Heavy food debt can strain colony resources

---

## For Developers

### Building

```bash
# Build the mod (from project root)
dotnet build Eternal/Source/EternalSolution.sln

# Release build
dotnet build Eternal/Source/EternalSolution.sln -c Release

# Debug build
dotnet build Eternal/Source/EternalSolution.sln -c Debug
```

Output: `Eternal/1.6/Assemblies/Eternal.dll`

### Scripts

| Script | Purpose |
|--------|---------|
| `create-mod-package.sh` | Creates a distributable `Mod/` directory with proper structure (About, Defs, Languages, Assemblies) |
| `deploy-to-rimworld.sh` | Deploys to your local RimWorld mods folder for testing |

### Architecture Overview

The codebase follows a **dependency injection** pattern via `EternalServiceContainer` -- no singletons. All managers are instantiated by `Eternal_Component` and registered with the container.

Key patterns:
- **TickOrchestrator** -- Extracts tick-based processing from the main component into a dedicated orchestrator with configurable intervals
- **MVP UI** -- Model-View-Presenter pattern for settings and management tabs
- **Pre-calculated healing queues** -- Injury snapshots captured at death time to prevent RimWorld from modifying hediffs before resurrection starts

Explore the `Source/` directory for implementation details.

---

## Credits

**Author:** 0Shard

## Disclaimer

This mod is not associated with, endorsed by, or related to Marvel's "Eternals" franchise in any way. The name "Eternal" refers solely to the in-game biological immortality mechanic.

## License

MIT License with Attribution and Notification Requirement. See [LICENSE](LICENSE) for full terms.

You are free to use, modify, and distribute this software. Modified versions must credit the original author and notify them of public distribution.
