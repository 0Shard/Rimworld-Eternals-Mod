/*
 * Relative Path: Eternal/Source/Eternal/Utilities/ListPool.cs
 * Creation Date: 01-01-2026
 * Last Edit: 13-01-2026
 * Author: 0Shard
 * Description: Generic object pool for List<T> to eliminate allocations in hot paths.
 *              Used to avoid GC pressure during tick processing by reusing list instances.
 */

using System.Collections.Generic;

namespace Eternal.Utilities
{
    /// <summary>
    /// Thread-safe generic list pool to eliminate allocations in hot paths.
    /// Uses a simple stack-based pool with a configurable max size.
    /// </summary>
    /// <typeparam name="T">The element type of the lists to pool</typeparam>
    public static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>();
        private const int MAX_POOL_SIZE = 16;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets a list from the pool, or creates a new one if the pool is empty.
        /// The returned list is guaranteed to be empty.
        /// </summary>
        /// <returns>An empty list ready for use</returns>
        public static List<T> Get()
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    return _pool.Pop();
                }
            }
            return new List<T>();
        }

        /// <summary>
        /// Returns a list to the pool for reuse.
        /// The list will be cleared before being added to the pool.
        /// </summary>
        /// <param name="list">The list to return to the pool</param>
        public static void Return(List<T> list)
        {
            if (list == null) return;

            list.Clear();

            lock (_lock)
            {
                if (_pool.Count < MAX_POOL_SIZE)
                {
                    _pool.Push(list);
                }
                // If pool is full, let the list be garbage collected
            }
        }

        /// <summary>
        /// Clears all pooled lists. Useful for cleanup on game exit.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _pool.Clear();
            }
        }
    }
}
