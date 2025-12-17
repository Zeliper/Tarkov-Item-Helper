namespace TarkovHelper.Services.Logging
{
    /// <summary>
    /// 로그 레벨 정의
    /// </summary>
    public enum LogLevel
    {
        /// <summary>매우 상세한 디버깅 정보 (메서드 진입/종료, 변수 값)</summary>
        Trace = 0,

        /// <summary>디버깅용 정보 (DB 쿼리, 상태 변경)</summary>
        Debug = 1,

        /// <summary>일반 정보성 메시지 (앱 시작, 페이지 전환)</summary>
        Info = 2,

        /// <summary>잠재적 문제 (느린 응답, 재시도)</summary>
        Warning = 3,

        /// <summary>오류 발생 (예외, 실패)</summary>
        Error = 4,

        /// <summary>치명적 오류 (앱 크래시, 데이터 손상)</summary>
        Critical = 5,

        /// <summary>로깅 비활성화</summary>
        None = 6
    }
}
