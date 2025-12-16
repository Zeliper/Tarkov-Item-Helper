using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TarkovDBEditor.Views;

/// <summary>
/// 다중 맵 퀘스트에서 맵을 선택하는 다이얼로그
/// </summary>
public partial class SelectMapDialog : Window
{
    public string? SelectedMap { get; private set; }

    /// <summary>
    /// 다중 맵 선택 다이얼로그 생성
    /// </summary>
    /// <param name="questLocation">퀘스트의 전체 Location 문자열 (예: "Shoreline, Interchange")</param>
    /// <param name="headerText">헤더 텍스트 (선택적)</param>
    public SelectMapDialog(string questLocation, string? headerText = null)
    {
        InitializeComponent();

        QuestLocationText.Text = questLocation;

        if (!string.IsNullOrEmpty(headerText))
            HeaderText.Text = headerText;

        // 쉼표로 구분된 맵 목록 파싱
        var maps = ParseMultiMapLocation(questLocation);
        MapsList.ItemsSource = maps;

        // 첫 번째 항목 선택
        if (maps.Count > 0)
            MapsList.SelectedIndex = 0;
    }

    /// <summary>
    /// 쉼표로 구분된 Location 문자열에서 맵 목록 추출
    /// </summary>
    public static List<string> ParseMultiMapLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return new List<string>();

        return location
            .Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .ToList();
    }

    /// <summary>
    /// Location이 다중 맵인지 확인
    /// </summary>
    public static bool IsMultiMapLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return false;

        return location.Contains(',') || location.Contains('/');
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (MapsList.SelectedItem is string selected)
        {
            SelectedMap = selected;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Please select a map.", "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
