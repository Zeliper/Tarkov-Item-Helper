using System;

namespace TarkovHelper.Services.Logging
{
    /// <summary>
    /// 로거 팩토리 - 간편한 로거 생성
    /// </summary>
    /// <example>
    /// private static readonly ILogger _log = Log.For&lt;MyClass&gt;();
    /// _log.Info("Hello World");
    /// </example>
    public static class Log
    {
        /// <summary>
        /// 타입 기반 로거 생성
        /// </summary>
        public static ILogger For<T>()
        {
            return LoggingService.Instance.CreateLogger<T>();
        }

        /// <summary>
        /// 카테고리명 기반 로거 생성
        /// </summary>
        public static ILogger For(string category)
        {
            return LoggingService.Instance.CreateLogger(category);
        }

        // 빠른 접근을 위한 정적 메서드들

        /// <summary>Trace 레벨 로그</summary>
        public static void Trace(string category, string message)
            => LoggingService.Instance.Log(LogLevel.Trace, category, message);

        /// <summary>Debug 레벨 로그</summary>
        public static void Debug(string category, string message)
            => LoggingService.Instance.Log(LogLevel.Debug, category, message);

        /// <summary>Info 레벨 로그</summary>
        public static void Info(string category, string message)
            => LoggingService.Instance.Log(LogLevel.Info, category, message);

        /// <summary>Warning 레벨 로그</summary>
        public static void Warning(string category, string message)
            => LoggingService.Instance.Log(LogLevel.Warning, category, message);

        /// <summary>Error 레벨 로그</summary>
        public static void Error(string category, string message, Exception? exception = null)
            => LoggingService.Instance.Log(LogLevel.Error, category, message, exception);

        /// <summary>Critical 레벨 로그</summary>
        public static void Critical(string category, string message, Exception? exception = null)
            => LoggingService.Instance.Log(LogLevel.Critical, category, message, exception);
    }
}
