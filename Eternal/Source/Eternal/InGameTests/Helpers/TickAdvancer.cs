// Relative Path: Eternal/Source/Eternal/InGameTests/Helpers/TickAdvancer.cs
// Author: 0Shard
// Description: Fail-closed real tick advancement with time-speed restoration.
#if DEBUG
using System;
using Verse;

namespace Eternal.InGameTests.Helpers
{
    public static class TickAdvancer
    {
        public const int DefaultMaxTicks = 100000;

        public static void AdvanceTicks(int count)
        {
            Advance(count, null, null, null);
        }

        public static int AdvanceUntil(Func<bool> condition, int maxTicks = DefaultMaxTicks, string timeoutMessage = null)
        {
            if (condition == null)
            {
                throw new TestFailedException("Tick condition is required");
            }

            return Advance(maxTicks, condition, timeoutMessage, null);
        }

        // This seam exercises failure/cleanup in the host suite; normal callers drive real ticks.
        internal static int AdvanceForTest(int count, Func<bool> condition, Action step)
        {
            if (step == null)
            {
                throw new TestFailedException("Injected tick step is required");
            }

            return Advance(count, condition, null, step);
        }

        private static int Advance(int count, Func<bool> condition, string timeoutMessage, Action injectedStep)
        {
            if (count < 0)
            {
                throw new TestFailedException("Tick count must be nonnegative");
            }

            var manager = Find.TickManager;
            var component = Eternal_Component.Current;
            TestAssert.IsNotNull(manager, "TickManager is required for tick advancement");
            TestAssert.IsNotNull(component, "Eternal component is required for tick advancement");
            var savedSpeed = manager.CurTimeSpeed;
            try
            {
                manager.CurTimeSpeed = TimeSpeed.Normal;
                for (int index = 0; index <= count; index++)
                {
                    try
                    {
                        if (condition != null && condition())
                        {
                            return index;
                        }

                        if (index == count)
                        {
                            break;
                        }

                        if (injectedStep != null)
                        {
                            injectedStep();
                        }
                        else
                        {
                            manager.DoSingleTick();
                            // Eternal's orchestrator runs in the render callback, outside DoSingleTick.
                            component.GameComponentUpdate();
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new TestFailedException($"Tick advancement failed at index {index}: {ex}");
                    }
                }
                if (condition != null)
                {
                    throw new TestFailedException(timeoutMessage ?? $"Condition not met after {count} ticks");
                }

                return count;
            }
            finally
            {
                manager.CurTimeSpeed = savedSpeed;
            }
        }
    }
}
#endif
