using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Masar.Diagnostics
{
    /// <summary>
    /// Keeps the most recent log lines in memory from the moment the game boots, so a
    /// diagnostics report contains the context of a problem. It also detects whether the
    /// previous session ended without a clean quit (crash, or killed for memory or heat).
    /// </summary>
    public static class LogBuffer
    {
        public struct Line
        {
            public float Time;
            public LogType Type;
            public string Text;
        }

        private const int Capacity = 500;
        private static readonly Queue<Line> s_Lines = new Queue<Line>(Capacity);
        private static readonly object s_Lock = new object();
        private static int s_Errors, s_Exceptions, s_Warnings;

        public static int ErrorCount => s_Errors;
        public static int ExceptionCount => s_Exceptions;
        public static int WarningCount => s_Warnings;
        /// <summary>True if the previous session didn't reach a clean quit.</summary>
        public static bool PreviousSessionUnclean { get; private set; }
        public static DateTime SessionStartUtc { get; private set; }

        private static string MarkerPath => Path.Combine(Application.persistentDataPath, "session.running");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            SessionStartUtc = DateTime.UtcNow;
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            try
            {
                PreviousSessionUnclean = File.Exists(MarkerPath);
                File.WriteAllText(MarkerPath, SessionStartUtc.ToString("o"));
            }
            catch { /* storage unavailable: report as unknown (false) */ }
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        private static void OnQuit()
        {
            try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            string text = condition;
            if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
                text += "\n" + (stackTrace.Length > 1200 ? stackTrace.Substring(0, 1200) : stackTrace);
            if (text.Length > 2000) text = text.Substring(0, 2000);
            lock (s_Lock)
            {
                if (type == LogType.Error || type == LogType.Assert) s_Errors++;
                else if (type == LogType.Exception) s_Exceptions++;
                else if (type == LogType.Warning) s_Warnings++;
                if (s_Lines.Count >= Capacity) s_Lines.Dequeue();
                s_Lines.Enqueue(new Line { Time = (float)(DateTime.UtcNow - SessionStartUtc).TotalSeconds, Type = type, Text = text });
            }
        }

        public static Line[] Snapshot()
        {
            lock (s_Lock) return s_Lines.ToArray();
        }
    }
}
