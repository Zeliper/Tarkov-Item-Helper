using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TarkovHelper.Services.Logging
{
    /// <summary>
    /// 파일 로그 쓰기 담당 클래스
    /// 비동기 배치 쓰기로 성능 최적화
    /// </summary>
    public class FileLogWriter : IDisposable
    {
        private readonly string _sessionFolder;
        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _writerTask;
        private readonly object _fileLock = new();
        private const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private const int MaxRotationFiles = 5;
        private const int FlushIntervalMs = 1000; // 1초마다 flush
        private bool _disposed;

        public FileLogWriter(string sessionFolder)
        {
            _sessionFolder = sessionFolder;
            Directory.CreateDirectory(_sessionFolder);
            _writerTask = Task.Run(ProcessLogQueueAsync);
        }

        public void Enqueue(LogEntry entry)
        {
            if (_disposed) return;
            _logQueue.Enqueue(entry);
        }

        private async Task ProcessLogQueueAsync()
        {
            var buffer = new StringBuilder();
            var levelBuffers = new ConcurrentDictionary<LogLevel, StringBuilder>();

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(FlushIntervalMs, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    // 종료 시 남은 로그 처리
                }

                // 큐에서 로그 수집
                buffer.Clear();
                levelBuffers.Clear();

                while (_logQueue.TryDequeue(out var entry))
                {
                    var formatted = FormatLogEntry(entry);
                    buffer.AppendLine(formatted);

                    if (!levelBuffers.ContainsKey(entry.Level))
                    {
                        levelBuffers[entry.Level] = new StringBuilder();
                    }
                    levelBuffers[entry.Level].AppendLine(formatted);
                }

                // 파일에 쓰기
                if (buffer.Length > 0)
                {
                    lock (_fileLock)
                    {
                        // all.log에 모든 로그 쓰기
                        WriteToFile("all.log", buffer.ToString());

                        // 레벨별 파일에 쓰기
                        foreach (var kvp in levelBuffers)
                        {
                            var fileName = GetLogFileName(kvp.Key);
                            WriteToFile(fileName, kvp.Value.ToString());
                        }
                    }
                }
            }
        }

        private string FormatLogEntry(LogEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            sb.Append($"[{GetLevelString(entry.Level)}] ");
            sb.Append($"[{entry.Category}] ");
            sb.Append(entry.Message);

            if (entry.Exception != null)
            {
                sb.AppendLine();
                sb.Append($"    Exception: {entry.Exception.GetType().FullName}: {entry.Exception.Message}");
                if (entry.Exception.StackTrace != null)
                {
                    sb.AppendLine();
                    foreach (var line in entry.Exception.StackTrace.Split('\n'))
                    {
                        sb.Append($"    {line.Trim()}");
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetLevelString(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT ",
                _ => "UNKN "
            };
        }

        private static string GetLogFileName(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "trace.log",
                LogLevel.Debug => "debug.log",
                LogLevel.Info => "info.log",
                LogLevel.Warning => "warning.log",
                LogLevel.Error => "error.log",
                LogLevel.Critical => "critical.log",
                _ => "other.log"
            };
        }

        private void WriteToFile(string fileName, string content)
        {
            try
            {
                var filePath = Path.Combine(_sessionFolder, fileName);

                // 파일 로테이션 확인
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length >= MaxFileSizeBytes)
                    {
                        RotateFile(filePath);
                    }
                }

                File.AppendAllText(filePath, content + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 로그 쓰기 실패 시 콘솔에만 출력 (앱 크래시 방지)
                System.Diagnostics.Debug.WriteLine($"[FileLogWriter] Failed to write log: {ex.Message}");
            }
        }

        private void RotateFile(string filePath)
        {
            try
            {
                // 기존 로테이션 파일 이동
                for (int i = MaxRotationFiles - 1; i >= 1; i--)
                {
                    var oldPath = $"{filePath}.{i}";
                    var newPath = $"{filePath}.{i + 1}";

                    if (File.Exists(oldPath))
                    {
                        if (i == MaxRotationFiles - 1)
                        {
                            File.Delete(oldPath);
                        }
                        else
                        {
                            File.Move(oldPath, newPath, true);
                        }
                    }
                }

                // 현재 파일을 .1로 이동
                File.Move(filePath, $"{filePath}.1", true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileLogWriter] Failed to rotate log file: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts.Cancel();

            try
            {
                // 남은 로그 처리를 위해 잠시 대기
                _writerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 무시
            }

            _cts.Dispose();
        }
    }

    /// <summary>
    /// 로그 엔트리 데이터 구조
    /// </summary>
    public record LogEntry(
        DateTime Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        Exception? Exception = null
    );
}
