using System.Windows;
using System.Windows.Controls;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// 퀘스트 드로어용 DataTemplateSelector
/// </summary>
public class QuestDrawerTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupHeaderTemplate { get; set; }
    public DataTemplate? ObjectiveTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is QuestGroupHeader)
            return GroupHeaderTemplate;
        if (item is QuestObjectiveViewModel)
            return ObjectiveTemplate;
        return base.SelectTemplate(item, container);
    }
}
