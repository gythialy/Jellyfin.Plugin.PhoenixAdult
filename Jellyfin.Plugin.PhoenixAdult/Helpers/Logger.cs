using Microsoft.Extensions.Logging;

namespace PhoenixAdult.Helpers
{
    internal static class Logger
    {
        private static ILogger Log { get; } = Plugin.Log;

        public static void Info(string text)
        {
            Log?.LogInformation(text);
        }

        public static void Error(string text)
        {
            Log?.LogError(text);
        }

        public static void Debug(string text)
        {
            Log?.LogDebug(text);
        }

        public static void Warning(string text)
        {
            Log?.LogWarning(text);
        }
    }
}
