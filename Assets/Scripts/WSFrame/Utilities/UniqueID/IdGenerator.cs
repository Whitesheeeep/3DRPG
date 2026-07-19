using System;
using System.Threading;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// Runtime unique id generator. IDs are unique during the current app run.
    /// </summary>
    public static class IdGenerator
    {
        public const long InvalidId = -1;

        private static long _nextId;

        /// <summary>
        /// Gets the next unique id. The first generated id is 1.
        /// </summary>
        public static long Next()
        {
            var id = Interlocked.Increment(ref _nextId);

            if (id == long.MaxValue)
            {
                throw new InvalidOperationException("IdGenerator has reached long.MaxValue.");
            }

            return id;
        }
    }
}
