# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

**Eternal** is a RimWorld 1.6 mod that introduces biological immortality mechanics with precise body part regrowth and resource-based balance. Pawns with the `Eternal_GeneticMarker` trait can resurrect after death through a player-initiated process involving a 4-phase body part regrowth system.

## Author Override

This is a public project. The file header `Author` field is overridden to `0Shard` instead of the global default. Use `Author: 0Shard` in all file headers within this repository. The real name must NEVER appear in any file that could reach the public repo.

## Dual-Repo Publishing (CRITICAL)

This project uses two GitHub remotes:

| Remote | Repo | Visibility |
|--------|------|-----------|
| `origin` | `0Shard/Eternal` | **PRIVATE** — all dev pushes go here |
| `public` | `0Shard/Rimworld-Eternals-Mod` | **PUBLIC** — ONLY push via `publish.sh` |

**Rules:**
- **NEVER push to the `public` remote directly.** No `git push public`. No manual checkout of the `public` branch.
- All `git push` in normal workflow targets `origin` only.
- To update the public repo: `bash publish.sh <version> "message"`
- If a command would push to `public`, WARN the user and ask for confirmation first.
- The `public` branch is an orphan branch managed exclusively by `publish.sh`.

## Build Commands

```bash
# Build the mod (from project root)
dotnet build Eternal/Source/EternalSolution.sln

# Build specific configuration
dotnet build Eternal/Source/EternalSolution.sln -c Release
dotnet build Eternal/Source/EternalSolution.sln -c Debug

# Output location: Eternal/1.6/Assemblies/Eternal.dll
```

## Release Workflow

Full release from dev to users (GitHub + Steam Workshop):

```bash
# 1. Build mod package (creates Mods/Eternal/ with About, Defs, Languages, Assemblies, Textures)
bash create-mod-package.sh

# 2. Publish to GitHub public repo + create release with zip
bash publish.sh v1.x -m "Release message"

# 3. Upload to Steam Workshop
bash workshop-upload.sh -m "Change note for subscribers"
```

### Script Reference

**`create-mod-package.sh`** — Builds `Mods/Eternal/` from source
- Copies About.xml, PublishedFileId.txt, Defs, Languages, Assemblies, Textures
- Excludes Harmony DLL (users install separately)
- Verifies package structure
- No flags — just run it

**`publish.sh`** — Pushes to public GitHub repo + creates release
- Usage: `bash publish.sh <version> [-m "message"] [--no-release]`
- `-m "msg"` — Commit message (default: "Release \<version\>")
- `--no-release` — Sync files only, skip GitHub release creation
- Auto-excludes private files (AGENTS.md, .planning/, workshop-upload.sh, etc.)
- **NEVER run `git push public` manually** — use this script only

**`workshop-upload.sh`** — Uploads to Steam Workshop via SteamCMD
- Usage: `bash workshop-upload.sh [-m "change note"] [--dry-run]`
- `-m "msg"` — Change note shown to Workshop subscribers (default: "Update")
- `--dry-run` — Show .vdf and command without uploading
- Reads Steam username from `.workshop-config` (gitignored), prompts to create on first run
- Requires: SteamCMD in PATH, `Mods/Eternal/` built via `create-mod-package.sh`
- Workshop item ID: 3681613276 (stored in `Eternal/About/PublishedFileId.txt`)

**`deploy-to-rimworld.sh`** — Copies `Mods/Eternal/` to RimWorld's local Mods directory for testing

The project targets .NET Framework 4.7.2 and uses NuGet packages for RimWorld references (`Krafs.Rimworld.Ref 1.6.4494-beta`) and Harmony (`Lib.Harmony 2.3.2`).

## Architecture Overview

### Core Design Pattern: Dependency Injection via Service Container

The mod uses `EternalServiceContainer` for dependency injection. **No singletons** - all managers are instantiated by `Eternal_Component` and registered with the container.

```csharp
// Access services via the container (preferred)
EternalServiceContainer.Instance.HealingProcessor
EternalServiceContainer.Instance.CorpseManager
EternalServiceContainer.Instance.FoodDebtSystem
EternalServiceContainer.Instance.HediffHealingCalculator

// Or via component (legacy, delegates to container)
Eternal_Component.Current.HealingProcessor
```

**Key DI Classes** (`Eternal/Source/Eternal/DI/`):
- `EternalServiceContainer`: Central container holding all service instances
- `SettingsAdapter`: Implements `ISettingsProvider` wrapping `Eternal_Mod.settings`

**Settings Architecture** (`Eternal/Source/Eternal/UI/Eternal_Settings.cs`):
- `SettingsDefaults`: Static class with all default values (single source of truth)
  - Includes `GetSeverityToNutritionRatioDisplay()` helper for UI ratio display
- `Eternal_Settings`: ModSettings subclass with per-section reset methods
- Default severity-to-nutrition ratio: 0.004f (250:1 - meaning 250 severity = 1 nutrition)

### TickOrchestrator Pattern

Tick-based processing is extracted from the god class into `TickOrchestrator` (`Components/TickOrchestrator.cs`):
- Receives all dependencies via constructor injection
- Handles all tick interval logic
- Implements `IExposable` for timing state serialization

### Tick-Based Processing Structure

The mod processes on multiple tick intervals configured in `Eternal_Settings`:

| Interval | Default | Purpose |
|----------|---------|---------|
| normalTickRate | 60 | Injury healing for living pawns |
| rareTickRate | 250 | Scars, regrowth, corpse healing, food debt |
| corpseCheckInterval | 1000 | Corpse preservation, cleanup |
| mapCheckInterval | 500 | Protect maps containing Eternal corpses |
| traitCheckInterval | 5000 | Trait-hediff consistency checks |

### Key Subsystems

**Healing System** (`Eternal/Source/Eternal/Healing/`)
- `EternalHealingProcessor`: Living pawn healing orchestration
- `EternalCorpseHealingProcessor`: Resurrection healing for dead pawns
  - `CompleteResurrection()`: Uses HediffSet swap pattern (Immortals mod) to preserve hediffs
  - `IsHealingComplete()`: Validates queue empty + no missing parts + regrowth complete
  - `ValidatePreResurrection()`: Comprehensive pre-resurrection validation
  - `FindCaravanContainingCorpse()`: Locates caravan containing un-spawned corpse
  - `StartCorpseHealing()`: Uses pre-calculated queue from death, falls back to live calculation
- `EternalHediffHealer`: Per-hediff healing with stage-based multipliers
- `EternalResurrectionCalculator`: Calculates healing queue based on missing parts
- `UnifiedHediffHealingCalculator`: Consolidates hediff healing logic (implements `IHediffHealingCalculator`)

**Healing Formula** (stage-based multipliers for debuffs):
```
healingAmount = effectiveRate × stageMultiplier × bodySize × severityScaling

Stage multipliers: Stage 0 = 1.0×, Stage 1 = 0.8×, Stage 2 = 0.6×, Stage 3 = 0.4×, Stage 4+ = 0.2×
```

**Regrowth System** (`Eternal/Source/Eternal/Regrowth/`)
- `EternalRegrowthState`: Per-pawn state tracking with 4-phase progression
- `EternalRegrowthManager`: Coordinates regrowth across all Eternals
- Uses `RegrowthPartState` model for consolidated phase/progress tracking

**Corpse Management** (`Eternal/Source/Eternal/Corpse/`)
- `EternalCorpseManager`: Global corpse tracking with save/load persistence
  - `RegisterCorpse()` now accepts optional `preCalculatedQueue` parameter
- `EternalCorpsePreservation`: Prevents decay of tracked corpses
- `EternalMapProtection`: Prevents temporary maps from closing while containing Eternals
  - `ScheduleCorpseTeleport()`: Schedules delayed teleportation with save/load support
  - `ProcessPendingTeleports()`: Processes scheduled teleports each tick
  - `PendingTeleport`: IExposable class for teleport persistence

**Caravan Handling** (`Eternal/Source/Eternal/Caravan/`)
- `EternalCaravanDeathHandler`: Handles Eternal deaths in caravans
  - `IsPawnInCaravan(Pawn)`: Static method to detect caravan membership
  - `RegisterDeath(Pawn, Corpse)`: Registers death with pre-calculated healing queue

**Food Debt** (`Eternal/Source/Eternal/Resources/`)
- `UnifiedFoodDebtManager`: Tracks nutrition debt accumulated during healing
- Implements `IFoodDebtSystem` interface for testability
- Nutrition cost formula: `severityHealed × severityToNutritionRatio × nutritionCostMultiplier`
- Default ratio: 0.004f (250:1) - healing 250 severity costs 1 nutrition
- **Food Need Waiver**: Pawns with food need disabled (via genes, hediffs, traits, race, or ideology) have all nutrient costs waived. Uses `pawn.HasFoodNeedDisabled()` extension method for detection.

### Critical Part Sequence

Body parts follow strict regrowth order to prevent death loops:
```
Neck → Head → Skull → Brain (sequential, enforced order)
```
All other parts (limbs, eyes, ears, etc.) regrow in parallel once their parent parts exist.

Defined in `Eternal/Source/Eternal/Constants/CriticalPartConstants.cs`.

### 4-Phase Regrowth System

Every body part progresses through:
```csharp
enum RegrowthPhase {
    InitialFormation,    // 0-25%
    TissueDevelopment,   // 25-50%
    NerveIntegration,    // 50-75%
    FunctionalCompletion,// 75-100%
    Complete
}
```

### UI Architecture (MVP Pattern)

The mod uses Model-View-Presenter for complex UI:
- `Eternal/Source/Eternal/UI/HediffSettings/` - Hediff configuration UI
- `Eternal/Source/Eternal/UI/Management/` - Management tab UI
- `Eternal/Source/Eternal/UI/Settings/` - Settings drawer/validator

### Harmony Patches

Located in `Eternal/Source/Eternal/Patches/`:
- `Pawn_HealthTracker_Patch`: Intercepts death for Eternal registration
- `Pawn_HealthTracker_AddHediff_Patch`: Intercepts hediff addition
- `TraitSet_Patch`: Auto-adds `Eternal_Essence` hediff when trait is added
- `MapParent_Patch`: Prevents map removal with Eternal corpses
- `CompRottable_Patch`: Prevents Eternal corpse decay
- `Hediff_PostRemoved_Patch`: Handles hediff removal events
- `RoofCollapse_Patch`: Protects Eternal corpses from roof collapse
- `SettlementAbandonUtility_Patch`: Prevents settlement abandonment with Eternal corpses
- `GameComponentRegistration_Patch`: Programmatic component registration
- `Odyssey/`: DLC-specific patches for space travel scenarios

**Note:** Food debt repayment is handled by `ProcessDebtRepaymentAndSync()` in `TickOrchestrator` (not a Harmony patch).

### RimWorld Death Sequence (Critical Knowledge)

When a pawn dies in RimWorld, the execution order is:
```
Pawn.Kill()
  -> Thing.Destroy() [pawn.Destroyed = true]
  -> Pawn_HealthTracker.Notify_PawnDied()
     -> Eternal_Hediff.Notify_PawnDied() [our PRIMARY registration hook]
```

**Key insight**: By the time `Notify_PawnDied()` is called, `pawn.Destroyed` is ALREADY true. This is why `IsValidEternalCorpse()` must NOT check the Destroyed flag - doing so would prevent all corpse registration.

### Resurrection Flow (Updated)

The resurrection process follows this precise sequence:

```
1. Pawn dies -> Eternal_Hediff.Notify_PawnDied()
   a. Capture PawnAssignmentSnapshot (work priorities, policies, schedules)
   b. Calculate healing queue NOW (captures injuries before RimWorld removes them)
   c. Check for caravan death -> delegate to EternalCaravanDeathHandler if in caravan
   d. Register corpse with EternalCorpseManager (includes pre-calculated queue)

2. User clicks "Resurrect Eternal" gizmo on corpse
   a. StartCorpseHealing() uses pre-calculated queue if available
   b. Injuries + regrowth heal in parallel while corpse is dead

3. When IsHealingComplete() returns true:
   a. Validates queue empty + no missing parts + regrowth complete
   b. CompleteResurrection() executes:
      i.   Save HediffSet and ImmunityHandler (Immortals pattern)
      ii.  Create clean slate for resurrection (new HediffSet/ImmunityHandler)
      iii. Handle caravan/storage corpses (find containing caravan)
      iv.  Call ResurrectionUtility.TryResurrect()
      v.   Restore saved HediffSet and ImmunityHandler
      vi.  Re-add to caravan if needed
      vii. Restore work priorities/policies from snapshot
```

### HediffSet Swap Pattern (Immortals Pattern)

RimWorld's `ResurrectionUtility.TryResurrect()` nukes the pawn's `HediffSet` during resurrection. To preserve custom hediffs like `Eternal_Essence`, we use the pattern from the Immortals mod:

```csharp
// Save before resurrection
var savedHediffSet = pawn.health.hediffSet;
var savedImmunity = pawn.health.immunity;

// Create clean slate for RimWorld's resurrection
pawn.health.hediffSet = new HediffSet(pawn);
pawn.health.immunity = new ImmunityHandler(pawn);

// Let RimWorld resurrect
ResurrectionUtility.TryResurrect(pawn, null);

// Restore our hediffs
pawn.health.hediffSet = savedHediffSet;
pawn.health.immunity = savedImmunity;
```

**Location**: `EternalCorpseHealingProcessor.CompleteResurrection()` and `ResurrectImmediately()`

### Pre-Calculated Healing Queue

**Problem**: RimWorld removes/modifies hediffs on dead pawns over time, causing injuries to be missing when resurrection starts later.

**Solution**: Calculate the healing queue at death time in `Notify_PawnDied()`, storing it in `CorpseTrackingEntry.PreCalculatedHealingQueue`. When `StartCorpseHealing()` runs, it uses this pre-calculated queue instead of recalculating.

**Flow**:
1. `Eternal_Hediff.Notify_PawnDied()` calls `EternalResurrectionCalculator.CalculateHealingQueue(pawn)`
2. Queue stored in `CorpseTrackingEntry.PreCalculatedHealingQueue`
3. `EternalCorpseHealingProcessor.StartCorpseHealing()` checks for pre-calculated queue first
4. Falls back to live calculation only if no pre-calculated queue exists (with warning)

### Caravan Death Handling

When an Eternal dies in a caravan:

1. `Eternal_Hediff.Notify_PawnDied()` detects caravan via `EternalCaravanDeathHandler.IsPawnInCaravan(pawn)`
2. Delegates to `EternalCaravanDeathHandler.RegisterDeath(pawn, corpse)`
3. Handler pre-calculates healing queue and registers with `EternalCorpseManager`
4. Corpse remains in caravan, can be resurrected while traveling
5. On resurrection, pawn is re-added to caravan via `Caravan.AddPawn()`

**Key Methods**:
- `EternalCaravanDeathHandler.IsPawnInCaravan(Pawn)` - static, checks pawn or corpse in caravan
- `EternalCaravanDeathHandler.RegisterDeath(Pawn, Corpse)` - registers caravan death with snapshot
- `EternalCorpseHealingProcessor.FindCaravanContainingCorpse(Corpse)` - finds caravan for un-spawned corpse

### Map Protection Teleportation

Eternals dying on temporary maps (quest sites, caravan encounters) need protection from map closure.

**Teleportation System** (`EternalMapProtection`):
- `ScheduleCorpseTeleport(Corpse, Map, int delayTicks)` - schedules delayed teleportation to target map
- `ProcessPendingTeleports()` - processes all scheduled teleports that have reached trigger time
- `PendingTeleport` class with `IExposable` for save/load persistence

**Flow**:
1. Temporary map with Eternal corpse detected in `CheckAndProtectMaps()`
2. Based on settings, corpses are scheduled for teleportation (default ~12 hours delay)
3. `ProcessPendingTeleports()` called each tick, executes mature teleports
4. Corpse despawned from source, spawned on target map
5. Player notified via message

### DefOf Verification

`EternalDefOf.VerifyBindings()` validates critical def references at startup:
- Called in `Eternal_Component.FinalizeInit()`
- Logs errors if `Eternal_GeneticMarker`, `Eternal_Essence`, or `Eternal_Regrowing` are null
- Helps catch XML definition issues or mod load order problems early

## Key Interfaces

Located in `Eternal/Source/Eternal/Interfaces/`:

```csharp
// Settings abstraction (wraps Eternal_Mod.settings)
ISettingsProvider {
    float BaseHealingRate { get; }
    float NutritionCostMultiplier { get; }
    float MaxDebtMultiplier { get; }
    // etc.
}

// Hediff-specific healing calculations (stage-based, per-hediff rates)
IHediffHealingCalculator {
    float CalculateHediffHealing(Pawn pawn, Hediff hediff, EternalHediffSetting setting);
    float GetStageMultiplier(Hediff hediff);
    float GetSeverityScaling(Hediff hediff, Pawn pawn);
}

// Simple healing rate (regrowth, corpse healing)
IHealingRateCalculator {
    float CalculateHealingPerTick(Pawn pawn);
}

// Debt accumulation with resurrection cap
IDebtAccumulator {
    void AddDebt(Pawn pawn, float amount);
    void AddDebtWithResurrectionCap(Pawn pawn, float amount, float costCap);
}

// Body part restoration
IPartRestorer {
    void RestorePart(Pawn pawn, BodyPartRecord part);
    void RestoreAllMissingParts(Pawn pawn);
}

// Food debt (ISP: inherits IFoodDebtReader + IFoodDebtWriter)
IFoodDebtSystem {
    void RegisterPawn(Pawn pawn);
    bool AddDebt(Pawn pawn, float amount);
    float GetDebt(Pawn pawn);
    float RepayDebt(Pawn pawn, float amount);
    float GetMaxCapacity(Pawn pawn);
    // etc.
}
```

## Key Extension Methods

Located in `Eternal/Source/Eternal/Extensions/`:
- `pawn.IsValidEternal()` - Check if pawn has Eternal trait and is valid (living pawns)
- `pawn.IsValidEternalCorpse()` - Check if pawn is valid Eternal for corpse registration (dead pawns)
- `pawn.HasFoodNeedDisabled()` - Check if pawn's food need is disabled (works for both living and dead pawns)
- Use these for consistent Eternal pawn validation

**Important**: `IsValidEternalCorpse()` does NOT check `pawn.Destroyed` because dead pawns are always destroyed Things in RimWorld. See "RimWorld Death Sequence" below.

## Data Models

**CorpseTrackingEntry** (`Models/CorpseTrackingEntry.cs`): Consolidated corpse tracking data
- `PreCalculatedHealingQueue`: Healing queue captured at death (before RimWorld removes injuries)
- `AssignmentSnapshot`: Work priorities/policies for post-resurrection restoration
- `CachedRottable`/`CachedEternalComponent`: Performance-optimized component access

**PawnAssignmentSnapshot** (`Models/PawnAssignmentSnapshot.cs`): Captures work priorities, policies, schedules for restoration

**RegrowthPartState** (`Models/RegrowthPartState.cs`): Per-body-part phase and progress

## Mod Dependencies

- **Harmony** (brrainz.harmony) - Required for runtime patching
- **RimWorld 1.6** - Target game version

## Directory Structure

```
Eternal/
├── About/           # Mod metadata (About.xml)
├── 1.6/
│   └── Assemblies/  # Compiled DLL output
├── Defs/            # XML definitions (Hediffs, Traits, ThoughtDefs, etc.)
├── Languages/       # Localization (English/Keyed/)
└── Source/
    └── Eternal/
        ├── Caravan/       # EternalCaravanDeathHandler (caravan death handling)
        ├── Components/    # Eternal_Component, TickOrchestrator
        ├── Corpse/        # Corpse tracking, preservation, map protection
        ├── DI/            # EternalServiceContainer, SettingsAdapter
        ├── Healing/       # Processors, calculators, scar healing
        ├── Hediffs/       # Eternal_Hediff (PRIMARY death registration hook)
        ├── Regrowth/      # 4-phase regrowth system
        ├── Patches/       # Harmony patches (incl. VGE/, Odyssey/)
        ├── UI/            # Settings, management tabs (MVP pattern)
        ├── Models/        # Data structures (CorpseTrackingEntry, etc.)
        ├── Interfaces/    # Service abstractions (I*Calculator, I*System)
        ├── Resources/     # UnifiedFoodDebtManager
        ├── Extensions/    # Pawn, BodyPart, Nutrition extensions
        ├── Settings/      # SettingsAdapter implementation
        └── Constants/     # Critical part definitions
```

## Tools

### dnSpy (.NET Decompiler)

**Location**: `C:/Users/Nikos/Desktop/dnspy/dnSpy.Console.exe`

```bash
# Decompile all DLLs in a folder to C# projects
dnSpy.Console.exe -o <output_dir> <input_folder>

# With custom solution name
dnSpy.Console.exe -o <output_dir> --sln-name <name> <input_folder>

# Recursive decompilation (subdirectories)
dnSpy.Console.exe -o <output_dir> -r <input_folder>

# Decompile specific DLLs
dnSpy.Console.exe -o <output_dir> <path>/*.dll
```

**Key Options**:
| Option | Description |
|--------|-------------|
| `-o <dir>` | Output directory |
| `-r` | Recursive search for .NET files |
| `-l <lang>` | Language: C#, Visual Basic, IL |
| `--sln-name <name>` | Solution file name |
| `--no-sln` | Don't create .sln file |
| `--no-resources` | Skip resource unpacking |
| `-t <type>` | Decompile specific type to stdout |

## Reference Materials

- `docs/structure.md` - Current architecture documentation
- `docs/corpse-registration-destroyed-flag-fix.md` - Critical bug fix: RimWorld death sequence timing
- `docs/healing-system-bug-fix.md` - Healing system bug fixes
- `docs/Eternal-Plan.md` - Archived design proposal (superseded)
- `docs/Eternal-implementation-plan.md` - Archived implementation roadmap
- `Example/` - Reference mods (SaveOurShip2, Immortals, WorkTab, RimImmortal)

### RimWorld Decompiled Source

**Location**: `RimWorld-Decompiled/`

Contains decompiled C# source from RimWorld's assemblies. Essential for understanding game mechanics.

**Structure**:
```
RimWorld-Decompiled/
├── RimWorld/Assembly-CSharp/
│   ├── RimWorld/          # Game logic (abilities, jobs, UI, etc.)
│   └── Verse/             # Core engine (hediffs, pawns, things)
├── Unity/                 # Unity engine components
└── Verse/                 # Additional Verse namespace code
```

**Common Use Cases**:
- Understanding hediff components: `Verse/HediffComp_*.cs`
- Gene mechanics: `Verse/Gene_*.cs`, `RimWorld/GeneUtility.cs`
- Pawn health: `Verse/Pawn_HealthTracker.cs`
- DLC features: Search for `ModsConfig.BiotechActive`, etc.

**Example**: Bloodfeeder mark investigation
- `RimWorld/SanguophageUtility.cs` - Where mark is applied
- `Verse/HediffComp_Disappears.cs` - How time-limited hediffs work
- `RimWorld/HediffDefOf.cs` - Hediff definitions

## Testing the Mod

1. Build the solution
2. Copy or symlink `Eternal/` folder to RimWorld's `Mods/` directory (or use `Mod/` as a pre-packaged version)
3. Enable Harmony first, then Eternal in RimWorld's mod menu
4. Assign `Eternal_GeneticMarker` trait to a pawn via dev tools or character creation
