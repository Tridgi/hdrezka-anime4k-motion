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
        self.assertIn('pageSize = available >= 590 ? 3', code)
        self.assertNotIn("WatchAlsoCards.Children.Clear()", code)
        self.assertNotIn("WatchAlsoCards.ColumnDefinitions.Clear()", code)
        self.assertIn('Log.Error("[Details] Render failed', code)

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
