"""Static smoke checks for native WinUI details layout and notifications routing.

This does not replace testing the application on Windows. It guards against
a known risky pattern: changing the visual tree synchronously in SizeChanged.
"""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
VIEWS = ROOT / "src" / "HDREZKA.App" / "Views"
XAML_NAME = "{http://schemas.microsoft.com/winfx/2006/xaml}Name"


def contents(name):
    return (VIEWS / name).read_text(encoding="utf-8-sig")


class DetailsLayoutSmokeTests(unittest.TestCase):
    def test_xaml_well_formed_and_handlers_exist(self):
        for name in ("DetailsPage", "SettingsPage", "UpdatesPage"):
            markup = contents(name + ".xaml")
            code = contents(name + ".xaml.cs")
            root = ET.fromstring(markup)
            names = [node.attrib[XAML_NAME] for node in root.iter()
                     if XAML_NAME in node.attrib]
            self.assertEqual(len(names), len(set(names)), name)
            for handler in re.findall(
                    r'\b(?:Click|SizeChanged|Toggled|SelectionChanged|ItemClick)'
                    r'="([A-Za-z_]\w*)"', markup):
                self.assertIn(handler + "(", code, (name, handler))

    def test_recommendations_are_paged_and_not_mutated_in_layout(self):
        markup = contents("DetailsPage.xaml")
        code = contents("DetailsPage.xaml.cs")
        self.assertIn('x:Name="WatchAlsoGrid"', markup)
        self.assertIn('ItemsStackPanel Orientation="Horizontal"', markup)
        self.assertIn('ScrollViewer.HorizontalScrollMode="Disabled"', markup)
        self.assertIn('QueueWatchAlsoLayout()', code)
        self.assertIn('WatchAlsoGrid.ItemsSource = visible;', code)
        self.assertIn('pageSize = contentWidth >= 780 ? 5', code)
        self.assertIn('x:Name="WatchAlsoPreviousButton" Grid.Column="0"', markup)
        self.assertIn('x:Name="WatchAlsoNextButton" Grid.Column="2"', markup)
        self.assertIn('RecommendationPreview="True"', markup)
        self.assertNotIn("WatchAlsoCards.Children.Clear()", code)
        self.assertNotIn("WatchAlsoCards.ColumnDefinitions.Clear()", code)
        self.assertIn('Log.Error("[Details] Render failed', code)

    def test_episode_right_click_does_not_recreate_focused_buttons(self):
        code = contents("DetailsPage.xaml.cs")
        handler = code.split("private void EpisodeButton_RightTapped", 1)[1]
        handler = handler.split("private void EpisodeButton_Click", 1)[0]
        self.assertNotIn("RenderEpisodes()", handler)
        self.assertIn("button.Content = nowWatched", handler)
        self.assertIn("DetailsScrollViewer.VerticalOffset", handler)

    def test_japan_badge_is_white_and_cast_heading_is_top_aligned(self):
        code = contents("DetailsPage.xaml.cs")
        self.assertIn('flag == "🇯🇵" ? Brush(33, 37, 44)', code)
        self.assertIn('Windows.UI.Color.FromArgb(255, 247, 247, 249)', code)
        self.assertIn('AddMetaRow(label, wrap, alignLabelTop: isCast)', code)

    def test_recommendation_tooltip_uses_lazy_data(self):
        card = contents("../Controls/MovieCard.xaml.cs")
        self.assertIn("RecommendationPreviewProperty", card)
        self.assertIn("FlyoutPlacementMode.Auto", card)
        self.assertIn("Task.Delay(180, cts.Token)", card)
        self.assertIn("Task.Delay(120, cts.Token)", card)
        self.assertNotIn("ToolTipService.SetToolTip(CardButton", card)
        self.assertIn("PreviewCache.Count >= 18", card)
        self.assertIn("GetDetailsAsync(movie.Id, cts.Token)", card)

    def test_inline_schedule_has_no_scroll_dialog(self):
        markup = contents("DetailsPage.xaml")
        code = contents("DetailsPage.xaml.cs")
        self.assertIn('x:Name="ScheduleCountText"', markup)
        self.assertIn('x:Name="ScheduleList"', markup)
        self.assertIn("private void ScheduleMoreButton_Click", code)
        handler = code.split("private void ScheduleMoreButton_Click", 1)[1]
        handler = handler.split("private async void VoicesRatingButton_Click", 1)[0]
        self.assertIn("_scheduleExpanded = !_scheduleExpanded", handler)
        self.assertNotIn("new ContentDialog", handler)
        self.assertNotIn("new ScrollViewer", handler)

    def test_comment_sort_editor_and_thread_preservation(self):
        markup = contents("DetailsPage.xaml")
        code = contents("DetailsPage.xaml.cs")
        for name in ("CommentsSortBox", "CommentInput", "CommentSendButton",
                     "CommentReplyRow", "CommentCharCount", "CommentStatusText"):
            self.assertIn('x:Name="' + name + '"', markup)
        self.assertIn('AcceptsReturn="True"', markup)
        self.assertIn("CommentsSortBox_SelectionChanged", code)
        self.assertIn("ThenBy(x => x.index)", code)
        self.assertIn("comment.Replies", code)
        self.assertIn("UpdateCommentComposer()", code)
        self.assertIn("CommentInput.SelectedText = emoji;", code)

    def test_notifications_preferences_only_on_notifications_page(self):
        settings = contents("SettingsPage.xaml")
        settings_code = contents("SettingsPage.xaml.cs")
        updates = contents("UpdatesPage.xaml")
        updates_code = contents("UpdatesPage.xaml.cs")
        self.assertNotIn('x:Name="SeriesExpander"', settings)
        self.assertNotIn("SeriesToggle_Toggled", settings_code)
        for token in ('x:Name="SeriesExpander"', 'x:Name="SeriesToggle"',
                      'x:Name="CheckSeriesButton"'):
            self.assertIn(token, updates)
        self.assertIn("SettingsService.Instance.SeriesUpdatesEnabled =", updates_code)
        self.assertIn("TrackedSeriesService.CheckForUpdatesAsync", updates_code)
        self.assertIn("SettingsService.Instance.Save();", updates_code)


if __name__ == "__main__":
    unittest.main()
