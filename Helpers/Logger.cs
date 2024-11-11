namespace kpu.Helpers
{
    public static class Logger
    {
        public static void LogInfo(string message)
            => Console.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");

        public static void LogWarning(string message)
            => Console.WriteLine($"[WARN] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");

        public static void LogError(string message)
            => Console.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
    }
}