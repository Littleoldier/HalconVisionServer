using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HalconVisionServer.Utils
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    internal sealed class LogEntry
    {
        public LogLevel Level { get; }
        public string Line { get; }
        public string? Category { get; }

        public LogEntry(LogLevel level, string line, string? category)
        {
            Level = level;
            Line = line;
            Category = category;
        }
    }

    /// <summary>
    /// 日志管理器
    /// </summary>
    public static class AppLogger
    {
        public static LogLevel GlobalLogLevel { get; set; } = LogLevel.Info;
        public static int MaxFileSizeMB { get; set; } = 10;
        public static int RetentionDays { get; set; } = 30;
        public static string LogDirectory { get; private set; } = "";

        private static readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );
        private static CancellationTokenSource? _writerCts;
        private static Task? _writerTask;
        private static readonly Encoding _encoding = new UTF8Encoding(false);
        private static StreamWriter? _writer;
        private static string _currentLogFile = "";
        private static long _currentFileBytes = 0;
        private static readonly object _fileLock = new();
        private static bool _initialized = false;

        public static void Initialize(string logDirectory = "Logs")
        {
            if (_initialized) return;
            lock (_fileLock)
            {
                if (_initialized) return;
                LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDirectory);
                if (!Directory.Exists(LogDirectory))
                    Directory.CreateDirectory(LogDirectory);

                CleanupOldLogs();
                PrepareLogFile();

                _writerCts = new CancellationTokenSource();
                _writerTask = Task.Run(() => LogWriterLoopAsync(_writerCts.Token));
                _initialized = true;
            }
        }

        // ============================================================
        // ⭐ 日志方法（支持单参数和双参数）
        // ============================================================

        public static void Log(string message, LogLevel level = LogLevel.Info, string? category = null)
        {
            if (level < GlobalLogLevel) return;

            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper();
            string categoryTag = string.IsNullOrEmpty(category) ? "" : $"[{category}] ";
            string logLine = $"[{timeStamp}] [{levelStr}] {categoryTag}{message}";

            _channel.Writer.TryWrite(new LogEntry(level, logLine, category));
        }

        // ---- Info ----
        public static void Info(string msg) => Log(msg, LogLevel.Info);
        public static void Info(string msg, string category) => Log(msg, LogLevel.Info, category);

        // ---- Warn ----
        public static void Warn(string msg) => Log(msg, LogLevel.Warn);
        public static void Warn(string msg, string category) => Log(msg, LogLevel.Warn, category);

        // ---- Error ----
        public static void Error(string msg) => Log(msg, LogLevel.Error);
        public static void Error(string msg, string category) => Log(msg, LogLevel.Error, category);

        // ---- Debug ----
        public static void Debug(string msg) => Log(msg, LogLevel.Debug);
        public static void Debug(string msg, string category) => Log(msg, LogLevel.Debug, category);

        // ---- Fatal ----
        public static void Fatal(string msg) => Log(msg, LogLevel.Fatal);
        public static void Fatal(string msg, string category) => Log(msg, LogLevel.Fatal, category);

        // ---- Trace ----
        public static void Trace(string msg) => Log(msg, LogLevel.Trace);
        public static void Trace(string msg, string category) => Log(msg, LogLevel.Trace, category);

        // ============================================================
        // 内部方法
        // ============================================================

        private static async Task LogWriterLoopAsync(CancellationToken ct)
        {
            try
            {
                lock (_fileLock)
                {
                    var fs = new FileStream(_currentLogFile, FileMode.Append, FileAccess.Write, FileShare.Read);
                    _writer = new StreamWriter(fs, _encoding) { AutoFlush = true };
                    _currentFileBytes = fs.Length;
                }

                await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        var line = entry.Line;
                        var bytesLength = _encoding.GetByteCount(line + Environment.NewLine);

                        if (_currentFileBytes + bytesLength > MaxFileSizeMB * 1024L * 1024L)
                        {
                            RolloverLogFile();
                        }

                        await _writer!.WriteLineAsync(line).ConfigureAwait(false);
                        _currentFileBytes += bytesLength;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"日志写入异常: {ex}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"日志任务异常: {ex}");
            }
            finally
            {
                try { _writer?.Dispose(); } catch { }
            }
        }

        private static void PrepareLogFile()
        {
            DateTime today = DateTime.Today;
            string dateStr = today.ToString("yyyy-MM-dd");
            _currentLogFile = Path.Combine(LogDirectory, $"VisionLog_{dateStr}.txt");
            if (!File.Exists(_currentLogFile))
            {
                try { File.WriteAllText(_currentLogFile, ""); } catch { }
                _currentFileBytes = 0;
            }
            else
            {
                try { _currentFileBytes = new FileInfo(_currentLogFile).Length; } catch { _currentFileBytes = 0; }
            }
        }

        private static void RolloverLogFile()
        {
            lock (_fileLock)
            {
                try { _writer?.Dispose(); } catch { }

                string baseName = Path.GetFileNameWithoutExtension(_currentLogFile);
                string dir = Path.GetDirectoryName(_currentLogFile) ?? LogDirectory;
                int index = 1;
                string newFile;
                do
                {
                    newFile = Path.Combine(dir, $"{baseName}_{index:D3}.txt");
                    index++;
                } while (File.Exists(newFile));

                try
                {
                    File.Move(_currentLogFile, newFile);
                }
                catch
                {
                    try
                    {
                        var alt = Path.Combine(dir, $"{baseName}_{DateTime.Now:HHmmss}.txt");
                        File.Move(_currentLogFile, alt);
                    }
                    catch { }
                }

                try
                {
                    var fs = new FileStream(_currentLogFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                    _writer = new StreamWriter(fs, _encoding) { AutoFlush = true };
                    _currentFileBytes = 0;
                }
                catch
                {
                    _writer = null;
                    _currentFileBytes = 0;
                }
            }
        }

        private static void CleanupOldLogs()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-RetentionDays);
                var files = Directory.GetFiles(LogDirectory, "VisionLog_*.txt", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        if (File.GetCreationTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void Shutdown(int waitMs = 5000)
        {
            if (!_initialized) return;
            try
            {
                _channel.Writer.Complete();
                try { _writerCts?.Cancel(); } catch { }
                if (_writerTask != null)
                {
                    try { _writerTask.Wait(waitMs); } catch { }
                }
            }
            catch { }
            finally
            {
                try { _writer?.Dispose(); } catch { }
                try { _writerCts?.Dispose(); } catch { }
                _initialized = false;
            }
        }
    }
}