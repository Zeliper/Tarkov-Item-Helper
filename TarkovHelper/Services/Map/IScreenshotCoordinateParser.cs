using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 스크린샷 파일명에서 좌표를 파싱하는 인터페이스
/// </summary>
public interface IScreenshotCoordinateParser
{
    /// <summary>
    /// 파일명에서 EFT 좌표를 파싱합니다.
    /// </summary>
    /// <param name="fileName">스크린샷 파일명 (경로 제외)</param>
    /// <param name="position">파싱 성공 시 좌표 정보</param>
    /// <returns>파싱 성공 여부</returns>
    bool TryParse(string fileName, out EftPosition? position);

    /// <summary>
    /// 현재 사용 중인 정규식 패턴을 반환합니다.
    /// </summary>
    string CurrentPattern { get; }

    /// <summary>
    /// 정규식 패턴을 업데이트합니다.
    /// </summary>
    /// <param name="pattern">새로운 정규식 패턴</param>
    /// <returns>패턴 유효성 여부</returns>
    bool UpdatePattern(string pattern);
}
