namespace TarkovHelper.Debug;

/// <summary>
/// TestMenu 클래스의 메서드에 적용하여 Toolbox 창에 버튼으로 표시되도록 합니다.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class TestMenuAttribute : Attribute
{
    /// <summary>
    /// 버튼에 표시될 이름 (null이면 메서드 이름 사용)
    /// </summary>
    public string? DisplayName { get; set; }

    public TestMenuAttribute() { }

    public TestMenuAttribute(string displayName)
    {
        DisplayName = displayName;
    }
}
