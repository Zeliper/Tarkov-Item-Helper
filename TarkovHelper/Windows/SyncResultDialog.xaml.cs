using System.Collections.ObjectModel;
using System.Windows;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Windows;

/// <summary>
/// Sync result dialog window.
/// Displays quest sync results and allows user to confirm or cancel changes.
/// </summary>
public partial class SyncResultDialog : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly SyncResult _result;
    private ObservableCollection<QuestChangeInfo> _pendingChanges;
    private List<AlternativeQuestGroupViewModel>? _alternativeGroups;

    /// <summary>
    /// Gets the selected changes to apply. Null if cancelled.
    /// </summary>
    public List<QuestChangeInfo>? SelectedChanges { get; private set; }

    /// <summary>
    /// Gets the count of alternative quest groups that were processed.
    /// </summary>
    public int AlternativeGroupCount => _alternativeGroups?.Count ?? 0;

    public SyncResultDialog(SyncResult result)
    {
        InitializeComponent();
        _result = result;
        _pendingChanges = new ObservableCollection<QuestChangeInfo>(result.QuestsToComplete);

        SetupUI();
        UpdateLocalizedText();
    }

    /// <summary>
    /// Show the sync result dialog and return selected changes.
    /// </summary>
    /// <param name="result">The sync result to display.</param>
    /// <param name="owner">Optional owner window for centering.</param>
    /// <returns>The selected changes to apply, or null if cancelled.</returns>
    public static List<QuestChangeInfo>? ShowResult(SyncResult result, Window? owner, out int alternativeCount)
    {
        var dialog = new SyncResultDialog(result);
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();
        alternativeCount = dialog.AlternativeGroupCount;
        return dialog.SelectedChanges;
    }

    private void SetupUI()
    {
        // Set completed quests list
        CompletedQuestList.ItemsSource = _pendingChanges;
        InProgressQuestList.ItemsSource = _result.InProgressQuests;

        // Handle alternative quest groups
        if (_result.AlternativeQuestGroups.Count > 0)
        {
            AlternativeQuestGroupViewModel.ResetCounter();
            _alternativeGroups = _result.AlternativeQuestGroups
                .Select(CreateAlternativeGroupViewModel)
                .ToList();

            AlternativeQuestsList.ItemsSource = _alternativeGroups;
            AlternativeQuestsSection.Visibility = Visibility.Visible;
        }
        else
        {
            _alternativeGroups = null;
            AlternativeQuestsSection.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateLocalizedText()
    {
        var prereqCount = _result.QuestsToComplete.Count(q => q.IsPrerequisite);
        var inProgressCount = _result.InProgressQuests.Count;

        TxtTitle.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 동기화 완료",
            AppLanguage.JA => "クエスト同期完了",
            _ => "Quest Sync Complete"
        };

        TxtCompletedHeader.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"완료된 퀘스트 ({_result.QuestsToComplete.Count})",
            AppLanguage.JA => $"完了したクエスト ({_result.QuestsToComplete.Count})",
            _ => $"Completed Quests ({_result.QuestsToComplete.Count})"
        };

        TxtInProgressHeader.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"진행중 퀘스트 ({inProgressCount})",
            AppLanguage.JA => $"進行中のクエスト ({inProgressCount})",
            _ => $"In Progress ({inProgressCount})"
        };

        TxtSummaryLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "요약:",
            AppLanguage.JA => "概要:",
            _ => "Summary:"
        };

        TxtStats.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"├─ 로그에서 발견된 이벤트: {_result.TotalEventsFound}\n├─ 자동 완료된 선행 퀘스트: {prereqCount}\n└─ 매칭 실패한 퀘스트 ID: {_result.UnmatchedQuestIds.Count}",
            AppLanguage.JA => $"├─ ログで見つかったイベント: {_result.TotalEventsFound}\n├─ 自動完了した前提クエスト: {prereqCount}\n└─ マッチング失敗したクエストID: {_result.UnmatchedQuestIds.Count}",
            _ => $"├─ Events found in logs: {_result.TotalEventsFound}\n├─ Prerequisites auto-completed: {prereqCount}\n└─ Unmatched quest IDs: {_result.UnmatchedQuestIds.Count}"
        };

        BtnCancel.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "취소",
            AppLanguage.JA => "キャンセル",
            _ => "Cancel"
        };

        BtnConfirm.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "확인",
            AppLanguage.JA => "確認",
            _ => "Confirm"
        };

        if (_result.AlternativeQuestGroups.Count > 0)
        {
            TxtAlternativeHeader.Text = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => $"선택 필요 퀘스트 - 그룹당 하나 선택 ({_result.AlternativeQuestGroups.Count}개 그룹)",
                AppLanguage.JA => $"選択が必要なクエスト - グループごとに1つ選択 ({_result.AlternativeQuestGroups.Count}グループ)",
                _ => $"Optional Quests - Choose One Per Group ({_result.AlternativeQuestGroups.Count} groups)"
            };
        }
    }

    private AlternativeQuestGroupViewModel CreateAlternativeGroupViewModel(AlternativeQuestGroup group)
    {
        var vm = new AlternativeQuestGroupViewModel
        {
            OriginalGroup = group,
            GroupLabel = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => $"선택 그룹: {string.Join(" / ", group.Choices.Select(c => c.Task.Name))}",
                AppLanguage.JA => $"選択グループ: {string.Join(" / ", group.Choices.Select(c => c.Task.Name))}",
                _ => $"Choose one: {string.Join(" / ", group.Choices.Select(c => c.Task.Name))}"
            }
        };

        foreach (var choice in group.Choices)
        {
            var choiceVm = new AlternativeQuestChoiceViewModel
            {
                GroupName = vm.GroupName,
                QuestName = choice.Task.Name,
                IsCompleted = choice.IsCompleted,
                IsFailed = choice.IsFailed,
                IsSelected = choice.IsSelected,
                OriginalChoice = choice
            };
            vm.Choices.Add(choiceVm);
        }

        // If none selected, select first enabled one
        if (!vm.Choices.Any(c => c.IsSelected) && vm.Choices.Any(c => c.IsEnabled))
        {
            vm.Choices.First(c => c.IsEnabled).IsSelected = true;
        }

        return vm;
    }

    private List<QuestChangeInfo> BuildSelectedChanges()
    {
        var selectedChanges = _pendingChanges.Where(c => c.IsSelected).ToList();

        // Add selected alternative quests to the changes list
        if (_alternativeGroups != null)
        {
            foreach (var group in _alternativeGroups)
            {
                var selectedChoice = group.Choices.FirstOrDefault(c => c.IsSelected && c.IsEnabled);
                if (selectedChoice != null)
                {
                    var task = selectedChoice.OriginalChoice.Task;
                    selectedChanges.Add(new QuestChangeInfo
                    {
                        QuestName = task.Name,
                        NormalizedName = task.NormalizedName ?? "",
                        Trader = task.Trader,
                        IsPrerequisite = true,
                        ChangeType = QuestEventType.Completed,
                        IsSelected = true,
                        Timestamp = DateTime.Now
                    });

                    // Fail the other alternatives
                    foreach (var otherChoice in group.Choices.Where(c => c != selectedChoice && !c.IsCompleted))
                    {
                        var otherTask = otherChoice.OriginalChoice.Task;
                        selectedChanges.Add(new QuestChangeInfo
                        {
                            QuestName = otherTask.Name,
                            NormalizedName = otherTask.NormalizedName ?? "",
                            Trader = otherTask.Trader,
                            IsPrerequisite = true,
                            ChangeType = QuestEventType.Failed,
                            IsSelected = true,
                            Timestamp = DateTime.Now
                        });
                    }
                }
            }
        }

        return selectedChanges;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        SelectedChanges = null;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedChanges = null;
        Close();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedChanges = BuildSelectedChanges();
        Close();
    }
}
