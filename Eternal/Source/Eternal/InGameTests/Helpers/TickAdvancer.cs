// Relative Path: Eternal/Source/Eternal/InGameTests/Helpers/TickAdvancer.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Tick advancement helper for in-game integration tests.
//              Advances the game tick counter to trigger time-dependent processing
//              like corpse healing, regrowth, and resurrection.
//              RC3-FIX: After each DoSingleTick(), explicitly calls Eternal_Component.GameComponentUpdate()
//              because GameComponentUpdate() runs in the render/frame loop (Game.UpdatePlay()), NOT in
//              DoSingleTick() which only calls GameComponentTick(). Without this explicit call, the
//              TickOrchestrator (and thus all regrowth/healing processing) never fires during tests.

#if DEBUG

using System;
using Verse;
using Eternal.Components;

namespace Eternal.InGameTests.Helpers
{
    /// <summary>
    /// Advances game ticks programmatically for testing time-dependent mod behavior.
    /// </summary>
    public static class TickAdvancer
    {
        /// <summary>
        /// Default maximum ticks before timeout (~28 game-minutes at 60 ticks/sec).
        /// </summary>
        public const int DefaultMaxTicks = 100000;

        /// <summary>
        /// Advances the game by the specified number of ticks.
        /// Also explicitly drives Eternal_Component.GameComponentUpdate() after each tick
        /// because GameComponentUpdate() is a render-loop callback (not called by DoSingleTick).
        /// </summary>
        /// <param name="count">Number of ticks to advance.</param>
        public static void AdvanceTicks(int count)
        {
            var tickManager = Find.TickManager;
            if (tickManager == null)
            {
                Log.Error("[EternalTests] TickManager is null, cannot advance ticks");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    tickManager.DoSingleTick();
                    // RC3-FIX: Drive Eternal processing explicitly — GameComponentUpdate() is NOT called
                    // by DoSingleTick(). It lives in Game.UpdatePlay() (render/frame loop), so tests must
                    // invoke it manually to trigger TickOrchestrator -> regrowth/healing/resurrection.
                    Eternal_Component.Current?.GameComponentUpdate();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EternalTests] Tick {i} threw: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Advances ticks until the given condition is true, or throws on timeout.
        /// Also explicitly drives Eternal_Component.GameComponentUpdate() after each tick.
        /// </summary>
        /// <param name="condition">Condition to wait for.</param>
        /// <param name="maxTicks">Maximum ticks before timeout.</param>
        /// <param name="timeoutMessage">Message if timeout is reached.</param>
        /// <returns>Number of ticks elapsed.</returns>
        public static int AdvanceUntil(Func<bool> condition, int maxTicks = DefaultMaxTicks, string timeoutMessage = null)
        {
            var tickManager = Find.TickManager;
            if (tickManager == null)
                throw new TestFailedException("[EternalTests] TickManager is null");

            for (int i = 0; i < maxTicks; i++)
            {
                if (condition())
                    return i;

                try
                {
                    tickManager.DoSingleTick();
                    // RC3-FIX: Drive Eternal processing explicitly — same reason as AdvanceTicks.
                    Eternal_Component.Current?.GameComponentUpdate();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EternalTests] Tick {i} threw: {ex.Message}");
                }
            }

            throw new TestFailedException(
                timeoutMessage ?? $"Condition not met after {maxTicks} ticks");
        }
    }
}

#endif
