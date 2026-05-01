using System;
using System.Collections.Generic;
using System.IO;

namespace Zenject2VContainer.Core {
    public enum LogLevel { Info, Warn, Error }

    public sealed class MigrationLogEntry {
        public DateTime UtcTimestamp;
        public LogLevel Level;
        public string Source;       // "Scan", "Apply.CSharp", "Apply.Yaml", "Verify", "Remove"
        public string Message;
        public string FilePath;     // optional
        public override string ToString() {
            var f = string.IsNullOrEmpty(FilePath) ? "" : $" [{FilePath}]";
            return $"{UtcTimestamp:O} {Level} {Source}{f}: {Message}";
        }
    }

    public sealed class MigrationLog {
        public List<MigrationLogEntry> Entries = new List<MigrationLogEntry>();

        public void Info(string source, string message, string filePath = null)  => Add(LogLevel.Info, source, message, filePath);
        public void Warn(string source, string message, string filePath = null)  => Add(LogLevel.Warn, source, message, filePath);
        public void Error(string source, string message, string filePath = null) => Add(LogLevel.Error, source, message, filePath);

        private void Add(LogLevel level, string source, string message, string filePath) {
            Entries.Add(new MigrationLogEntry {
                UtcTimestamp = DateTime.UtcNow,
                Level = level,
                Source = source,
                Message = message,
                FilePath = filePath
            });
        }

        public void WriteTo(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var w = new StreamWriter(path, append: false);
            foreach (var e in Entries) w.WriteLine(e.ToString());
        }
    }
}
