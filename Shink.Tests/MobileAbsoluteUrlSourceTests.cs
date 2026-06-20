using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class MobileAbsoluteUrlSourceTests
{
    [TestMethod]
    public void MobileAbsoluteUrlHelperDoesNotTreatRootRelativePathsAsFileUrls()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "trimmedPathOrUrl.StartsWith(\"//\", StringComparison.Ordinal)");
        StringAssert.Contains(program, "absoluteUri.Scheme is \"http\" or \"https\"");
        StringAssert.Contains(program, "return $\"{baseUrl}/{trimmedPathOrUrl.TrimStart('/')}\";");
        Assert.IsFalse(program.Contains("if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absoluteUri))", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStorySummariesUseDirectMediaUrlsInsteadOfBrowserImageProxy()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "static string ToMobileMediaUri(HttpContext httpContext, string? pathOrUrl)");
        StringAssert.Contains(program, "TryExtractImageProxySource(trimmedPathOrUrl, out var proxiedSourceUrl)");
        StringAssert.Contains(program, "QueryHelpers.ParseQuery(query)");
        StringAssert.Contains(program, "ImageUrl: ToMobileMediaUri(httpContext, story.ImagePath)");
        StringAssert.Contains(program, "ThumbnailUrl: ToMobileMediaUri(httpContext, story.ThumbnailPath)");
        Assert.IsFalse(program.Contains("ImageUrl: ToAbsoluteUri(httpContext, story.ImagePath)", StringComparison.Ordinal));
        Assert.IsFalse(program.Contains("ThumbnailUrl: ToAbsoluteUri(httpContext, story.ThumbnailPath)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileClientUnwrapsLiveImageProxyUrlsAndRejectsFileUrls()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(client, "NormalizeIncomingUrl(url.Trim())");
        StringAssert.Contains(client, "TryExtractImageProxySource(trimmedUrl, out var proxiedImageUrl)");
        StringAssert.Contains(client, "string.Equals(parsed.Scheme, Uri.UriSchemeFile");
        StringAssert.Contains(client, "return $\"{path}{parsed.Query}{parsed.Fragment}\";");
        StringAssert.Contains(client, "private static bool IsWebUri(Uri uri)");
        StringAssert.Contains(client, "Uri.UriSchemeHttps");
        Assert.IsFalse(client.Contains("if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("Source = \"dis_storietyd.png\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("_apiClient.BuildImageUrl(\"/branding/DIS_STORIETYD.png\")", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterPlaylistCardsUseDedicatedPlaylistArtwork()
    {
        var helper = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(helper, "ResolveStoryCardImageSource(story, apiClient)");
        StringAssert.Contains(helper, "IsLegacyWebsiteAsset(story.ThumbnailUrl)");
        StringAssert.Contains(helper, "return apiClient.BuildImageUrl(story.ImageUrl);");
        StringAssert.Contains(helper, "normalized.StartsWith(\"/stories/\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(luisterPage, "playlist.ArtworkUrl");
        StringAssert.Contains(luisterPage, "_apiClient.BuildCachedImageSource(playlist.ArtworkUrl, \"schink_background.jpeg\")");
        Assert.IsFalse(luisterPage.Contains("firstStory?.ImageUrl", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("firstStory?.ThumbnailUrl", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("IsBundledPlaylistFallback", StringComparison.Ordinal));
        StringAssert.Contains(program, "ArtworkUrl: BuildMobilePlaylistArtworkUri(httpContext, playlist)");
        StringAssert.Contains(program, "playlist.ShowcaseImagePath");
        StringAssert.Contains(program, "playlist.PreferredStory?.ImagePath");
        StringAssert.Contains(program, "playlist.PreferredStory?.ThumbnailPath");
        StringAssert.Contains(program, "playlist.BackdropImagePath");
        StringAssert.Contains(program, "playlist.LogoImagePath");
    }

    [TestMethod]
    public void MobileLuisterUsesOrderedSectionsForSpeellysteParity()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(models, "public sealed record MobileLuisterSection(");
        StringAssert.Contains(program, "IsMobileSpeellysteSystemPlaylist");
        StringAssert.Contains(program, "item.Playlist.IncludeInSpeellysteCarousel");
        StringAssert.Contains(program, "MobileLuisterSectionKinds.Speellyste");
        StringAssert.Contains(program, ".OrderBy(section => section.SortOrder)");
        StringAssert.Contains(luisterPage, "FilterSections(_sections, _searchEntry.Text)");
        StringAssert.Contains(luisterPage, "IsSpeellysteSection(section)");
        StringAssert.Contains(luisterPage, "BuildPlaylistShowcase(section.Title, section.Playlists)");
        Assert.IsFalse(luisterPage.Contains("_playlistContent.Children.Add(BuildPlaylistShowcase(filteredPlaylists));", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterSearchDebouncesTypingAndMatchesStoryMetadata()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "_searchEntry.TextChanged += (_, _) => QueueSearchRender();");
        StringAssert.Contains(luisterPage, "TextColor = Color.FromArgb(\"#243238\")");
        StringAssert.Contains(luisterPage, "PlaceholderColor = Color.FromArgb(\"#7C817C\")");
        StringAssert.Contains(luisterPage, "private async Task DebounceSearchRenderAsync(CancellationToken cancellationToken)");
        StringAssert.Contains(luisterPage, "await Task.Delay(220, cancellationToken);");
        StringAssert.Contains(luisterPage, "private async Task ResetScrollPositionAsync()");
        StringAssert.Contains(luisterPage, "await _scrollView.ScrollToAsync(0, 0, false);");
        StringAssert.Contains(luisterPage, "if (!_hasLoaded || !_isPageActive || Handler is null)");
        StringAssert.Contains(luisterPage, "catch (ObjectDisposedException)");
        StringAssert.Contains(luisterPage, "_isPageActive = false;");
        StringAssert.Contains(luisterPage, "MainThread.BeginInvokeOnMainThread(() =>");
        StringAssert.Contains(luisterPage, "_ = ResetScrollPositionAsync();");
        StringAssert.Contains(luisterPage, "RenderPlaylistContent();");
        StringAssert.Contains(luisterPage, "StoryMatches(story, normalizedQuery)");
        StringAssert.Contains(luisterPage, "ContainsNormalized(story.Description, normalizedQuery)");
        StringAssert.Contains(luisterPage, "ContainsNormalized(story.Slug, normalizedQuery)");
        Assert.IsFalse(luisterPage.Contains("_searchEntry.TextChanged += (_, _) => RenderPlaylistContent();", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterPullToRefreshUsesUiSafeCancelableLoadPath()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "_refreshView = new RefreshView");
        StringAssert.Contains(luisterPage, "Command = new Command(() => _ = TriggerRefreshAsync())");
        StringAssert.Contains(luisterPage, "private async Task TriggerRefreshAsync()");
        StringAssert.Contains(luisterPage, "_loadCancellation?.Cancel();");
        StringAssert.Contains(luisterPage, "_loadCancellation = new CancellationTokenSource();");
        StringAssert.Contains(luisterPage, "var cancellationToken = _loadCancellation.Token;");
        StringAssert.Contains(luisterPage, "await MainThread.InvokeOnMainThreadAsync(() =>");
        StringAssert.Contains(luisterPage, "_refreshView.IsRefreshing = false");
        StringAssert.Contains(luisterPage, "if (cancellationToken.IsCancellationRequested || !_isPageActive)");
        StringAssert.Contains(luisterPage, "_isPageActive = true;");
        StringAssert.Contains(luisterPage, "_isPageActive = false;");
        Assert.IsFalse(luisterPage.Contains("Command = new Command(async () => await LoadAsync(forceRefresh: true))", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterTopButtonsSlideBackWhenUserScrollsUp()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "private readonly ScrollView _scrollView;");
        StringAssert.Contains(luisterPage, "private readonly Grid _topBarOverlay;");
        StringAssert.Contains(luisterPage, "private Border? _floatingTopBarHost;");
        StringAssert.Contains(luisterPage, "_topBarOverlay = new Grid");
        StringAssert.Contains(luisterPage, "HeightRequest = FloatingTopBarContentInset");
        StringAssert.Contains(luisterPage, "ZIndex = 100");
        StringAssert.Contains(luisterPage, "_refreshView,\n                _topBarOverlay");
        StringAssert.Contains(luisterPage, "_scrollView.Scrolled += OnContentScrolled;");
        StringAssert.Contains(luisterPage, "RenderFloatingTopBar();");
        StringAssert.Contains(luisterPage, "_topBarOverlay.Children.Add(_floatingTopBarHost);");
        StringAssert.Contains(luisterPage, "_topBarOverlay.InputTransparent = _isTopBarHidden;");
        StringAssert.Contains(luisterPage, "private void OnContentScrolled(object? sender, ScrolledEventArgs e)");
        StringAssert.Contains(luisterPage, "var delta = e.ScrollY - _lastScrollY;");
        StringAssert.Contains(luisterPage, "_ = SetTopBarHiddenAsync(delta > 0);");
        StringAssert.Contains(luisterPage, "private async Task SetTopBarHiddenAsync(bool hidden)");
        StringAssert.Contains(luisterPage, "topBar.TranslateToAsync(0, hidden ? FloatingTopBarHiddenOffset : 0");
        StringAssert.Contains(luisterPage, "topBar.FadeToAsync(hidden ? 0 : 1");
        StringAssert.Contains(luisterPage, "Padding = new Thickness(18, FloatingTopBarContentInset, 18, 28)");
        Assert.IsFalse(luisterPage.Contains("_content.Children.Add(BuildLuisterTopBar());", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileSignedInShellDoesNotRenderBottomTabBar()
    {
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));

        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));");
        StringAssert.Contains(appShell, "ContentTemplate = new DataTemplate(() => _services.GetRequiredService<LuisterPage>())");
        StringAssert.Contains(luisterPage, "Shell.Current.GoToAsync(nameof(AccountPage), animate: true)");
        StringAssert.Contains(luisterPage, "Shell.Current.GoToAsync(nameof(ProfilePage), animate: true)");
        StringAssert.Contains(mobileTopBar, "Shell.Current.GoToAsync(nameof(AccountPage), animate: true)");
        StringAssert.Contains(mobileTopBar, "Shell.Current.GoToAsync(nameof(ProfilePage), animate: true)");
        Assert.IsFalse(appShell.Contains("new TabBar()", StringComparison.Ordinal));
        Assert.IsFalse(appShell.Contains("CreateTab(", StringComparison.Ordinal));
        Assert.IsFalse(appShell.Contains("tab_luister.png", StringComparison.Ordinal));
        Assert.IsFalse(appShell.Contains("tab_rekening.png", StringComparison.Ordinal));
        Assert.IsFalse(appShell.Contains("SetTabBar", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("OpenAccountTab", StringComparison.Ordinal));
        Assert.IsFalse(mobileTopBar.Contains("TabBar", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileProfileIconOpensEditableProfileWithoutSubscriptionInfo()
    {
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var profilePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "ProfilePage.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<ProfilePage>();");
        StringAssert.Contains(luisterPage, "profileTap.Tapped += async (_, _) => await OpenProfileAsync();");
        StringAssert.Contains(mobileTopBar, "profileTap.Tapped += async (_, _) => await OpenProfileAsync();");
        StringAssert.Contains(profilePage, "public sealed class ProfilePage : ContentPage");
        StringAssert.Contains(profilePage, "private readonly Entry _emailEntry;");
        StringAssert.Contains(profilePage, "private readonly Entry _firstNameEntry;");
        StringAssert.Contains(profilePage, "private readonly Entry _lastNameEntry;");
        StringAssert.Contains(profilePage, "private readonly Entry _displayNameEntry;");
        StringAssert.Contains(profilePage, "private readonly Entry _mobileNumberEntry;");
        StringAssert.Contains(profilePage, "await _apiClient.UpdateProfileAsync(");
        StringAssert.Contains(profilePage, "_emailEntry.IsReadOnly = true;");
        StringAssert.Contains(profilePage, "var email = FirstValue(session.Email, _emailEntry.Text);");
        StringAssert.Contains(profilePage, "var nameParts = SplitDisplayName(displayName);");
        StringAssert.Contains(profilePage, "_displayNameEntry.Text = displayName ?? BuildDisplayName(firstName, lastName) ?? string.Empty;");
        StringAssert.Contains(profilePage, "private static string? FirstValue(params string?[] values)");
        StringAssert.Contains(profilePage, "private static (string? FirstName, string? LastName) SplitDisplayName(string? displayName)");
        Assert.IsFalse(profilePage.Contains("HasPaidSubscription", StringComparison.Ordinal));
        Assert.IsFalse(profilePage.Contains("betaalde luistertoegang", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(profilePage.Contains("gratis toegang", StringComparison.OrdinalIgnoreCase));

        StringAssert.Contains(models, "string? FirstName,");
        StringAssert.Contains(models, "string? LastName,");
        StringAssert.Contains(models, "string? MobileNumber,");
        StringAssert.Contains(models, "public sealed record MobileProfileUpdateResponse(string Message, MobileSession Session);");
        StringAssert.Contains(client, "FirstNamePreferenceKey");
        StringAssert.Contains(client, "LastNamePreferenceKey");
        StringAssert.Contains(client, "MobileNumberPreferenceKey");
        StringAssert.Contains(client, "public async Task<(bool IsSuccess, string Message)> UpdateProfileAsync(");
        StringAssert.Contains(client, "\"/api/mobile/profile\"");

        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/profile\"");
        StringAssert.Contains(program, "sealed record MobileProfileUpdateRequest");
        StringAssert.Contains(program, "sealed record MobileProfileUpdateResponse");
        StringAssert.Contains(program, "FirstName: ResolveMobileProfileFirstName");
        StringAssert.Contains(program, "LastName: ResolveMobileProfileLastName");
        StringAssert.Contains(program, "MobileNumber: ResolveMobileProfileMobileNumber");
        StringAssert.Contains(program, "UpsertSubscriberProfileAsync(");
    }

    [TestMethod]
    public void MobileLuisterShowcaseMatchesWebDatabaseFlagsAndPreferredStoryFallback()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));

        StringAssert.Contains(models, "bool? ShowShowcaseImageOnLuisterPage = null");
        StringAssert.Contains(luisterPage, "var showcaseStory = ResolvePlaylistShowcaseStory(playlist);");
        StringAssert.Contains(luisterPage, "if (showcaseStory is not null && ShouldShowPlaylistShowcase(playlist))");
        StringAssert.Contains(luisterPage, "playlist.ShowcaseStory ?? playlist.Stories.FirstOrDefault();");
        StringAssert.Contains(luisterPage, "playlist.ShowShowcaseImageOnLuisterPage == true;");
        Assert.IsFalse(luisterPage.Contains("return playlist.ShowcaseStory is not null || playlist.Stories.Count > 0;", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("if (playlist.ShowShowcaseImageOnLuisterPage is { } explicitValue)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterDoesNotShowSignedInAccountSummary()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "if (!_sessionState.Current.IsSignedIn)");
        StringAssert.Contains(luisterPage, "_content.Children.Add(BuildAccountPanel());");
        Assert.IsFalse(luisterPage.Contains("\"Alles oop\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("\"Gratis toegang\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("Text = session.Email ?? \"Ingeteken\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("if (session.IsSignedIn)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterTopBarMirrorsWebNotificationCenter()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));

        StringAssert.Contains(luisterPage, "BuildNotificationButton()");
        StringAssert.Contains(luisterPage, "searchButton,");
        StringAssert.Contains(luisterPage, "notificationButton,");
        StringAssert.Contains(luisterPage, "profileButton");
        StringAssert.Contains(luisterPage, "_notificationPage?.UnreadCount");
        StringAssert.Contains(luisterPage, "FormatNotificationCount(unreadCount)");
        StringAssert.Contains(luisterPage, "NotificationBadgeRefreshInterval = TimeSpan.FromSeconds(45)");
        StringAssert.Contains(luisterPage, "private IDispatcherTimer? _notificationRefreshTimer;");
        StringAssert.Contains(luisterPage, "_apiClient.NewNotificationsAvailable += _count => MainThread.BeginInvokeOnMainThread(() =>");
        StringAssert.Contains(luisterPage, "_ = RefreshNotificationsInBackgroundAsync());");
        StringAssert.Contains(luisterPage, "StartNotificationRefreshTimer();");
        StringAssert.Contains(luisterPage, "StopNotificationRefreshTimer();");
        StringAssert.Contains(luisterPage, "private void StartNotificationRefreshTimer()");
        StringAssert.Contains(luisterPage, "_notificationRefreshTimer.Tick += (_, _) =>");
        StringAssert.Contains(luisterPage, "private async Task ShowNotificationsAsync()");
        StringAssert.Contains(luisterPage, "await _apiClient.GetNotificationsAsync(");
        StringAssert.Contains(luisterPage, "await _apiClient.MarkAllNotificationsReadAsync()");
        StringAssert.Contains(luisterPage, "MarkAllNotificationsReadLocally();");
        StringAssert.Contains(luisterPage, "RenderContent();");
        StringAssert.Contains(luisterPage, "await _apiClient.ClearNotificationsAsync()");
        StringAssert.Contains(luisterPage, "await _apiClient.ClearNotificationAsync(notification.Id)");
        StringAssert.Contains(luisterPage, "await _apiClient.GetNotificationsAsync(before: before, history: _notificationPage.HasHistory");
        StringAssert.Contains(luisterPage, "Teken in om kennisgewings te sien.");
        StringAssert.Contains(luisterPage, "BuildNotificationCloseButton()");
        StringAssert.Contains(luisterPage, "Drawable = new NotificationDownCaretDrawable()");
        StringAssert.Contains(luisterPage, "private sealed class NotificationDownCaretDrawable : IDrawable");
        StringAssert.Contains(luisterPage, "return new SwipeView");
        StringAssert.Contains(luisterPage, "var removeSwipeItem = new SwipeItem");
        StringAssert.Contains(luisterPage, "Text = \"Verwyder\"");
        StringAssert.Contains(luisterPage, "removeSwipeItem.Invoked += async (_, _) => await ClearNotificationAsync();");
        StringAssert.Contains(luisterPage, "SwipeBehaviorOnInvoked = SwipeBehaviorOnInvoked.Close");
        StringAssert.Contains(luisterPage, "RowDefinitions =");
        StringAssert.Contains(luisterPage, "new RowDefinition { Height = GridLength.Star }");
        StringAssert.Contains(luisterPage, "Grid.SetRow(notificationScrollView, 2);");
        Assert.IsFalse(luisterPage.Contains("Content = new VerticalStackLayout\n            {\n                Padding = new Thickness(18, 18, 18, 28),\n                Spacing = 16,\n                Children =\n                {\n                    header,\n                    statusLabel,\n                    new ScrollView", StringComparison.Ordinal));

        StringAssert.Contains(models, "public sealed record MobileNotificationPage(");
        StringAssert.Contains(models, "public sealed record MobileNotificationItem(");
        StringAssert.Contains(models, "public sealed record MobileNotificationMutationResponse(");
        StringAssert.Contains(client, "GetNotificationsAsync(");
        StringAssert.Contains(client, "public event Action<int>? NewNotificationsAvailable;");
        StringAssert.Contains(client, "int limit = 10,");
        StringAssert.Contains(client, "DateTimeOffset? before = null,");
        StringAssert.Contains(client, "bool history = false,");
        StringAssert.Contains(client, "BuildNotificationRequestPath(limit, before, history)");
        StringAssert.Contains(client, "return $\"/api/notifications?{string.Join(\"&\", queryParts)}\";");
        StringAssert.Contains(client, "result?.NewNotificationsCreated > 0");
        StringAssert.Contains(client, "NewNotificationsAvailable?.Invoke(result.NewNotificationsCreated);");
        StringAssert.Contains(client, "private sealed record TrackingResponse(bool Tracked, int NewNotificationsCreated = 0);");
        StringAssert.Contains(client, "PostAsync<MobileNotificationMutationResponse>(\"/api/notifications/read-all\"");
        StringAssert.Contains(client, "PostAsync<MobileNotificationMutationResponse>(\"/api/notifications/clear\"");
        StringAssert.Contains(client, "$\"/api/notifications/{notificationId:D}/read\"");
        StringAssert.Contains(client, "$\"/api/notifications/{notificationId:D}/clear\"");
    }

    [TestMethod]
    public void MobileLuisterStoryCardsUseNativeArtworkAndFavoriteHeartOverlay()
    {
        var helper = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(helper, "BuildFavoriteHeart(story, onFavoriteTap)");
        StringAssert.Contains(helper, "Text = story.IsFavorite ? \"♥\" : \"♡\"");
        StringAssert.Contains(helper, "TextColor = story.IsFavorite ? Color.FromArgb(\"#E11D48\") : Color.FromArgb(\"#8A938D\")");
        StringAssert.Contains(helper, "HorizontalOptions = LayoutOptions.End");
        StringAssert.Contains(helper, "VerticalOptions = LayoutOptions.Start");
        StringAssert.Contains(helper, "StrokeShape = new RoundRectangle { CornerRadius = 24 }");
        StringAssert.Contains(helper, "Text = story.IsLocked ? \"Maak oop\" : \"Luister nou\"");
        Assert.IsFalse(helper.Contains("Text = story.IsFavorite ? \"Hartjie af\" : \"Hartjie\"", StringComparison.Ordinal));
        Assert.IsFalse(helper.Contains("new HorizontalStackLayout", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "WidthRequest = 168");
        StringAssert.Contains(luisterPage, "StrokeShape = new RoundRectangle { CornerRadius = 16 }");
        StringAssert.Contains(luisterPage, "TextColor = story.IsFavorite ? Color.FromArgb(\"#E11D48\") : Color.FromArgb(\"#8A938D\")");
        StringAssert.Contains(luisterPage, "Color = story.IsFavorite ? Color.FromArgb(\"#E11D48\") : Color.FromArgb(\"#8A938D\")");
    }

    [TestMethod]
    public void MobileLuisterWeeklyPopularPlaylistShowsTopLeftRankBadgesLikeWeb()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var webLuister = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor"));

        StringAssert.Contains(webLuister, "private static bool IsWeeklyPopularPlaylist(StoryPlaylist playlist) =>");
        StringAssert.Contains(webLuister, "\"popular-stories-this-week\"");
        StringAssert.Contains(webLuister, "story-carousel-rank");
        StringAssert.Contains(luisterPage, "private static bool IsWeeklyPopularPlaylist(MobilePlaylist playlist) =>");
        StringAssert.Contains(luisterPage, "\"popular-stories-this-week\"");
        StringAssert.Contains(luisterPage, "BuildRankedStoryCarousel(playlist)");
        StringAssert.Contains(luisterPage, "rankedStories,\n            304,");
        StringAssert.Contains(luisterPage, "new RankedLuisterStory(story, index + 1)");
        StringAssert.Contains(luisterPage, "BuildLuisterStoryCarouselCard(playlist, rankedStory.Story, rankedStory.Rank)");
        StringAssert.Contains(luisterPage, "if (rank is not null)");
        StringAssert.Contains(luisterPage, "cardShell.Children.Add(BuildStoryRankBadge(rank.Value));");
        StringAssert.Contains(luisterPage, "private static View BuildStoryRankBadge(int rank) =>");
        StringAssert.Contains(luisterPage, "Text = rank.ToString(CultureInfo.InvariantCulture)");
        StringAssert.Contains(luisterPage, "FontFamily = \"Arial Rounded MT Bold\"");
        StringAssert.Contains(luisterPage, "FontSize = 76");
        StringAssert.Contains(luisterPage, "LineHeight = 0.82");
        StringAssert.Contains(luisterPage, "HorizontalOptions = LayoutOptions.Start");
        StringAssert.Contains(luisterPage, "VerticalOptions = LayoutOptions.Start");
    }

    [TestMethod]
    public void MobileLuisterUsesSolidBackgroundColor()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "LuisterBackgroundColor = Color.FromArgb(\"#FFF7E8\")");
        StringAssert.Contains(luisterPage, "BackgroundColor = LuisterBackgroundColor;");
        Assert.IsFalse(luisterPage.Contains("BuildLuisterWebBackgroundBrush", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("BuildLuisterWebBaseBackgroundBrush", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("BuildLuisterWebOverlayBackgroundBrush", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("LinearGradientBrush", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "Background = Brush.Transparent");
    }

    [TestMethod]
    public void MobileLuisterFavoriteHeartPersistsAndUpdatesVisibleStoryState()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);");
        StringAssert.Contains(luisterPage, "_favoriteRequestsInFlight.Add(favoriteKey)");
        StringAssert.Contains(luisterPage, "_favoriteRequestsInFlight.Remove(favoriteKey)");
        StringAssert.Contains(luisterPage, "IsFavoriteRequestInFlight(story)");
        StringAssert.Contains(luisterPage, "new ActivityIndicator");
        StringAssert.Contains(luisterPage, "IsRunning = true");
        StringAssert.Contains(luisterPage, "UpdateFavoriteState(story.Slug, isFavorite);");
        StringAssert.Contains(luisterPage, "RenderPlaylistContent();");
        StringAssert.Contains(luisterPage, "private void UpdateFavoriteState(string slug, bool isFavorite)");
        StringAssert.Contains(luisterPage, "playlist.ShowcaseStory is null ? null : UpdateStoryFavoriteState(playlist.ShowcaseStory, slug, isFavorite)");
        StringAssert.Contains(luisterPage, "story with { IsFavorite = isFavorite }");
        Assert.IsFalse(luisterPage.Contains("await LoadAsync();", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterCachesStoryDataForFastColdStart()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(client, "private static readonly TimeSpan DefaultLuisterCacheMaxAge = TimeSpan.FromHours(12);");
        StringAssert.Contains(client, "public Task<MobileLuisterResponse?> GetCachedLuisterAsync(CancellationToken cancellationToken = default)");
        StringAssert.Contains(client, "private async Task<MobileLuisterResponse?> GetAndCacheLuisterAsync(CancellationToken cancellationToken)");
        StringAssert.Contains(client, "await SaveLuisterCacheAsync(response, cancellationToken);");
        StringAssert.Contains(client, "private async Task SaveLuisterCacheAsync(MobileLuisterResponse response, CancellationToken cancellationToken)");
        StringAssert.Contains(client, "new MobileLuisterCacheEntry(DateTimeOffset.UtcNow, response)");
        StringAssert.Contains(client, "var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, \"story-data\");");
        StringAssert.Contains(client, "return System.IO.Path.Combine(cacheDirectory, $\"luister-{cacheKey}.json\");");
        StringAssert.Contains(client, "private sealed record MobileLuisterCacheEntry(DateTimeOffset CachedAtUtc, MobileLuisterResponse Response);");

        StringAssert.Contains(luisterPage, "var downloadsTask = LoadPlayableDownloadsSafelyAsync(cancellationToken);");
        StringAssert.Contains(luisterPage, "var renderedCachedData = !forceRefresh && await TryRenderCachedLuisterAsync(downloadsTask, cancellationToken);");
        StringAssert.Contains(luisterPage, "if (!renderedCachedData)");
        StringAssert.Contains(luisterPage, "private async Task<bool> TryRenderCachedLuisterAsync(");
        StringAssert.Contains(luisterPage, "var cachedResponse = await _apiClient.GetCachedLuisterAsync(cancellationToken);");
        StringAssert.Contains(luisterPage, "ApplyLuisterResponse(cachedResponse, await downloadsTask);");
        StringAssert.Contains(luisterPage, "private void ApplyLuisterResponse(");
        StringAssert.Contains(luisterPage, "_sections = ApplyCurrentFavoriteState(sections);");
        StringAssert.Contains(luisterPage, "private IReadOnlyList<MobileLuisterSection> ApplyCurrentFavoriteState(IReadOnlyList<MobileLuisterSection> sections)");
        StringAssert.Contains(luisterPage, "favoriteSlugs.Contains(story.Slug)");
    }

    [TestMethod]
    public void MobileLuisterStoryTitlesOpenPlayerWithPlaylistContext()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "story => BuildLuisterStoryCarouselCard(playlist, story)");
        StringAssert.Contains(luisterPage, "private View BuildLuisterStoryCarouselCard(MobilePlaylist playlist, MobileStorySummary story, int? rank = null)");
        StringAssert.Contains(luisterPage, "await OpenPlaylistStoryAsync(story, playlist);");
        StringAssert.Contains(luisterPage, "private async Task OpenPlaylistStoryAsync(MobileStorySummary story, MobilePlaylist playlist)");
        StringAssert.Contains(luisterPage, "await CapturePlayerTransitionBackdropAsync();");
        StringAssert.Contains(luisterPage, "private async Task CapturePlayerTransitionBackdropAsync()");
        StringAssert.Contains(luisterPage, "await _transitionBackdropState.CaptureAsync();");
        StringAssert.Contains(luisterPage, "[\"playlistTitle\"] = playlist.Title");
        StringAssert.Contains(luisterPage, "[\"playlistSlug\"] = playlist.Slug");
        Assert.IsFalse(luisterPage.Contains("story => BuildLuisterStoryCarouselCard(story)", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("tap.Tapped += async (_, _) => await OpenStoryAsync(story);", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailUsesNativePlayerAndDirectR2AudioUrls()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var audioService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "AudioPlaybackService.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(program, "IStoryMediaStorageService storyMediaStorageService");
        StringAssert.Contains(program, "ResolveMobileAudioUrlAsync(");
        StringAssert.Contains(program, "storyMediaStorageService.CreateAudioReadUrlAsync(");
        StringAssert.Contains(program, "DurationSeconds: story.DurationSeconds");
        StringAssert.Contains(program, "decimal? DurationSeconds");
        StringAssert.Contains(models, "decimal? DurationSeconds");
        StringAssert.Contains(storyDetail, "IAudioPlaybackService _audioPlaybackService");
        StringAssert.Contains(storyDetail, "await _audioPlaybackService.PlayAsync(");
        StringAssert.Contains(storyDetail, "new AudioPlaybackMetadata(");
        StringAssert.Contains(storyDetail, "Shell.SetTabBarIsVisible(this, false)");
        StringAssert.Contains(storyDetail, "BuildCoverArt(detail)");
        StringAssert.Contains(storyDetail, "HeightRequest = CoverArtHeight");
        StringAssert.Contains(storyDetail, "return Math.Clamp(height * 0.36, 260, 330);");
        StringAssert.Contains(storyDetail, "BuildTransportControls(detail, playButton)");
        StringAssert.Contains(storyDetail, "HeightRequest = 82");
        StringAssert.Contains(storyDetail, "WidthRequest = 76");
        StringAssert.Contains(storyDetail, "private static double CoverArtHeight");
        StringAssert.Contains(storyDetail, "var height = ScreenHeight;");
        StringAssert.Contains(storyDetail, "_activeCatalogDuration = ResolveCatalogDuration(detail);");
        StringAssert.Contains(storyDetail, "_activeCatalogDuration is null ? \"--:--\" : FormatTime(_activeCatalogDuration.Value)");
        StringAssert.Contains(storyDetail, "private TimeSpan? ResolveCatalogDuration(MobileStoryDetailResponse detail) =>");
        StringAssert.Contains(storyDetail, "private decimal? ResolveCatalogDurationSeconds(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "if (detail.Story.DurationSeconds is > 0)");
        StringAssert.Contains(storyDetail, "_previewStory is { DurationSeconds: > 0 } previewStory");
        StringAssert.Contains(storyDetail, "var playlistStory = _playlistStories.FirstOrDefault");
        StringAssert.Contains(storyDetail, "var duration = _audioPlaybackService.Duration ?? _activeCatalogDuration;");
        StringAssert.Contains(storyDetail, "var durationSeconds = NormalizeTrackingSeconds(duration?.TotalSeconds);");
        StringAssert.Contains(storyDetail, "EnsureCatalogDurationVisibleAsync(detail);");
        StringAssert.Contains(storyDetail, "private void EnsureCatalogDurationVisibleAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "var audioUrl = _apiClient.BuildAbsoluteUrl(detail.AudioUrl);");
        StringAssert.Contains(storyDetail, "var shouldPrepareFirst = DeviceInfo.Current.Platform == DevicePlatform.Android;");
        StringAssert.Contains(storyDetail, "if (shouldPrepareFirst)");
        StringAssert.Contains(storyDetail, "duration = await _audioPlaybackService.GetDurationAsync(preparedAudioUrl, cancellationToken);");
        StringAssert.Contains(storyDetail, "duration = await _audioPlaybackService.GetDurationAsync(audioUrl, cancellationToken);");
        StringAssert.Contains(storyDetail, "if (duration is null && !cancellationToken.IsCancellationRequested)");
        StringAssert.Contains(storyDetail, "var preparedAudioUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(");
        StringAssert.Contains(storyDetail, "DownloadAudioForPlaybackAsync(");
        Assert.IsFalse(storyDetail.Contains("Gereed om te luister", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Onderbreek", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Besig om te speel", StringComparison.Ordinal));
        StringAssert.Contains(audioService, "TimeSpan CurrentPosition");
        StringAssert.Contains(audioService, "TimeSpan? Duration");
        StringAssert.Contains(audioService, "Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default);");
        StringAssert.Contains(audioService, "public async Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default)");
        StringAssert.Contains(audioService, "probePlayer = new AVFoundation.AVPlayer(playerItem);");
        StringAssert.Contains(audioService, "WaitUntilReadyToPlayAsync(playerItem)");
        StringAssert.Contains(audioService, "WaitUntilReadyToPlayAsync(playerItem, cancellationToken)");
        StringAssert.Contains(audioService, "AVFoundation.AVPlayerItemStatus.ReadyToPlay");
        StringAssert.Contains(audioService, "AVFoundation.AVPlayerItemStatus.Failed");
        StringAssert.Contains(audioService, "AVFoundation.AVAudioSessionCategory.Playback");
        StringAssert.Contains(audioService, "MPRemoteCommandCenter.Shared");
        StringAssert.Contains(audioService, "MPNowPlayingInfoCenter.DefaultCenter.NowPlaying");
        StringAssert.Contains(audioService, "MediaPlayer.MPMediaItemArtwork");
        StringAssert.Contains(audioService, "LoadArtworkForMetadataAsync(metadata)");
        StringAssert.Contains(audioService, "info.Artwork = _artwork;");
        StringAssert.Contains(audioService, "GetByteArrayAsync(artworkUrl");
        StringAssert.Contains(audioService, "player.Error += (_, args) =>");
        StringAssert.Contains(audioService, "args.Handled = true;");
        StringAssert.Contains(audioService, "ready.TrySetException(new InvalidOperationException(\"Kon nie die audio stroom oopmaak nie.\"));");
        Assert.IsFalse(storyDetail.Contains("<audio id=\"story-audio\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("new WebView", StringComparison.Ordinal));
        StringAssert.Contains(audioService, "AVFoundation.AVPlayer");
        StringAssert.Contains(mauiProgram, "ConfigureEntryChrome();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();");
    }

    [TestMethod]
    public void MobileStoryDetailShowsWebStoryInfoCardAndStoryQuestions()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "Summary: story.Summary");
        StringAssert.Contains(program, "Lessons: story.Lessons ?? Array.Empty<string>()");
        StringAssert.Contains(program, "ValueTags: story.ValueTags ?? Array.Empty<string>()");
        StringAssert.Contains(program, "ConversationQuestions: story.ConversationQuestions ?? Array.Empty<string>()");
        StringAssert.Contains(program, "Characters: story.Characters ?? Array.Empty<string>()");
        StringAssert.Contains(program, "CharacterTiles: characterTiles");
        StringAssert.Contains(program, "CharacterUnlockEvaluator.EvaluateUnlockStates(");
        StringAssert.Contains(program, "TestQuestions: (story.TestQuestions ?? Array.Empty<StoryTestQuestion>())");
        StringAssert.Contains(program, "sealed record MobileStoryCharacterResponse(");
        StringAssert.Contains(program, "sealed record MobileStoryTestQuestionResponse(");

        StringAssert.Contains(models, "IReadOnlyList<string> Lessons,");
        StringAssert.Contains(models, "IReadOnlyList<string> ValueTags,");
        StringAssert.Contains(models, "IReadOnlyList<string> ConversationQuestions,");
        StringAssert.Contains(models, "IReadOnlyList<string> Characters,");
        StringAssert.Contains(models, "IReadOnlyList<MobileStoryCharacter> CharacterTiles,");
        StringAssert.Contains(models, "public sealed record MobileStoryCharacter(");
        StringAssert.Contains(models, "IReadOnlyList<MobileStoryTestQuestion> TestQuestions,");
        StringAssert.Contains(models, "public sealed record MobileStoryTestQuestion(");

        StringAssert.Contains(storyDetail, "_content.Children.Add(BuildStoryInfoCard(detail));");
        StringAssert.Contains(storyDetail, "StorySummaryCardColor = Color.FromArgb(\"#222222\")");
        StringAssert.Contains(storyDetail, "StorySummaryGoldColor = Color.FromArgb(\"#D4B075\")");
        StringAssert.Contains(storyDetail, "StorySummaryTestButtonColor = Color.FromArgb(\"#F3C86D\")");
        StringAssert.Contains(storyDetail, "BuildStoryInfoTextBlock(\"Waaroor gaan die storie?\", synopsis)");
        StringAssert.Contains(storyDetail, "BuildStoryInfoTagBlock(\"Waardes\", detail.ValueTags)");
        StringAssert.Contains(storyDetail, "BuildStoryInfoListBlock(\"Gesels 'n bietjie\", detail.ConversationQuestions)");
        StringAssert.Contains(storyDetail, "BuildStoryCharacterBlock(detail)");
        StringAssert.Contains(storyDetail, "private static View BuildStoryCharacterTile(MobileStoryCharacter character)");
        StringAssert.Contains(storyDetail, "async () => await ShowStoryTestModalAsync(detail),");
        StringAssert.Contains(storyDetail, "isPrimary: true");
        StringAssert.Contains(storyDetail, "private async Task ShowStoryTestModalAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "private void RenderStoryTestModalContent()");
        StringAssert.Contains(storyDetail, "private View BuildStoryTestQuestionCard(MobileStoryTestQuestion question, int questionIndex)");
        StringAssert.Contains(storyDetail, "private View BuildStoryTestOption(MobileStoryTestQuestion question, int questionIndex, string option, string? optionText)");
        StringAssert.Contains(storyDetail, "Kontroleer antwoorde");
        StringAssert.Contains(storyDetail, "BuildStoryTestScoreText(detail)");
    }

    [TestMethod]
    public void MobileIosDeclaresBackgroundAudioForNativePlayback()
    {
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));

        StringAssert.Contains(infoPlist, "<key>UIBackgroundModes</key>");
        StringAssert.Contains(infoPlist, "<string>audio</string>");
    }

    [TestMethod]
    public void MobileClientDownloadsProtectedWebsiteAudioBeforeNativePlayback()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(client, "PrepareAudioPlaybackSourceAsync(");
        StringAssert.Contains(client, "ShouldDownloadAudioForPlayback(playableUrl)");
        StringAssert.Contains(client, "DownloadAudioForPlaybackAsync(");
        StringAssert.Contains(client, "FileSystem.CacheDirectory");
        StringAssert.Contains(client, "HttpCompletionOption.ResponseHeadersRead");
        StringAssert.Contains(client, "uri.AbsolutePath.StartsWith(\"/media/audio/\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(client, "return new Uri(cachePath).AbsoluteUri;");
    }

    [TestMethod]
    public void MobileOfflineDownloadsUsePrivateDurableStorageAndAccessExpiry()
    {
        var service = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "OfflineStoryDownloadService.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var androidManifest = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "AndroidManifest.xml"));

        StringAssert.Contains(service, "public interface IOfflineStoryDownloadService");
        StringAssert.Contains(service, "public sealed record OfflineStoryDownload");
        StringAssert.Contains(service, "public enum OfflineDownloadState");
        StringAssert.Contains(service, "FileSystem.AppDataDirectory");
        StringAssert.Contains(service, "offline-story-audio");
        StringAssert.Contains(service, "offline-story-downloads.json");
        StringAssert.Contains(service, "LastAccessVerifiedAt");
        StringAssert.Contains(service, "AccessRefreshWindow = TimeSpan.FromDays(30)");
        StringAssert.Contains(service, "DeletePaidDownloadsAsync");
        StringAssert.Contains(service, "File.Move(temporaryPath, audioPath)");
        StringAssert.Contains(service, "DownloadAudioToFileAsync(");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<IOfflineStoryDownloadService, OfflineStoryDownloadService>();");
        StringAssert.Contains(androidManifest, "android.permission.ACCESS_NETWORK_STATE");
        Assert.IsFalse(service.Contains("FileSystem.CacheDirectory", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailOffersOfflineDownloadAndPrefersLocalPlayback()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "IOfflineStoryDownloadService offlineDownloadService");
        StringAssert.Contains(storyDetail, "_offlineDownloadService");
        StringAssert.Contains(storyDetail, "BuildDownloadPillButton(");
        StringAssert.Contains(storyDetail, "Download for offline listening");
        StringAssert.Contains(storyDetail, "Drawable = new DownloadIconDrawable()");
        StringAssert.Contains(storyDetail, "private sealed class DownloadIconDrawable : IDrawable");
        StringAssert.Contains(storyDetail, "new DownloadedIconDrawable()");
        StringAssert.Contains(storyDetail, "private sealed class DownloadedIconDrawable : IDrawable");
        Assert.IsFalse(storyDetail.Contains("Hierdie storie is gereed vir offline luister.", StringComparison.Ordinal));
        StringAssert.Contains(storyDetail, "HeightRequest = 42");
        StringAssert.Contains(storyDetail, "Laai af");
        StringAssert.Contains(storyDetail, "Afgelaai");
        StringAssert.Contains(storyDetail, "Verwyder aflaai");
        StringAssert.Contains(storyDetail, "ConfirmCellularDownloadAsync()");
        StringAssert.Contains(storyDetail, "ResolvePlayableAudioAsync(");
        StringAssert.Contains(storyDetail, "RenderOfflineDetail(");
        StringAssert.Contains(storyDetail, "Hierdie aflaai moet weer aanlyn bevestig word.");
    }

    [TestMethod]
    public void MobileLuisterRendersDownloadedOfflineSection()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "IOfflineStoryDownloadService offlineDownloadService");
        StringAssert.Contains(luisterPage, "_offlineDownloadService");
        StringAssert.Contains(luisterPage, "_downloadedStories");
        StringAssert.Contains(luisterPage, "ShouldShowInlineDownloadedSection()");
        StringAssert.Contains(luisterPage, "Connectivity.Current.NetworkAccess != NetworkAccess.Internet");
        StringAssert.Contains(luisterPage, "BuildDownloadedSection()");
        StringAssert.Contains(luisterPage, "Afgelaai");
        StringAssert.Contains(luisterPage, "GetPlayableDownloadsAsync()");
        StringAssert.Contains(luisterPage, "RefreshDownloadsInBackgroundAsync()");
        StringAssert.Contains(luisterPage, "OpenDownloadedStoryAsync(");
        StringAssert.Contains(luisterPage, "source={Uri.EscapeDataString(download.Source)}");
        Assert.IsFalse(luisterPage.Contains(".GetAwaiter()", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains(".GetResult()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterMenuOpensDownloadedStoriesPage()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var downloadedPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "DownloadedPage.cs"));
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(luisterPage, "\"Downloaded\", \"Settings\", \"Manage Account\"");
        StringAssert.Contains(luisterPage, "await Shell.Current.GoToAsync(nameof(DownloadedPage), animate: true)");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(DownloadedPage), typeof(DownloadedPage));");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<DownloadedPage>();");
        StringAssert.Contains(downloadedPage, "public sealed class DownloadedPage : ContentPage");
        StringAssert.Contains(downloadedPage, "IOfflineStoryDownloadService offlineDownloadService");
        StringAssert.Contains(downloadedPage, "GetPlayableDownloadsAsync()");
        StringAssert.Contains(downloadedPage, "OpenDownloadedStoryAsync(");
        StringAssert.Contains(downloadedPage, "Stories gereed vir offline luister.");
    }

    [TestMethod]
    public void MobileStoryAudioUsesSignedMediaRouteForR2AndLocalProviders()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "static Task<string?> ResolveMobileAudioUrlAsync(");
        StringAssert.Contains(program, "return Task.FromResult<string?>(ToAbsoluteUri(httpContext, audioAccessService.CreateSignedAudioUrl(story.Slug)));");
        Assert.IsFalse(program.Contains("storyMediaStorageService.CreateAudioReadUrlAsync(\r\n            story.AudioBucket", StringComparison.Ordinal));
        Assert.IsFalse(program.Contains("return readUri?.ToString();", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailOpensAndClosesWithoutSlowExtraWork()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var audioService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "AudioPlaybackService.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var gratisPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "GratisPage.cs"));
        var homePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "HomePage.cs"));

        StringAssert.Contains(storyDetail, "public void ApplyQueryAttributes(IDictionary<string, object> query)");
        StringAssert.Contains(storyDetail, "RenderPreview(_previewStory)");
        StringAssert.Contains(storyDetail, "await LoadAsync(showLoading: _previewStory is null, cancellationToken: _loadCts.Token)");
        StringAssert.Contains(storyDetail, "Shell.Current.GoToAsync(\"..\", animate: false)");
        StringAssert.Contains(storyDetail, "CancelActiveLoad();");
        StringAssert.Contains(storyDetail, "UnsubscribePlaybackEvents();");
        StringAssert.Contains(storyDetail, "TryStopAudioPlayback();");
        StringAssert.Contains(storyDetail, "if (cancellationToken.IsCancellationRequested || !_isPageActive)");
        StringAssert.Contains(storyDetail, "if (!_isPageActive)");
        StringAssert.Contains(storyDetail, "_isClosing");
        StringAssert.Contains(storyDetail, "BuildInlineLoadingState()");
        StringAssert.Contains(luisterPage, "animate: false");
        StringAssert.Contains(gratisPage, "animate: false");
        StringAssert.Contains(homePage, "animate: false");
        StringAssert.Contains(audioService, "_player?.Pause();");
        Assert.IsFalse(audioService.Contains("_player?.Seek(CoreMedia.CMTime.Zero)", StringComparison.Ordinal));
        StringAssert.Contains(program, "RelatedStories: Array.Empty<MobileStorySummaryResponse>()");
    }

    [TestMethod]
    public void MobileStoryDetailAnimatesCloseBeforeFastShellPop()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(storyDetail, "PrepareCloseBackdrop();");
        StringAssert.Contains(storyDetail, "private void PrepareCloseBackdrop()");
        StringAssert.Contains(storyDetail, "_closeBackdrop = new Image");
        StringAssert.Contains(storyDetail, "_playerSurface = new Grid");
        StringAssert.Contains(storyDetail, "_closeBackdrop.Margin = ResolveBackdropMargin();");
        StringAssert.Contains(storyDetail, "private Thickness ResolveBackdropMargin()");
        StringAssert.Contains(storyDetail, "var safeAreaInsets = iOSPage.GetSafeAreaInsets(this);");
        StringAssert.Contains(storyDetail, "var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;");
        StringAssert.Contains(storyDetail, "var systemBarInsets = insets.GetInsets(AndroidWindowInsets.Type.SystemBars());");
        StringAssert.Contains(storyDetail, "_closeBackdrop.IsVisible = true;");
        StringAssert.Contains(storyDetail, "await AnimateCloseAsync();");
        StringAssert.Contains(storyDetail, "private async Task AnimateCloseAsync()");
        StringAssert.Contains(storyDetail, "private const uint CloseAnimationDurationMs = 170;");
        StringAssert.Contains(storyDetail, "var closeDistance = Height > 0");
        StringAssert.Contains(storyDetail, "? Height + 40");
        StringAssert.Contains(storyDetail, "_playerSurface.TranslateToAsync(0, closeDistance, CloseAnimationDurationMs, Easing.CubicIn)");
        Assert.IsFalse(storyDetail.Contains("_content.FadeToAsync", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("_root.FadeToAsync", StringComparison.Ordinal));
        StringAssert.Contains(storyDetail, "await Shell.Current.GoToAsync(\"..\", animate: false);");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<PlayerTransitionBackdropState>();");
    }

    [TestMethod]
    public void MobileStoryDetailUsesCleanPlayerChromeWithoutQueueHintOrSaveSharePills()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "BuildDownCaretButton()");
        StringAssert.Contains(storyDetail, "DownCaretDrawable");
        StringAssert.Contains(storyDetail, "CastIconDrawable");
        StringAssert.Contains(storyDetail, "BuildFavoriteOverlay(detail)");
        StringAssert.Contains(storyDetail, "Text = detail.Story.IsFavorite ? \"♥\" : \"♡\"");
        StringAssert.Contains(storyDetail, "BuildInfoPillButton()");
        StringAssert.Contains(storyDetail, "Drawable = new InfoIconDrawable()");
        StringAssert.Contains(storyDetail, "Text = \"Info\"");
        StringAssert.Contains(storyDetail, "await ToggleFavoriteAsync(detail);");
        Assert.IsFalse(storyDetail.Contains("BuildQueueHint()", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("\"Jou ry\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("\"☰+  Stoor\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("\"↗\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("BuildTopIconButton(\"⌄\")", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Gunsteling\")", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailCoverArtCanOpenFullscreenImage()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var orientationService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "OrientationService.cs"));
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));
        var appDelegate = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "AppDelegate.cs"));

        StringAssert.Contains(storyDetail, "BuildFullscreenCoverButton()");
        StringAssert.Contains(storyDetail, "FullscreenIconDrawable");
        StringAssert.Contains(storyDetail, "HorizontalOptions = LayoutOptions.End");
        StringAssert.Contains(storyDetail, "VerticalOptions = LayoutOptions.End");
        StringAssert.Contains(storyDetail, "await ShowFullscreenCoverAsync(detail);");
        StringAssert.Contains(storyDetail, "private async Task ShowFullscreenCoverAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "Navigation.PushModalAsync(fullscreenPage, true)");
        StringAssert.Contains(storyDetail, "Aspect = Aspect.AspectFit");
        StringAssert.Contains(storyDetail, "fullscreenImageTap.Tapped += (_, _) => _ = ToggleFullscreenPlaybackAsync(detail);");
        StringAssert.Contains(storyDetail, "await Navigation.PopModalAsync(true)");
        StringAssert.Contains(storyDetail, "Padding = new Thickness(8)");
        StringAssert.Contains(storyDetail, "new ColumnDefinition(GridLength.Star)");
        StringAssert.Contains(storyDetail, "new ColumnDefinition(GridLength.Auto)");
        StringAssert.Contains(storyDetail, "BuildFullscreenMediaControls(detail)");
        StringAssert.Contains(storyDetail, "HeightRequest = 4");
        StringAssert.Contains(storyDetail, "BuildFullscreenTransportControls(detail, playButton)");
        StringAssert.Contains(storyDetail, "private async Task ToggleFullscreenPlaybackAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "BuildCompactPlaybackButton(playButton.Text)");
        StringAssert.Contains(storyDetail, "BuildCompactTransportButton(\"|‹\")");
        StringAssert.Contains(storyDetail, "RestoreFullscreenPlaybackUi(detail);");
        StringAssert.Contains(storyDetail, "IOrientationService _orientationService");
        StringAssert.Contains(storyDetail, "_orientationService.RequestLandscape();");
        StringAssert.Contains(storyDetail, "_orientationService.RequestPortrait();");
        StringAssert.Contains(storyDetail, "DeviceDisplay.Current.KeepScreenOn = true;");
        StringAssert.Contains(storyDetail, "DeviceDisplay.Current.KeepScreenOn = _wasKeepScreenOnBeforeFullscreen;");
        StringAssert.Contains(storyDetail, "fullscreenPage.Disappearing += (_, _) =>");
        StringAssert.Contains(storyDetail, "RestoreFullscreenCoverDeviceState();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<IOrientationService, OrientationService>();");
        StringAssert.Contains(orientationService, "public interface IOrientationService");
        StringAssert.Contains(orientationService, "RequestLandscape()");
        StringAssert.Contains(orientationService, "RequestPortrait()");
        StringAssert.Contains(infoPlist, "UIInterfaceOrientationLandscapeLeft");
        StringAssert.Contains(infoPlist, "UIInterfaceOrientationLandscapeRight");
        StringAssert.Contains(appDelegate, "GetSupportedInterfaceOrientations");
        StringAssert.Contains(appDelegate, "OrientationService.CurrentIosOrientationMask");
    }

    [TestMethod]
    public void MobileStoryDetailPlayerButtonsUseRealActionsOnly()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var playlistState = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "PlaylistPlaybackState.cs"));

        StringAssert.Contains(storyDetail, "menuButton.Clicked += async (_, _) => await ShowPlayerMenuAsync();");
        StringAssert.Contains(storyDetail, "await Share.Default.RequestAsync(new ShareTextRequest");
        StringAssert.Contains(storyDetail, "titleRow.GestureRecognizers.Add(tap);");
        StringAssert.Contains(storyDetail, "private MobileStorySummary? ResolvePreviousStory(");
        StringAssert.Contains(storyDetail, "private MobileStorySummary? ResolveNextStory(");
        StringAssert.Contains(storyDetail, "BuildPlaybackModeRow(detail)");
        StringAssert.Contains(storyDetail, "BuildPlaybackModeButton(");
        StringAssert.Contains(storyDetail, "\"Auto\"");
        StringAssert.Contains(storyDetail, "\"Skommel\"");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.SetAutoplay(!_playlistPlaybackState.IsAutoplayEnabled);");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.SetShuffle(!_playlistPlaybackState.IsShuffleEnabled, detail.Story);");
        StringAssert.Contains(storyDetail, "await OpenPlaylistStoryAsync(previousStory, autoplay: ShouldAutoplaySelection());");
        StringAssert.Contains(storyDetail, "await OpenPlaylistStoryAsync(nextStory, autoplay: ShouldAutoplaySelection());");
        StringAssert.Contains(storyDetail, "await ReplaceActiveStoryAsync(nextStory, autoplay: ShouldAutoplaySelection());");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.IsAutoplayEnabled && _currentDetail is { } currentDetail");
        StringAssert.Contains(storyDetail, "await ReplaceActiveStoryAsync(nextStory, autoplay: true);");
        StringAssert.Contains(playlistState, "public bool IsAutoplayEnabled { get; private set; }");
        StringAssert.Contains(playlistState, "public bool IsShuffleEnabled { get; private set; }");
        StringAssert.Contains(playlistState, "public IReadOnlyList<MobileStorySummary> GetPlaybackStories(MobileStorySummary? currentStory = null)");
    }

    [TestMethod]
    public void MobileStoryDetailPlaylistQueueReplacesCurrentStoryInPlace()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "private async Task ReplaceActiveStoryAsync(MobileStorySummary story, bool autoplay = false)");
        StringAssert.Contains(storyDetail, "await ReplaceActiveStoryAsync(story, autoplay);");
        StringAssert.Contains(storyDetail, "_pendingAutoplayAfterLoad = autoplay;");
        StringAssert.Contains(storyDetail, "StorySlug = story.Slug;");
        StringAssert.Contains(storyDetail, "Source = story.Source;");
        StringAssert.Contains(storyDetail, "RenderPreview(story);");
        StringAssert.Contains(storyDetail, "await LoadAsync(showLoading: false, cancellationToken: _loadCts.Token);");

        var methodStart = storyDetail.IndexOf("private async Task OpenPlaylistStoryAsync", StringComparison.Ordinal);
        Assert.IsTrue(methodStart >= 0);
        var methodEnd = storyDetail.IndexOf("\n    private sealed class", methodStart, StringComparison.Ordinal);
        Assert.IsTrue(methodEnd > methodStart);
        var playlistMethod = storyDetail[methodStart..methodEnd];

        Assert.IsFalse(playlistMethod.Contains("Shell.Current.GoToAsync", StringComparison.Ordinal));
        Assert.IsFalse(playlistMethod.Contains("StoryDetailPage?slug=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobilePlaylistPlaybackStateKeepsShuffleOrderAndAutoplayFlags()
    {
        var playlistState = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "PlaylistPlaybackState.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(playlistState, "private IReadOnlyList<string> _shuffleOrder = Array.Empty<string>();");
        StringAssert.Contains(playlistState, "public void Set(MobilePlaylist playlist, MobileStorySummary? currentStory = null)");
        StringAssert.Contains(playlistState, "public void SetAutoplay(bool isEnabled)");
        StringAssert.Contains(playlistState, "public void SetShuffle(bool isEnabled, MobileStorySummary? currentStory = null)");
        StringAssert.Contains(playlistState, "RefreshShuffleOrder(currentStory);");
        StringAssert.Contains(playlistState, "OrderBy(_ => Random.Shared.Next())");
        StringAssert.Contains(playlistState, "remainingKeys.Insert(0, currentStoryKey);");
        StringAssert.Contains(storyDetail, "return _playlistPlaybackState.GetPlaybackStories(currentStory);");
    }

    [TestMethod]
    public void MobileStoryDetailCastSheetUsesPlatformControlsAndSwipeDismiss()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "if (IsNativeRoutePickerAvailable)");
        StringAssert.Contains(storyDetail, "private static bool IsNativeRoutePickerAvailable");
        StringAssert.Contains(storyDetail, "DeviceInfo.Platform == DevicePlatform.iOS");
        StringAssert.Contains(storyDetail, "DeviceInfo.Platform == DevicePlatform.MacCatalyst");
        StringAssert.Contains(storyDetail, "BuildCastAvailableControlsHeader()");
        StringAssert.Contains(storyDetail, "new SwipeGestureRecognizer { Direction = SwipeDirection.Down }");
        StringAssert.Contains(storyDetail, "swipeDown.Swiped += (_, _) => DismissCastPicker();");
        StringAssert.Contains(storyDetail, "\"AirPlay and Bluetooth devices\"");
        Assert.IsFalse(storyDetail.Contains("\"Living Room Speaker\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("BuildCastAllDevicesHeader()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileAuthFieldsRemoveNativeEntryChromeInsideRoundedBorders()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(accountPage, "entry.BackgroundColor = Colors.Transparent;");
        StringAssert.Contains(luisterPage, "Shell.SetNavBarIsVisible(this, false);");
        StringAssert.Contains(accountPage, "Content = entry");
        StringAssert.Contains(mauiProgram, "ConfigureEntryChrome();");
        StringAssert.Contains(mauiProgram, "EntryHandler.Mapper.AppendToMapping(\"SchinkPlainEntryChrome\"");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.Background = null;");
    }

    [TestMethod]
    public void MobileAuthFormModesCenterPanelOnScreen()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "_authPanelTopSpacer");
        StringAssert.Contains(accountPage, "protected override void OnSizeAllocated(double width, double height)");
        StringAssert.Contains(accountPage, "UpdateAuthPanelTopSpacer();");
        StringAssert.Contains(accountPage, "_signedOutState.Children.Add(_authPanelTopSpacer);");
        StringAssert.Contains(accountPage, "if (_authPanelMode == AuthPanelMode.Landing)");
        StringAssert.Contains(accountPage, "var estimatedPanelHeight = _authPanelMode == AuthPanelMode.SignIn ? 330 : 620;");
        StringAssert.Contains(accountPage, "Math.Floor((screenHeight - estimatedPanelHeight) / 2)");
        StringAssert.Contains(accountPage, "_authPanelTopSpacer.IsVisible = true;");
    }

    [TestMethod]
    public void MobileAuthFormModesHaveBackButtonToLanding()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "BuildAuthPanelHeader(");
        StringAssert.Contains(accountPage, "var backIcon = new GraphicsView");
        StringAssert.Contains(accountPage, "Drawable = new BackChevronDrawable()");
        StringAssert.Contains(accountPage, "private sealed class BackChevronDrawable");
        StringAssert.Contains(accountPage, "canvas.DrawLine(21.5f, 12.5f, 15.5f, 19f);");
        StringAssert.Contains(accountPage, "canvas.DrawLine(15.5f, 19f, 21.5f, 25.5f);");
        Assert.IsFalse(accountPage.Contains("Source = \"auth_caret_dark_rendered.png\"", StringComparison.Ordinal));
        Assert.IsFalse(accountPage.Contains("Rotation = 180", StringComparison.Ordinal));
        StringAssert.Contains(accountPage, "WidthRequest = 38");
        StringAssert.Contains(accountPage, "Content = new Grid");
        StringAssert.Contains(accountPage, "new ColumnDefinition { Width = 38 }");
        StringAssert.Contains(accountPage, "SetAuthPanelMode(AuthPanelMode.Landing);");
        Assert.IsFalse(accountPage.Contains("BuildAuthPanelHeading(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileApiErrorsExtractServerMessageFromJsonBody()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(client, "ExtractErrorMessage(body)");
        StringAssert.Contains(client, "JsonDocument.Parse(body)");
        StringAssert.Contains(client, "TryGetProperty(\"message\", out var messageElement)");
        StringAssert.Contains(client, "return message;");
        Assert.IsFalse(client.Contains(": body);", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileWelcomeScreenUsesResponsiveLandingMetrics()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "private sealed record LandingLayoutMetrics(");
        StringAssert.Contains(accountPage, "GetLandingLayoutMetrics()");
        StringAssert.Contains(accountPage, "var compact = height < 740;");
        StringAssert.Contains(accountPage, "var tight = height < 680;");
        StringAssert.Contains(accountPage, "Text = \"Bou jou kind se karakter -\"");
        StringAssert.Contains(accountPage, "Text = \"\\neen storie op 'n slag.\"");
        StringAssert.Contains(accountPage, "FontAttributes = FontAttributes.Italic");
        StringAssert.Contains(accountPage, "Text = \"Rustige, opbouende \"");
        StringAssert.Contains(accountPage, "Text = \"Afrikaanse storietyd\"");
        StringAssert.Contains(accountPage, "Text = \"Minder skerms. Rustiger aande. Stories wat waardes bou.\"");
        StringAssert.Contains(accountPage, "Source = \"schink_stories_home_hero.png\"");
        StringAssert.Contains(accountPage, "LogoHeight: Math.Clamp(height * (tight ? 0.135 : 0.155), 96, 144)");
        StringAssert.Contains(accountPage, "TitleSublineFontSize: Math.Clamp(height * (tight ? 0.035 : 0.039), 23, 32)");
        StringAssert.Contains(accountPage, "TitleMargin: new Thickness(0, tight ? -34 : compact ? -42 : -52, 0, 0)");
        StringAssert.Contains(accountPage, "CharacterHeight: Math.Clamp(height * (tight ? 0.23 : 0.27), 154, 246)");
        StringAssert.Contains(accountPage, "ModeButtonHeight: tight ? 64 : compact ? 70 : 78");
        StringAssert.Contains(accountPage, "RowSpacing = metrics.PanelContentSpacing");
        StringAssert.Contains(accountPage, "ApplyLandingLayoutMetrics();");
    }

    [TestMethod]
    public void MobileAuthLandingButtonsRenderIconRowsOnAndroid()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "var icon = new Image");
        StringAssert.Contains(accountPage, "? \"auth_icon_user_white_rendered.png\"");
        StringAssert.Contains(accountPage, ": \"auth_icon_pencil_gold_rendered.png\"");
        StringAssert.Contains(accountPage, "var label = new Label");
        StringAssert.Contains(accountPage, "var button = new Border");
        StringAssert.Contains(accountPage, "HorizontalOptions = LayoutOptions.Fill");
        StringAssert.Contains(accountPage, "VerticalOptions = LayoutOptions.Fill");
        Assert.IsFalse(accountPage.Contains("ImageSource = mode == AuthPanelMode.SignIn ? \"auth_icon_user_white_rendered.png\" : \"auth_icon_pencil_gold_rendered.png\"", StringComparison.Ordinal));
        Assert.IsFalse(accountPage.Contains("ContentLayout = new Button.ButtonContentLayout", StringComparison.Ordinal));
        StringAssert.Contains(accountPage, "BackgroundColor = isPrimary ? Color.FromArgb(\"#146D69\") : Color.FromArgb(\"#FFFCF5\")");
        StringAssert.Contains(accountPage, "Stroke = isPrimary ? Color.FromArgb(\"#146D69\") : Color.FromArgb(\"#E8B52F\")");
        StringAssert.Contains(accountPage, "StrokeShape = new RoundRectangle { CornerRadius = 26 }");
        StringAssert.Contains(accountPage, "var buttonHeight = isLanding ? metrics.ModeButtonHeight : 78;");
        StringAssert.Contains(accountPage, "HeightRequest = buttonHeight");
        StringAssert.Contains(accountPage, "MinimumHeightRequest = buttonHeight");
        StringAssert.Contains(accountPage, "tap.Tapped += (_, _) => SetAuthPanelMode(mode);");
        StringAssert.Contains(accountPage, "button.GestureRecognizers.Add(tap);");
        StringAssert.Contains(accountPage, "return button;");
        Assert.IsFalse(accountPage.Contains("var hitTarget = new Button", StringComparison.Ordinal));
        Assert.IsFalse(accountPage.Contains("Opacity = 0.01", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileSignInSupportsGoogleOAuthDeepLinkFlow()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var apiClient = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var iOSInfo = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));
        var androidCallback = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "GoogleAuthCallbackActivity.cs"));
        var googleIcon = File.ReadAllText(GetRepoPath("Shink.Mobile", "Resources", "Images", "google_g.svg"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(accountPage, "Teken in met Google");
        StringAssert.Contains(accountPage, "Source = ImageSource.FromFile(\"google_g.svg\")");
        StringAssert.Contains(googleIcon, "fill=\"#4285F4\"");
        StringAssert.Contains(googleIcon, "fill=\"#34A853\"");
        StringAssert.Contains(googleIcon, "fill=\"#FBBC05\"");
        StringAssert.Contains(googleIcon, "fill=\"#EA4335\"");
        Assert.IsFalse(accountPage.Contains("GoogleLogoDrawable", StringComparison.Ordinal));
        StringAssert.Contains(accountPage, "WebAuthenticator.Default.AuthenticateAsync(");
        StringAssert.Contains(accountPage, "_apiClient.BuildGoogleSignInStartUri()");
        StringAssert.Contains(accountPage, "new Uri(MobileApiClient.GoogleCallbackUrl)");
        StringAssert.Contains(accountPage, "CompleteGoogleSignInAsync(token)");
        StringAssert.Contains(apiClient, "public const string GoogleCallbackUrl = \"schinkstories://auth/google\";");
        StringAssert.Contains(apiClient, "BuildUri(\"/api/mobile/auth/google/start\")");
        StringAssert.Contains(apiClient, "\"/api/mobile/auth/google/complete\"");
        StringAssert.Contains(iOSInfo, "<string>schinkstories</string>");
        StringAssert.Contains(androidCallback, "WebAuthenticatorCallbackActivity");
        StringAssert.Contains(androidCallback, "DataScheme = \"schinkstories\"");
        StringAssert.Contains(androidCallback, "DataHost = \"auth\"");
        StringAssert.Contains(androidCallback, "DataPath = \"/google\"");
        StringAssert.Contains(program, "app.MapGet(\"/api/mobile/auth/google/start\"");
        StringAssert.Contains(program, "app.MapGet(\"/auth/mobile/google/callback\"");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/auth/google/complete\"");
        StringAssert.Contains(program, "MobileGoogleAuthTokenProtectorPurpose");
        StringAssert.Contains(program, "MobileGoogleCallbackUrl = \"schinkstories://auth/google\"");
    }

    [TestMethod]
    public void MobileWelcomeLogoForcesTransparentImageBackground()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(project, "<MauiAsset Include=\"Resources/Images/schink_stories_logo_white.png\" LogicalName=\"schink_stories_logo_white_raw.png\" />");
        StringAssert.Contains(accountPage, "CreatePackageImageSource(\"schink_stories_logo_white_raw.png\")");
        StringAssert.Contains(accountPage, "FileSystem.OpenAppPackageFileAsync(fileName)");
        StringAssert.Contains(accountPage, "BackgroundColor = Colors.Transparent");
    }

    [TestMethod]
    public void MobileWelcomeLogoKeepsTransparentBackground()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var logoBytes = File.ReadAllBytes(GetRepoPath("Shink.Mobile", "Resources", "Images", "schink_stories_logo_white.png"));

        StringAssert.Contains(project, "<MauiImage Update=\"Resources/Images/schink_stories_logo_white.png\" Resize=\"False\" />");
        StringAssert.Contains(accountPage, "CreatePackageImageSource(\"schink_stories_logo_white_raw.png\")");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, logoBytes.Take(4).ToArray());
        Assert.AreEqual(6, logoBytes[25]);
    }

    [TestMethod]
    public void MobileAccountPageAppliesCachedSessionBeforeRendering()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "var hasCachedSession = _sessionState.Current.IsSignedIn;");
        StringAssert.Contains(accountPage, "_signedInState = new VerticalStackLayout { Spacing = 0, IsVisible = hasCachedSession };");
        StringAssert.Contains(accountPage, "_signedOutState = new VerticalStackLayout { Spacing = 0, IsVisible = !hasCachedSession };");
        StringAssert.Contains(accountPage, "if (_signedOutState.Children.Count == 0)");
        StringAssert.Contains(accountPage, "_sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(ApplySessionState);");
        StringAssert.Contains(accountPage, "_signedInState.IsVisible = true;");
        StringAssert.Contains(accountPage, "_signedOutState.IsVisible = false;");
    }

    [TestMethod]
    public void MobileAppIconUsesGeneratedWordlessSchinkAsset()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));
        var androidManifest = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "AndroidManifest.xml"));
        var androidMainActivity = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "MainActivity.cs"));
        var iconPath = GetRepoPath("Shink.Mobile", "Resources", "AppIcon", "schink_appicon.png");
        var iconBytes = File.ReadAllBytes(iconPath);

        StringAssert.Contains(project, "<MauiIcon Include=\"Resources/AppIcon/schink_appicon.png\" />");
        StringAssert.Contains(infoPlist, "<key>XSAppIconAssets</key>");
        StringAssert.Contains(infoPlist, "<string>Assets.xcassets/schink_appicon.appiconset</string>");
        StringAssert.Contains(androidManifest, "android:icon=\"@mipmap/schink_appicon\"");
        StringAssert.Contains(androidManifest, "android:roundIcon=\"@mipmap/schink_appicon_round\"");
        StringAssert.Contains(androidMainActivity, "Icon = \"@mipmap/schink_appicon\"");
        StringAssert.Contains(androidMainActivity, "RoundIcon = \"@mipmap/schink_appicon_round\"");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, iconBytes.Take(4).ToArray());
        Assert.IsTrue(iconBytes.Length > 100_000);
    }

    [TestMethod]
    public void MobileSplashScreenUsesGeneratedSchinkStoriesArtwork()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var splashPath = GetRepoPath("Shink.Mobile", "Resources", "Splash", "schink_stories_logo_white.png");
        var splashBytes = File.ReadAllBytes(splashPath);

        StringAssert.Contains(project, "<MauiSplashScreen Include=\"Resources/Splash/schink_stories_logo_white.png\" Color=\"#023333\" BaseSize=\"320,140\" />");
        Assert.IsFalse(project.Contains("<MauiSplashScreen Include=\"Resources/Splash/splash.svg\"", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, splashBytes.Take(4).ToArray());
        Assert.IsTrue(splashBytes.Length > 20_000);
    }

    [TestMethod]
    public void MobileVisibleBrandCopyUsesSchinkSpelling()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var mainPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "MainPage.xaml"));

        StringAssert.Contains(project, "<ApplicationTitle>Schink Stories</ApplicationTitle>");
        StringAssert.Contains(mainPage, "Text=\"Schink Stories\"");
        StringAssert.Contains(mainPage, "bestaande Schink Stories dienste");
        Assert.IsFalse(project.Contains(">Shink Stories<", StringComparison.Ordinal));
        Assert.IsFalse(mainPage.Contains("Shink Stories", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterShowsContinueListeningCardFromSavedPlaybackState()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var continueListeningState = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "ContinueListeningState.cs"));

        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<ContinueListeningState>();");
        StringAssert.Contains(continueListeningState, "public sealed class ContinueListeningState");
        StringAssert.Contains(continueListeningState, "Preferences.Default.Set(PreferenceKey");
        StringAssert.Contains(continueListeningState, "public void UpdateProgress(");
        StringAssert.Contains(continueListeningState, "public void Clear()");
        StringAssert.Contains(continueListeningState, "var preservedDurationSeconds = current is not null");
        StringAssert.Contains(continueListeningState, "NormalizeSeconds(durationSeconds) ?? story.DurationSeconds ?? preservedDurationSeconds");
        StringAssert.Contains(storyDetail, "ContinueListeningState continueListeningState");
        StringAssert.Contains(storyDetail, "SaveContinueListening(detail);");
        StringAssert.Contains(storyDetail, "_continueListeningState.UpdateProgress(");
        StringAssert.Contains(luisterPage, "BuildContinueListeningCard()");
        StringAssert.Contains(luisterPage, "\"Gaan voort met luister\"");
        StringAssert.Contains(luisterPage, "Text = \"Maak skoon\"");
        StringAssert.Contains(luisterPage, "clearButton.Clicked += (_, _) => ClearContinueListening();");
        StringAssert.Contains(luisterPage, "private void ClearContinueListening()");
        StringAssert.Contains(luisterPage, "_continueListeningState.Clear();");
        StringAssert.Contains(luisterPage, "ResolveContinueListeningStory(item)");
        StringAssert.Contains(luisterPage, "await OpenContinueListeningAsync(item)");
        StringAssert.Contains(luisterPage, "MergeContinueListeningMetadata(resolvedStory.Value.Story, item)");
        StringAssert.Contains(luisterPage, "DurationSeconds = story.DurationSeconds is > 0 ? story.DurationSeconds : item.DurationSeconds");
        StringAssert.Contains(luisterPage, "_playlistContent.Children.Add(continueListeningCard);");
    }

    [TestMethod]
    public void MobileAuthCookiesPersistForApkUpdateDemoInstalls()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var agents = File.ReadAllText(GetRepoPath("AGENTS.md"));

        StringAssert.Contains(client, "private readonly CookieContainer _cookieContainer;");
        StringAssert.Contains(client, "SecureStorage.Default.GetAsync(BuildAuthCookieStorageKey())");
        StringAssert.Contains(client, "SecureStorage.Default.SetAsync(BuildAuthCookieStorageKey(), serializedCookies)");
        StringAssert.Contains(client, "await EnsureAuthCookiesLoadedAsync(cancellationToken);");
        StringAssert.Contains(client, "await SaveAuthCookiesAsync(cancellationToken);");
        StringAssert.Contains(client, "await ClearPersistedAuthCookiesAsync();");
        StringAssert.Contains(client, "private sealed record PersistedAuthCookie(");

        StringAssert.Contains(project, "<ApplicationId>com.schink.stories.mobile</ApplicationId>");
        StringAssert.Contains(project, "<ApplicationVersion>2</ApplicationVersion>");
        StringAssert.Contains(project, "<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>");

        StringAssert.Contains(agents, "Keep the mobile package ID fixed at `com.schink.stories.mobile`.");
        StringAssert.Contains(agents, "same stable release/demo keystore");
        StringAssert.Contains(agents, "`ApplicationVersion` before producing every shareable APK");
        StringAssert.Contains(agents, "install the new APK over the old one instead of uninstalling first");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(segments)} from {AppContext.BaseDirectory}.");
    }
}
