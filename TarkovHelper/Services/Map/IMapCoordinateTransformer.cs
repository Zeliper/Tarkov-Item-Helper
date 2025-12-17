using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 월드 좌표를 화면 좌표로 변환하는 인터페이스
/// </summary>
public interface IMapCoordinateTransformer
{
    /// <summary>
    /// EFT 월드 좌표를 맵 이미지 픽셀 좌표로 변환합니다.
    /// </summary>
    /// <param name="worldPosition">월드 좌표</param>
    /// <param name="screenPosition">변환된 화면 좌표</param>
    /// <returns>변환 성공 여부 (해당 맵 설정이 없으면 false)</returns>
    bool TryTransform(EftPosition worldPosition, out ScreenPosition? screenPosition);

    /// <summary>
    /// 특정 맵에 대해 월드 좌표를 화면 좌표로 변환합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="worldX">월드 X 좌표</param>
    /// <param name="worldY">월드 Y 좌표</param>
    /// <param name="angle">방향 각도 (선택)</param>
    /// <param name="screenPosition">변환된 화면 좌표</param>
    /// <returns>변환 성공 여부</returns>
    bool TryTransform(string mapKey, double worldX, double worldY, double? angle, out ScreenPosition? screenPosition);

    /// <summary>
    /// 맵 설정을 업데이트합니다.
    /// </summary>
    /// <param name="maps">새로운 맵 설정 목록</param>
    void UpdateMaps(IEnumerable<MapConfig> maps);

    /// <summary>
    /// 특정 맵 설정을 가져옵니다.
    /// </summary>
    /// <param name="mapKey">맵 키 또는 별칭</param>
    /// <returns>맵 설정 (없으면 null)</returns>
    MapConfig? GetMapConfig(string mapKey);

    /// <summary>
    /// 모든 맵 키 목록을 반환합니다.
    /// </summary>
    IReadOnlyList<string> GetAllMapKeys();

    /// <summary>
    /// tarkov.dev API 좌표를 화면 좌표로 변환합니다.
    /// Transform 배열과 CoordinateRotation을 사용합니다.
    /// tarkov.dev 방식: pos(position) = [position.z, position.x]
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="apiX">API position.x 좌표</param>
    /// <param name="apiY">API position.y 좌표 (높이)</param>
    /// <param name="apiZ">API position.z 좌표</param>
    /// <param name="screenPosition">변환된 화면 좌표</param>
    /// <returns>변환 성공 여부</returns>
    bool TryTransformApiCoordinate(string mapKey, double apiX, double apiY, double? apiZ, out ScreenPosition? screenPosition);
}
