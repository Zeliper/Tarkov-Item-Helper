using System;
using System.IO;
using System.Linq;

namespace TarkovHelper.Services.Logging
{
    /// <summary>
    /// 메인 로깅 서비스
    /// 싱글톤 패턴, 세션 폴더 관리, 로그 레벨 설정
    /// </summary>
    public class LoggingService : IDisposable
    {
        private static LoggingService? _instance;
        private static readonly object _lock = new();

        public static LoggingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LoggingService();
                    }
                }
                return _instance;
            }
        }

        private readonly string _logDirectory;
        private readonly string _sessionFolder;
        private readonly FileLogWriter _fileWriter;
        private LogLevel _minimumLevel;
        private bool _enableConsoleOutput;
        private bool _disposed;

        public string SessionFolder => _sessionFolder;
        public string LogDirectory => _logDirectory;

        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        public bool EnableConsoleOutput
        {
            get => _enableConsoleOutput;
            set => _enableConsoleOutput = value;
        }

        private LoggingService()
        {
            // 로그 디렉토리: 실행 폴더/Logs
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logDirectory);

            // 세션 폴더 생성: 날짜-인스턴스번호
            _sessionFolder = CreateSessionFolder();
            _fileWriter = new FileLogWriter(_sessionFolder);

            // 빌드 모드에 따른 기본 설정
#if DEBUG
            _minimumLevel = LogLevel.Trace;
            _enableConsoleOutput = true;
#else
            _minimumLevel = LogLevel.Warning;
            _enableConsoleOutput = false;
#endif

            // 시작 로그
            Log(LogLevel.Info, "LoggingService", $"Logging initialized. Session: {Path.GetFileName(_sessionFolder)}, Level: {_minimumLevel}");
        }

        private string CreateSessionFolder()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var instanceNumber = 1;

            // 같은 날짜의 기존 폴더 확인
            var existingFolders = Directory.GetDirectories(_logDirectory, $"{today}-*")
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Select(name =>
                {
                    var parts = name!.Split('-');
                    if (parts.Length >= 4 && int.TryParse(parts[3], out var num))
                    {
                        return num;
                    }
                    return 0;
                })
                .ToList();

            if (existingFolders.Count > 0)
            {
                instanceNumber = existingFolders.Max() + 1;
            }

            var sessionFolderName = $"{today}-{instanceNumber:D3}";
            var sessionFolder = Path.Combine(_logDirectory, sessionFolderName);
            Directory.CreateDirectory(sessionFolder);

            return sessionFolder;
        }

        /// <summary>
        /// 설정에서 로그 레벨 로드 (Release 빌드용)
        /// SettingsService 초기화 후 호출
        /// </summary>
        public void LoadSettingsFromDb()
        {
#if !DEBUG
            try
            {
                var levelStr = SettingsService.Instance.GetValue("logging.level", ((int)LogLevel.Warning).ToString());
                if (int.TryParse(levelStr, out var level) && level >= 0 && level <= 6)
                {
                    _minimumLevel = (LogLevel)level;
                    Log(LogLevel.Info, "LoggingService", $"Loaded log level from settings: {_minimumLevel}");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "LoggingService", $"Failed to load log level from settings: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// 로그 레벨 설정 저장
        /// </summary>
        public void SaveLogLevel(LogLevel level)
        {
            _minimumLevel = level;
            try
            {
                SettingsService.Instance.SetValue("logging.level", ((int)level).ToString());
                Log(LogLevel.Info, "LoggingService", $"Log level saved: {level}");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "LoggingService", $"Failed to save log level: {ex.Message}");
            }
        }

        public void Log(LogLevel level, string category, string message, Exception? exception = null)
        {
            if (level < _minimumLevel || _disposed) return;

            var entry = new LogEntry(DateTime.Now, level, category, message, exception);

            // 파일에 쓰기
            _fileWriter.Enqueue(entry);

            // 콘솔 출력 (Debug 빌드 또는 설정 활성화 시)
            if (_enableConsoleOutput)
            {
                var consoleMessage = $"[{entry.Timestamp:HH:mm:ss.fff}] [{GetLevelString(level)}] [{category}] {message}";
                System.Diagnostics.Debug.WriteLine(consoleMessage);

                if (exception != null)
                {
                    System.Diagnostics.Debug.WriteLine($"    Exception: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        private static string GetLevelString(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT",
                _ => "UNKN"
            };
        }

        public ILogger CreateLogger<T>()
        {
            return new CategoryLogger(typeof(T).Name, this);
        }

        public ILogger CreateLogger(string category)
        {
            return new CategoryLogger(category, this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Log(LogLevel.Info, "LoggingService", "Logging service shutting down");
            _fileWriter.Dispose();
        }

        /// <summary>
        /// 카테고리별 로거 구현
        /// </summary>
        private class CategoryLogger : ILogger
        {
            private readonly string _category;
            private readonly LoggingService _service;

            public CategoryLogger(string category, LoggingService service)
            {
                _category = category;
                _service = service;
            }

            public void Trace(string message) => _service.Log(LogLevel.Trace, _category, message);
            public void Debug(string message) => _service.Log(LogLevel.Debug, _category, message);
            public void Info(string message) => _service.Log(LogLevel.Info, _category, message);
            public void Warning(string message) => _service.Log(LogLevel.Warning, _category, message);
            public void Error(string message, Exception? exception = null) => _service.Log(LogLevel.Error, _category, message, exception);
            public void Critical(string message, Exception? exception = null) => _service.Log(LogLevel.Critical, _category, message, exception);
            public void Log(LogLevel level, string message, Exception? exception = null) => _service.Log(level, _category, message, exception);
            public bool IsEnabled(LogLevel level) => level >= _service.MinimumLevel;
        }
    }
}
