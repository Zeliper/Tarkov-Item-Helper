using System.Windows;

namespace TarkovHelper.Debug;

/// <summary>
/// Debug 모드에서 Toolbox 창에 표시될 테스트 함수들을 정의합니다.
/// [TestMenu] 어트리뷰트가 붙은 public 메서드가 버튼으로 표시됩니다.
/// </summary>
public static class TestMenu
{
    /// <summary>
    /// MainWindow 인스턴스 (Toolbox에서 주입)
    /// </summary>
    public static Window? MainWindow { get; set; }

    // All test functions removed since they were for wiki/JSON parsing
    // which is no longer used (data now comes from DB)
}
