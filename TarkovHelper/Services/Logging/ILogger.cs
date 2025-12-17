using System;

namespace TarkovHelper.Services.Logging
{
    /// <summary>
    /// 로거 인터페이스
    /// </summary>
    public interface ILogger
    {
        /// <summary>매우 상세한 디버깅 정보</summary>
        void Trace(string message);

        /// <summary>디버깅용 정보</summary>
        void Debug(string message);

        /// <summary>일반 정보성 메시지</summary>
        void Info(string message);

        /// <summary>잠재적 문제</summary>
        void Warning(string message);

        /// <summary>오류 발생</summary>
        void Error(string message, Exception? exception = null);

        /// <summary>치명적 오류</summary>
        void Critical(string message, Exception? exception = null);

        /// <summary>지정된 레벨로 로그 출력</summary>
        void Log(LogLevel level, string message, Exception? exception = null);

        /// <summary>해당 레벨이 활성화되어 있는지 확인</summary>
        bool IsEnabled(LogLevel level);
    }
}
