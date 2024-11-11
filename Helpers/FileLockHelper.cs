namespace KpuRegion.Helpers
{
    public static class FileLockHelper
    {
        private static readonly Dictionary<string, SemaphoreSlim> _fileLocks = [];
        private static readonly object _lockObject = new();

        public static SemaphoreSlim GetLock(string filename)
        {
            lock (_lockObject)
            {
                if (!_fileLocks.TryGetValue(filename, out var semaphore))
                {
                    semaphore = new SemaphoreSlim(1, 1);
                    _fileLocks[filename] = semaphore;
                }
                return semaphore;
            }
        }
    }
}