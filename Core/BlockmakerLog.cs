using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Blockmaker
{

    /// <summary>
    /// Centralized logging for the Blockmaker SDK.
    /// All SDK log output goes through here so game developers can control verbosity.
    ///
    /// Default behavior:
    ///   - Editor / Development builds: all levels enabled
    ///   - Release builds: only warnings and errors
    ///
    /// To silence SDK logs entirely:
    ///   BlockmakerLog.Level = BlockmakerLogLevel.None;
    ///
    /// To get full diagnostics:
    ///   BlockmakerLog.Level = BlockmakerLogLevel.Verbose;
    ///
    /// To intercept logs (e.g. send to your own analytics):
    ///   BlockmakerLog.OnLog += (level, msg) => MyAnalytics.Track(msg);
    /// </summary>
    public static class BlockmakerLog
    {
        public static BlockmakerLogLevel Level { get; set; } =
            Debug.isDebugBuild ? BlockmakerLogLevel.Verbose : BlockmakerLogLevel.Warning;

        /// <summary>
        /// Optional hook for game code to intercept all SDK log messages.
        /// Fires regardless of Level — filtering is the subscriber's responsibility.
        /// </summary>
        public static event Action<BlockmakerLogLevel, string> OnLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Level = Debug.isDebugBuild ? BlockmakerLogLevel.Verbose : BlockmakerLogLevel.Warning;
            OnLog = null;
        }

        public static void Verbose(string message)
        {
            OnLog?.Invoke(BlockmakerLogLevel.Verbose, message);
            if (Level <= BlockmakerLogLevel.Verbose)
                Debug.Log(message);
        }

        public static void Info(string message)
        {
            OnLog?.Invoke(BlockmakerLogLevel.Info, message);
            if (Level <= BlockmakerLogLevel.Info)
                Debug.Log(message);
        }

        public static void Warning(string message)
        {
            OnLog?.Invoke(BlockmakerLogLevel.Warning, message);
            if (Level <= BlockmakerLogLevel.Warning)
                Debug.LogWarning(message);
        }

        public static void Error(string message)
        {
            OnLog?.Invoke(BlockmakerLogLevel.Error, message);
            if (Level <= BlockmakerLogLevel.Error)
                Debug.LogError(message);
        }

        public static void Exception(Exception ex)
        {
            OnLog?.Invoke(BlockmakerLogLevel.Error, ex.ToString());
            if (Level <= BlockmakerLogLevel.Error)
                Debug.LogException(ex);
        }
    }

    public enum BlockmakerLogLevel
    {
        Verbose = 0,
        Info    = 1,
        Warning = 2,
        Error   = 3,
        None    = 4
    }

}