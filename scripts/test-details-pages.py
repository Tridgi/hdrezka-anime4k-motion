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
        for name in ("DetailsPage", "SettingsPage", "UpdatesPage", "AppearancePage", "HomePage", "CatalogPage", "CollectionsPage", "PersonPage", "SupportPage"):
            markup = contents(name + ".xaml")
            code = contents(name + ".xaml.cs")
            root = ET.fromstring(markup)
            names = [node.attrib[XAML_NAME] for node in root.iter()
                     if XAML_NAME in node.attrib]
            self.assertEqual(len(names), len(set(names)), name)
            for handler in re.findall(
                    r'\b(?:Click|SizeChanged|Toggled|SelectionChanged|ItemClick|ViewChanged|TextChanged|RecommendationHoverChanged)'
                    r'="([A-Za-z_]\w*)"', markup):
                self.assertIn(handler + "(", code, (name, handler))

    def test_all_collections_pages_are_loaded_deduped_and_bounded(self):
        client = contents("../../HDREZKA.Core/Api/RezkaClient.cs")
        parsers = contents("../../HDREZKA.Core/Api/Parsers.cs")
        code = contents("CollectionsPage.xaml.cs")
        self.assertIn("PageUrl(\"collections/\", page)", client)
        self.assertIn("const int hardPageLimit = 25", client)
        self.assertIn("if (seen.Add(item.Id))", client)
        self.assertIn("if (fresh == 0) break", client)
        self.assertIn("ParseCollectionsLastPage(html)", client)
        self.assertIn("ParseCollectionsLastPage(string html)", parsers)
        self.assertIn("GetAllCollectionsAsync()", code)
        self.assertIn("if (e.Parameter is CollectionLaunch launch", code)

    def test_details_franchise_collection_chips_and_genre_navigation(self):
        details = contents("DetailsPage.xaml.cs")
        models = contents("../../HDREZKA.Core/Api/Models.cs")
        parser = contents("../../HDREZKA.Core/Api/Parsers.cs")
        catalog = contents("CatalogPage.xaml.cs")
        self.assertIn("FranchiseParts { get; init; }", models)
        self.assertIn("CollectionLinks { get; init; }", models)
        self.assertIn("SourceSeries { get; init; }", models)
        self.assertIn("new CollectionLaunch(link.Id, link.Name, link.Id)", details)
        self.assertIn('Loc.Get("Details.FromSeries")', details)
        self.assertIn("GetSourceSeriesListAsync(sourcePath, page)",
                      contents("CollectionsPage.xaml.cs"))
        self.assertIn("ParseFranchiseParts(doc, pagePath)", parser)
        self.assertIn("ParseDetailCollectionLinks(doc)", parser)
        self.assertIn("WrapNavigationChips(parts", details)
        self.assertIn("new CollectionLaunch(link.Id, link.Name)", details)
        self.assertIn("new CatalogGenreLaunch(target)", details)
        self.assertIn("GetGenreListAsync(genre, page, filter: filter)", catalog)
        self.assertIn("GetSectionListAsync(section, filter, genreId, page)", catalog)
        self.assertIn("GetGenreOptionsAsync(section)", catalog)
        self.assertIn("ClickableChip(", details)
        self.assertIn("ClearGenreButton_Click", catalog)

    def test_person_page_uses_adaptive_poster_grid_without_scrollbars(self):
        markup = contents("PersonPage.xaml")
        code = contents("PersonPage.xaml.cs")
        for name in ("PersonHeroGrid", "PersonBioPanel", "PersonPoster",
                     "FilmographyPanel", "FilmographyHeader",
                     "FilmographyCountText"):
            self.assertIn('x:Name="' + name + '"', markup)
        self.assertIn("PersonHeroGrid_SizeChanged", code)
        self.assertIn("ItemsWrapGrid", markup)
        self.assertIn('x:Key="PersonFilmTemplate"', markup)
        self.assertIn("ScrollViewer.VerticalScrollMode", markup)
        self.assertIn("VerticalScrollBarVisibility=\"Hidden\"", markup)
        self.assertIn("GridView", code)
        self.assertNotIn("MaxHeight = 480", code)

    def test_localization_dictionaries_do_not_contain_duplicate_keys(self):
        localization = contents("../Services/LocalizationService.cs")
        for dictionary in ("Russian", "English", "Ukrainian"):
            start = localization.index(
                "private static readonly Dictionary<string, string> " + dictionary)
            end = localization.index("};", start)
            keys = re.findall(r'\["([^"]+)"\]\s*=', localization[start:end])
            self.assertEqual(len(keys), len(set(keys)),
                             dictionary + " contains duplicate keys")

    def test_catalog_filter_first_and_hover_preview_is_outside_grid(self):
        markup = contents("CatalogPage.xaml")
        code = contents("CatalogPage.xaml.cs")
        self.assertLess(markup.index('x:Name="FilterBox"'),
                        markup.index('x:Name="SectionPanel"'))
        self.assertLess(markup.index('x:Name="SectionPanel"'),
                        markup.index('x:Name="GenreBox"'))
        self.assertLess(markup.index('x:Name="GenreBox"'),
                        markup.index('x:Name="ClearGenreButton"'))
        self.assertNotIn('x:Name="GenreFilterPanel"', markup)
        self.assertNotIn('HorizontalScrollMode="Auto"', markup)
        for row in ("CatalogFiltersHost", "CatalogMainFilterRow",
                    "CatalogSecondaryFilterRow", "CatalogTertiaryFilterRow"):
            self.assertIn('x:Name="' + row + '"', markup)
        self.assertIn("CatalogFiltersHost_SizeChanged(", code)
        self.assertIn("ReflowSectionButtons(", code)
        self.assertIn("CatalogSecondaryFilterRow.Children.Add(SectionPanel)", code)
        self.assertIn('RecommendationPreview="True"', markup)
        self.assertIn('RecommendationHoverChanged="CatalogMovie_HoverChanged"', markup)
        self.assertIn('x:Name="CatalogPreviewLayer"', markup)
        self.assertIn('Grid.RowSpan="2" Canvas.ZIndex="100"', markup)
        self.assertIn('IsHitTestVisible="False"', markup)
        self.assertIn('x:Name="CatalogPreviewPanel"', markup)
        self.assertIn("PositionCatalogPreview(MovieCard card)", code)
        self.assertIn("Task.Delay(170, token)", code)
        self.assertIn("GetDetailsAsync(movie.Id, token)", code)
        self.assertIn("GenreColors.Palette", code)
        self.assertIn("HideCatalogPreview();", code)
        self.assertIn("if (_previewCache.Count >= 24)", code)

    def test_boosty_support_page_has_no_redemption_form(self):
        markup = contents("SupportPage.xaml")
        code = contents("SupportPage.xaml.cs")
        self.assertIn('x:Name="BoostyButton"', markup)
        self.assertIn("https://boosty.to/tridgi", code)
        for name in ("DonorBox", "ApplyDonorButton",
                     "DonorStatusText", "DonorPerksText"):
            self.assertNotIn(name, markup)
            self.assertNotIn(name, code)
        self.assertIn('x:Name="BoostyQrImage"', markup)
        self.assertIn('x:Name="BoostyQrCard"', markup)
        self.assertIn('x:Name="BoostyContentLayout"', markup)
        self.assertIn("BoostyContentLayout_SizeChanged(", code)
        self.assertIn("boosty-qr.png", code)

    def test_comment_ownership_and_error_decoding_are_implemented(self):
        details = contents("DetailsPage.xaml.cs")
        core = contents("../../HDREZKA.Core/Api/RezkaClient.cs")
        parser = contents("../../HDREZKA.Core/Api/Parsers.cs")
        self.assertIn('Loc.Get("Comments.YourComment")', details)
        self.assertIn('comment.IsOwn', details)
        self.assertIn('IsOwn: isOwn', parser)
        self.assertIn('allowed_comments', core)
        self.assertIn('CleanCommentMessage(', core)

    def test_collections_browse_grid_replaces_horizontal_strip(self):
        markup = contents("CollectionsPage.xaml")
        code = contents("CollectionsPage.xaml.cs")
        self.assertNotIn('x:Name="CollectionsPanel"', markup)
        self.assertNotIn('HorizontalScrollMode="Auto"', markup)
        for name in ("BrowseScroll", "BrowsePanel", "BrowseSearchBox",
                     "FoldersGrid", "CollectionDetailsPanel", "SelectedCollectionTitle",
                     "BackToCollectionsButton", "BrowseStatePanel"):
            self.assertIn('x:Name="' + name + '"', markup)
        self.assertIn('BrowseSearchBox_TextChanged', code)
        self.assertIn("RebuildFolderCards()", code)
        self.assertIn("StringComparison.CurrentCultureIgnoreCase", code)
        self.assertIn("SetView(selected: false)", code)
        self.assertNotIn("SelectCollection(firstButton", code)

    def test_collections_missing_folder_does_not_strand_user(self):
        code = contents("CollectionsPage.xaml.cs")
        self.assertIn("if (ex.Kind == RezkaError.NotFound)", code)
        self.assertIn('ReturnToBrowse(Loc.Get("Collections.Unavailable"))', code)
        self.assertIn("BackToCollectionsButton_Click", code)
        self.assertIn("Stretch = Stretch.Uniform,", code)
        self.assertIn("TryReturnToBrowse()", code)
        self.assertIn("version == _requestVersion", code)
        self.assertIn("DetailsBackButton.Visibility", code)
        self.assertIn('Loc.Get("Collections.EmptyFolder")', code)
        self.assertIn("BrowseScroll.ChangeView(null, 0, null, true)", code)

    def test_appearance_poster_sample_changes_with_selected_size(self):
        markup = contents("AppearancePage.xaml")
        code = contents("AppearancePage.xaml.cs")
        for name in ("PosterPreviewCard", "PosterPreviewArt",
                     "PosterPreviewHeading", "PosterPreviewDimensions",
                     "PosterPreviewPlaceholderText", "PosterPreviewFilmTitle",
                     "PosterPreviewFilmInfo"):
            self.assertIn('x:Name="' + name + '"', markup)
        self.assertIn("PosterMetrics.CardWidth", code)
        self.assertIn("PosterMetrics.CardHeight", code)
        self.assertIn("PosterPreviewArt.Width = width", code)
        self.assertIn("PosterPreviewArt.Height = height", code)
        self.assertIn("PosterPreviewArt.Background = background", code)
        self.assertIn("SyncPosterSize();", code)
        self.assertIn("UpdatePosterPreview();", code)
        self.assertIn('Loc.Get("Appearance.PosterDimensions", width, height)', code)

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

    def test_hover_is_page_owned_and_clamped_to_viewport(self):
        markup = contents("DetailsPage.xaml")
        details = contents("DetailsPage.xaml.cs")
        card = contents("../Controls/MovieCard.xaml.cs")
        self.assertIn('x:Name="PreviewLayer"', markup)
        self.assertIn('IsHitTestVisible="False"', markup)
        self.assertIn('x:Name="RecommendationPreviewOverlay"', markup)
        self.assertIn('RecommendationHoverChanged="RecommendationCard_HoverChanged"', markup)
        self.assertIn("RecommendationHoverChanged?.Invoke(", card)
        self.assertIn("PositionRecommendationPreview(MovieCard card)", details)
        self.assertIn("Canvas.SetLeft(overlay, x)", details)
        self.assertIn("Task.Delay(145, ct)", details)
        self.assertIn("GetDetailsAsync(", details)
        self.assertNotIn("FlyoutPlacementMode.Auto", card)

    def test_compact_schedule_and_full_modal_without_visible_scrollbar(self):
        markup = contents("DetailsPage.xaml")
        code = contents("DetailsPage.xaml.cs")
        self.assertIn('x:Name="ScheduleList"', markup)
        self.assertNotIn('x:Name="ScheduleCountText"', markup)
        self.assertIn("Take(5)", code)
        handler = code.split("private async void ScheduleMoreButton_Click", 1)[1]
        handler = handler.split("private async void VoicesRatingButton_Click", 1)[0]
        self.assertIn("ShowCenteredModalAsync", handler)
        self.assertIn("VerticalScrollBarVisibility = ScrollBarVisibility.Hidden", handler)
        self.assertNotIn("_scheduleExpanded = !_scheduleExpanded", handler)

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
        self.assertIn("CommentOpenSiteButton_Click", code)
        self.assertIn('x:Name="CommentOpenSiteButton"', markup)
        self.assertIn("ex.Kind == RezkaError.Site ? ex.Message", code)

    def test_appearance_page_is_in_footer_and_settings_links_to_it(self):
        settings = contents("SettingsPage.xaml")
        appearance = contents("AppearancePage.xaml")
        code = contents("AppearancePage.xaml.cs")
        navigation = contents("../MainWindow.xaml")
        navigation_code = contents("../MainWindow.xaml.cs")
        self.assertNotIn('x:Name="ThemeBox"', settings)
        self.assertNotIn('x:Name="PaletteBox"', settings)
        self.assertIn('x:Name="OpenAppearanceButton"', settings)
        for name in ("ThemeBox", "ManualBrightnessBox", "AccentMatrixGrid",
                     "AccentHexBox", "PaletteBox", "PosterSizeBox",
                     "ResetAppearanceButton", "PreviewRezkaBadge"):
            self.assertIn('x:Name="' + name + '"', appearance)
        self.assertIn("SetDetailPalette(palette)", code)
        self.assertIn("ThemeHelper.Apply(theme)", code)
        self.assertIn('x:Name="NavAppearance" Tag="Appearance"', navigation)
        self.assertLess(navigation.index('x:Name="NavAppearance"'),
                        navigation.index('x:Name="NavSupport"'))
        self.assertIn('typeof(AppearancePage)', navigation_code)

    def test_mouse_side_buttons_navigate_frame_history(self):
        code = contents("../MainWindow.xaml.cs")
        self.assertIn("IsXButton1Pressed", code)
        self.assertIn("IsXButton2Pressed", code)
        self.assertIn("ContentFrame.CanGoBack", code)
        self.assertIn("ContentFrame.CanGoForward", code)
        self.assertIn("RootGrid.AddHandler(UIElement.PointerPressedEvent", code)

    def test_all_pages_hide_vertical_scrollbar_but_keep_scrolling(self):
        for file in VIEWS.glob("*.xaml"):
            text = file.read_text(encoding="utf-8-sig")
            self.assertNotIn('VerticalScrollBarVisibility="Auto"', text, file.name)
        self.assertNotIn("VerticalScrollBarVisibility = ScrollBarVisibility.Auto",
                         contents("DetailsPage.xaml.cs"))
        self.assertNotIn("VerticalScrollBarVisibility = ScrollBarVisibility.Auto",
                         contents("VersionHistoryDialog.cs"))

    def test_hover_badges_and_genre_chips_follow_details_palette(self):
        markup = contents("DetailsPage.xaml")
        code = contents("DetailsPage.xaml.cs")
        for name in ("PreviewHeader", "PreviewRezkaBadge", "PreviewImdbBadge",
                     "PreviewKpBadge", "PreviewGenreChips"):
            self.assertIn('x:Name="' + name + '"', markup)
        self.assertIn("StyleRecommendationPreview()", code)
        self.assertIn("BadgeBrushes(MetadataTone.Rating)", code)
        self.assertIn("RenderPreviewGenres(IReadOnlyList<MovieGenre>? genres)", code)
        self.assertIn('Loc.Get("Details.AboutMovie")', code)

    def test_home_hero_and_horizontal_rails(self):
        markup = contents("HomePage.xaml")
        code = contents("HomePage.xaml.cs")
        self.assertIn('x:Name="HeroDescription"', markup)
        self.assertIn('x:Name="HeroInnerGrid"', markup)
        self.assertIn("TimeSpan.FromSeconds(14)", code)
        self.assertIn("_heroHovered", code)
        self.assertIn('HeroRatingBadge.Background = RezkaScoreColors.Gradient(rating)', code)
        self.assertIn("LoadHeroDescriptionAsync", code)
        self.assertNotIn('dot.Width = i == _heroIndex ? 20 : 8', code)
        self.assertIn("dot.Width = 9", code)
        for section in ("Films", "Series", "Cartoons", "Popular", "Watching", "Soon"):
            self.assertIn(f'x:Name="{section}Scroll"', markup)
            self.assertIn(f'x:Name="{section}Prev"', markup)
            self.assertIn(f'x:Name="{section}Next"', markup)
            self.assertIn(f'x:Name="{section}EndButton"', markup)
            self.assertIn(f'x:Name="{section}Grid"', markup)
        self.assertIn("HomeRailNav_Click", code)
        self.assertIn("ItemsControl grid", code)
        self.assertIn("ChangeView(target, null, null, false)", code)

    def test_home_preview_outside_scroll_clip_and_title_alignment(self):
        markup = contents("HomePage.xaml")
        code = contents("HomePage.xaml.cs")
        card = contents("../Controls/MovieCard.xaml")
        card_code = contents("../Controls/MovieCard.xaml.cs")
        self.assertIn('x:Name="HomePreviewLayer"', markup)
        self.assertIn('IsHitTestVisible="False"', markup)
        self.assertIn('RecommendationHoverChanged="HomeMovie_HoverChanged"', markup)
        self.assertIn("HomeHoverCurrent", code)
        self.assertIn("PositionHomePreview", code)
        self.assertIn("Task.Delay(160, ct)", code)
        self.assertIn('Height="92"', card)
        self.assertIn('Height="40"', card)
        self.assertIn('Height="32"', card)
        self.assertIn('VerticalAlignment="Top" HorizontalAlignment="Left"', card)
        self.assertIn('Margin="10,20,18,22"', markup)
        self.assertIn('x:Name="RatingBadge"', card)
        self.assertIn("RezkaScoreColors.Gradient(score.Value)", card_code)
        self.assertIn("new CubicEase", card_code)
        self.assertIn("RatingBadge.Margin = InfoBadge.Visibility", card_code)
        self.assertNotIn("SetRatingFromDetails(float? rating)", card_code)
        self.assertIn("var score = Movie?.Rating", card_code)

    def test_full_bleed_palettes_and_zoom_have_no_extra_dark_title_pill(self):
        appearance = contents("AppearancePage.xaml.cs")
        card = contents("../Controls/MovieCard.xaml.cs")
        for fragment in ("var halves = new LinearGradientBrush",
                         "FontSize = 18", "tile.Children.Add(label);",
                         "var ink = new SolidColorBrush"):
            self.assertIn(fragment, appearance)
        self.assertNotIn("The slight centre shadow keeps white labels", appearance)
        self.assertIn("Canvas.SetZIndex(CardButton, 20)", card)
        self.assertIn("AnimateScale(1.055f)", card)

    def test_hover_cards_and_catalog_rows_allow_margin_for_scale(self):
        for page in ("CatalogPage.xaml", "SearchPage.xaml", "BookmarksPage.xaml"):
            markup = contents(page)
            self.assertIn('Value="12,18,12,22"', markup)
            self.assertIn('VerticalContentAlignment" Value="Top"', markup)

    def test_home_hover_uses_individual_rating_brand_marks(self):
        markup = contents("HomePage.xaml")
        code = contents("HomePage.xaml.cs")
        for name in ("HomePreviewRezka", "HomePreviewImdb",
                     "HomePreviewKp", "HomePreviewRezkaText",
                     "HomePreviewImdbText", "HomePreviewKpText"):
            self.assertIn('x:Name="' + name + '"', markup)
        self.assertIn('Text="IMDb"', markup)
        self.assertIn('Text="КП"', markup)
        self.assertIn('HomePreviewImdbText.Text = imdb?.ToString("0.0")', code)

    def test_color_schemes_are_full_bleed_and_hover_genres_text_only(self):
        appearance = contents("AppearancePage.xaml.cs")
        details = contents("DetailsPage.xaml.cs")
        self.assertIn("var halves = new LinearGradientBrush", appearance)
        self.assertIn("Offset = .5, Color = primary", appearance)
        self.assertIn("Offset = .5, Color = secondary", appearance)
        func = details.split("private void RenderPreviewGenres", 1)[1].split(
            "private void UpdateRecommendationPreview", 1)[0]
        self.assertIn("new TextBlock", func)
        self.assertNotIn("Chip(", func)
        self.assertIn("GenreColors.Palette(", details)
        self.assertIn("GenreColors.Palette(", contents("HomePage.xaml.cs"))

    def test_category_colors_distinct_and_score_dependent(self):
        genre = contents("../Services/GenreColors.cs")
        details = contents("DetailsPage.xaml.cs")
        home = contents("HomePage.xaml.cs")
        self.assertIn("AngularDistance(old, hue) < 28", genre)
        self.assertIn("name.Contains(\"мелодрам\")", genre)
        self.assertIn("name.Contains(\"фэнтез\")", genre)
        self.assertIn("public static LinearGradientBrush Gradient(float score)", genre)
        self.assertIn('label == "Rezka"', details)
        self.assertIn("RezkaScoreColors.Gradient(rating.Value.Value)", details)
        self.assertIn("HomePreviewRezka.Background = RezkaScoreColors.Gradient(score)", home)

    def test_only_arrows_scroll_horizontal_home_rails(self):
        markup = contents("HomePage.xaml")
        code = contents("HomePage.xaml.cs")
        self.assertEqual(markup.count('HorizontalScrollBarVisibility="Hidden"'), 6)
        self.assertEqual(markup.count('HorizontalScrollMode="Enabled"'), 6)
        self.assertIn("HomeRailNav_Click", code)
        self.assertIn("rail.Scroll.ChangeView(target, null, null, false)", code)
        for name in ("Films", "Series", "Cartoons", "Popular", "Watching", "Soon"):
            self.assertIn('x:Name="' + name + 'Scroll"', markup)

    def test_catalog_end_tiles_larger_and_posters_never_dim(self):
        markup = contents("HomePage.xaml")
        home = contents("HomePage.xaml.cs")
        poster = contents("../Controls/MovieCard.xaml")
        for name in ("Films", "Series", "Cartoons", "Popular", "Watching", "Soon"):
            self.assertIn('x:Name="' + name + 'EndHint"', markup)
        self.assertIn("PosterMetrics.CardWidth * 1.30", home)
        self.assertIn("button.Background = gradient;", home)
        self.assertIn('Background="Transparent" CornerRadius="12" Opacity="0"', poster)
        self.assertNotIn('card.SetRatingFromDetails(', home)
        self.assertIn('RezkaScoreColors.Gradient(score.Value)', contents("../Controls/MovieCard.xaml.cs"))

    def test_shift_wheel_and_arrows_keep_rails_scrollable(self):
        markup = contents("HomePage.xaml")
        code = contents("HomePage.xaml.cs")
        self.assertEqual(markup.count('HorizontalScrollBarVisibility="Hidden"'), 6)
        self.assertEqual(markup.count('PointerWheelChanged="HomeRail_PointerWheelChanged"'), 6)
        self.assertIn("InputKeyboardSource.GetKeyStateForCurrentThread", code)
        self.assertIn("Windows.System.VirtualKey.Shift", code)
        self.assertIn("rail.Scroll.ChangeView(target, null, null, false)", code)
        self.assertIn("HomeVerticalScroll.ChangeView(null, target, null, false)", code)
        self.assertIn("scroll.SizeChanged +=", code)
        self.assertIn("e.Handled = true", code)

    def test_netflix_style_hero_rank_replaces_kicker(self):
        markup = contents("HomePage.xaml")
        code = contents("HomePage.xaml.cs")
        self.assertIn('x:Name="HeroRankNumber"', markup)
        self.assertIn('x:Name="HeroArtwork"', markup)
        self.assertIn("HeroRankNumber.Text = (_heroIndex + 1).ToString", code)
        self.assertIn("HeroArtwork.Visibility = narrow", code)
        self.assertNotIn('x:Name="HeroKicker"', markup)
        self.assertNotIn("HeroKicker.Text", code)

    def test_hover_keeps_poster_clear_and_does_not_lazily_add_rating(self):
        markup = contents("../Controls/MovieCard.xaml")
        card = contents("../Controls/MovieCard.xaml.cs")
        details = contents("DetailsPage.xaml.cs")
        home = contents("HomePage.xaml.cs")
        self.assertIn("<Button.Template>", markup)
        self.assertIn("UseSystemFocusVisuals=\"True\"", markup)
        self.assertIn('Background="Transparent" CornerRadius="12" Opacity="0"', markup)
        self.assertNotIn("_detailRating", card)
        self.assertNotIn("SetRatingFromDetails", card)
        self.assertNotIn("card.SetRatingFromDetails(", home)
        self.assertNotIn("card.SetRatingFromDetails(", details)
        self.assertIn("var score = Movie?.Rating", card)

    def test_sidebar_button_has_one_xaml_content_element(self):
        root = ET.fromstring(contents("../MainWindow.xaml"))
        for element in root.iter():
            if element.attrib.get(XAML_NAME) != "AppPaneButton":
                continue
            children = list(element)
            self.assertEqual(len(children), 1,
                             "WinUI Button.Content accepts exactly one child")
            self.assertTrue(children[0].tag.endswith("Grid"))
            self.assertEqual(len(children[0]), 2)
            return
        self.fail("AppPaneButton not found")

    def test_sidebar_glyph_changes_on_toggle(self):
        markup = contents("../MainWindow.xaml")
        code = contents("../MainWindow.xaml.cs")
        self.assertIn('x:Name="AppPaneOpenGlyph"', markup)
        self.assertIn('x:Name="AppPaneClosedGlyph"', markup)
        self.assertIn("NavView.PaneOpened +=", code)
        self.assertIn("NavView.PaneClosed +=", code)
        self.assertIn("NavView_DisplayModeChanged", code)
        self.assertIn("UpdatePaneGlyph()", code)

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
