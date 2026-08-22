using System;
using System.IO;

namespace XoCrazy.Core
{
    /// <summary>
    /// Append-only trace to %TEMP%\xocrazy.log.
    ///
    /// The apply path crosses three subsystems that all fail quietly: storage writes return an
    /// HRESULT nobody reads, MEF service lookups return null, and format map sets on an unknown
    /// key are no-ops. Without a trace, "nothing happened" is indistinguishable at every step.
    /// </summary>
    internal static class Diag
    {
        private static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "xocrazy.log");

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never be the reason something fails.
            }
        }
    }
}
