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
        StringAssert.Contains(luisterPage, "BuildLuisterImageSource(playlist.ArtworkUrl, \"schink_background.jpeg\")");
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
        StringAssert.Contains(luisterPage, "_feedView!.ScrollTo(0, position: ScrollToPosition.Start, animate: false);");
        StringAssert.Contains(luisterPage, "if (!_hasLoaded || !_isPageActive || Handler is null)");
        StringAssert.Contains(luisterPage, "HandlerChanged += (_, _) =>");
        StringAssert.Contains(luisterPage, "if (_isPageActive && _hasLoaded)");
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
    public void MobileLuisterUsesPersistentNativeStyleAppBarAndBottomBar()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var bottomBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));

        StringAssert.Contains(luisterPage, "private readonly CollectionView? _feedView;");
        StringAssert.Contains(luisterPage, "ItemsSource = Array.Empty<LuisterFeedItem>()");
        StringAssert.Contains(luisterPage, "private readonly Grid _topBarOverlay;");
        StringAssert.Contains(luisterPage, "private readonly Grid _bottomBarOverlay;");
        StringAssert.Contains(luisterPage, "private readonly Border _floatingTopBarHost;");
        StringAssert.Contains(luisterPage, "private readonly ContentView _bottomBarHost;");
        StringAssert.Contains(luisterPage, "_topBarOverlay = new Grid");
        StringAssert.Contains(luisterPage, "_bottomBarOverlay = new Grid");
        StringAssert.Contains(luisterPage, "ZIndex = 100");
        StringAssert.Contains(luisterPage, "_refreshView,\n                _topBarOverlay,\n                _bottomBarOverlay");
        StringAssert.Contains(luisterPage, "RenderFloatingTopBar();");
        StringAssert.Contains(luisterPage, "RenderBottomBar();");
        StringAssert.Contains(luisterPage, "_topBarOverlay.Children.Add(_floatingTopBarHost);");
        StringAssert.Contains(luisterPage, "return MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(luisterPage, "notificationAction: ShowNotificationsAsync");
        StringAssert.Contains(luisterPage, "Header = BuildStoriesPageHeader(),");
        StringAssert.Contains(luisterPage, "ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems");
        StringAssert.Contains(bottomBar, "Label: \"Soek\"");
        StringAssert.Contains(bottomBar, "Label: \"Afgelaai\"");
        StringAssert.Contains(bottomBar, "Label: \"Karakters\"");
        StringAssert.Contains(bottomBar, "SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(bottomBar, "nameof(DownloadedPage)");
        StringAssert.Contains(bottomBar, "OpenRouteAsync(\"//Karakters\")");
        Assert.IsFalse(luisterPage.Contains("OnContentScrolled", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("SetTopBarHidden", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("_content.Children.Add(BuildLuisterTopBar());", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("private readonly ScrollView _scrollView;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileSignedInShellUsesSharedBottomNavigationWithoutShellTabDuplication()
    {
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var bottomBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));

        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));");
        StringAssert.Contains(appShell, "ContentTemplate = new DataTemplate(() => _services.GetRequiredService<LuisterPage>())");
        StringAssert.Contains(luisterPage, "Shell.Current.GoToAsync(nameof(AccountPage), animate: true)");
        StringAssert.Contains(luisterPage, "Shell.Current.GoToAsync(nameof(ProfilePage), animate: true)");
        StringAssert.Contains(mobileTopBar, "Shell.Current.GoToAsync(nameof(AccountPage), animate: true)");
        StringAssert.Contains(mobileTopBar, "Shell.Current.GoToAsync(nameof(ProfilePage), animate: true)");
        StringAssert.Contains(luisterPage, "MobileBottomBar.Build(this, \"listen\", OpenStoriesSearchAsync)");
        StringAssert.Contains(bottomBar, "Destination: \"search\"");
        StringAssert.Contains(bottomBar, "Destination: \"downloads\"");
        StringAssert.Contains(bottomBar, "Destination: \"characters\"");
        Assert.IsFalse(appShell.Contains("new TabBar()", StringComparison.Ordinal));
        Assert.IsFalse(appShell.Contains("CreateTab(", StringComparison.Ordinal));
        Assert.IsFalse(appShell.Contains("SetTabBar", StringComparison.Ordinal));
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
        StringAssert.Contains(luisterPage, "return MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(mobileTopBar, "profileTap.Tapped += async (_, _) => await navigationGate.RunAsync(OpenProfileAsync);");
        StringAssert.Contains(mobileTopBar, "var navigationGate = new NavigationGate();");
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
        StringAssert.Contains(luisterPage, "nextItems.Add(LuisterFeedItem.Account());");
        Assert.IsFalse(luisterPage.Contains("\"Alles oop\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("\"Gratis toegang\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("Text = session.Email ?? \"Ingeteken\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("BuildSignedInAccountSummary", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterTopBarMirrorsWebNotificationCenter()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var bottomBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));

        StringAssert.Contains(luisterPage, "notificationAction: ShowNotificationsAsync");
        StringAssert.Contains(mobileTopBar, "BuildNotificationButton(notificationCount)");
        StringAssert.Contains(mobileTopBar, "notificationAction");
        StringAssert.Contains(bottomBar, "Destination: \"search\"");
        StringAssert.Contains(luisterPage, "_notificationPage?.UnreadCount");
        StringAssert.Contains(mobileTopBar, "unreadCount > 99 ? \"99+\"");
        StringAssert.Contains(luisterPage, "NotificationBadgeRefreshInterval = TimeSpan.FromSeconds(45)");
        StringAssert.Contains(luisterPage, "private IDispatcherTimer? _notificationRefreshTimer;");
        StringAssert.Contains(luisterPage, "_apiClient.NewNotificationsAvailable += OnNewNotificationsAvailable;");
        StringAssert.Contains(luisterPage, "_apiClient.NewNotificationsAvailable -= OnNewNotificationsAvailable;");
        StringAssert.Contains(luisterPage, "private void OnNewNotificationsAvailable(int count)");
        StringAssert.Contains(luisterPage, "_ = RefreshNotificationsInBackgroundAsync());");
        StringAssert.Contains(luisterPage, "StartNotificationRefreshTimer();");
        StringAssert.Contains(luisterPage, "StopNotificationRefreshTimer();");
        StringAssert.Contains(luisterPage, "private void StartNotificationRefreshTimer()");
        StringAssert.Contains(luisterPage, "_notificationRefreshTimer.Tick += (_, _) =>");
        StringAssert.Contains(luisterPage, "private async Task ShowNotificationsAsync()");
        StringAssert.Contains(luisterPage, "await _apiClient.GetNotificationsAsync(");
        StringAssert.Contains(luisterPage, "_ = TryMarkAllNotificationsReadAsync();");
        StringAssert.Contains(luisterPage, "MarkAllNotificationsReadLocally();");
        StringAssert.Contains(luisterPage, "var markReadTask = TryMarkNotificationReadAsync(notification.Id);");
        StringAssert.Contains(luisterPage, "await markReadTask;");
        StringAssert.Contains(luisterPage, "private async Task TryMarkNotificationReadAsync(Guid notificationId)");
        StringAssert.Contains(luisterPage, "Dié kennisgewing kon nie nou oopmaak nie.");
        StringAssert.Contains(luisterPage, "RenderContent();");
        StringAssert.Contains(luisterPage, "await _apiClient.ClearNotificationsAsync(cancellationToken)");
        StringAssert.Contains(luisterPage, "await _apiClient.ClearNotificationAsync(notification.Id, cancellationToken)");
        StringAssert.Contains(luisterPage, "if (before is null && !currentPage.HasHistory)");
        StringAssert.Contains(luisterPage, "history: currentPage.HasHistory,");
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
        StringAssert.Contains(mobileTopBar, "AutomationId = \"mobile-top-notifications\"");
        StringAssert.Contains(mobileTopBar, "SemanticProperties.SetDescription(container, \"Kennisgewings\")");
    }

    [TestMethod]
    public void MobileNotificationModalGuardsOpenCloseLifecycle()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var modalStart = luisterPage.IndexOf("private async Task ShowNotificationsAsync()", StringComparison.Ordinal);
        var modalEnd = luisterPage.IndexOf("private async Task TryMarkAllNotificationsReadAsync()", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, modalStart);
        Assert.IsGreaterThan(modalStart, modalEnd);
        var modalSource = luisterPage[modalStart..modalEnd];

        StringAssert.Contains(modalSource, "_notificationModalPage is not null || _isOpeningNotificationModal");
        StringAssert.Contains(modalSource, "modal.Disappearing += (_, _) => EndNotificationModalSession(modal)");
        StringAssert.Contains(modalSource, "await Navigation.PushModalAsync(modal, animated: false)");
        StringAssert.Contains(modalSource, "await modal.Navigation.PopModalAsync(animated: false)");
        StringAssert.Contains(modalSource, "_notificationModalCancellation?.Cancel();");
        StringAssert.Contains(modalSource, "IsNotificationModalActive(modal, cancellationToken)");
        StringAssert.Contains(modalSource, "await CloseNotificationModalAsync(modal)");
        Assert.IsFalse(modalSource.Contains("RenderContent();", StringComparison.Ordinal));
        Assert.IsFalse(modalSource.Contains("await Navigation.PopModalAsync(true)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterStoryCardsUseNativeArtworkAndFavoriteHeartOverlay()
    {
        var helper = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var favoriteHeart = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileFavoriteHeart.cs"));
        var mobileProject = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var webLuister = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor"));
        var webStyles = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylist.razor.css"));

        StringAssert.Contains(helper, "BuildFavoriteHeart(story, onFavoriteTap)");
        StringAssert.Contains(helper, "var heart = MobileFavoriteHeart.CreateButton(story.IsFavorite, 24);");
        StringAssert.Contains(favoriteHeart, "public const string Glyph = \"\\uf004\"");
        StringAssert.Contains(favoriteHeart, "RegularFontFamilyName = \"Font Awesome 6 Free Regular\"");
        StringAssert.Contains(favoriteHeart, "SolidFontFamilyName = \"Font Awesome 6 Free Solid\"");
        StringAssert.Contains(favoriteHeart, "FontFamily = ResolveFontFamily(isFavorite)");
        StringAssert.Contains(favoriteHeart, "isFavorite ? SolidFontFamilyName : RegularFontFamilyName");
        StringAssert.Contains(favoriteHeart, "FontAttributes = FontAttributes.None");
        StringAssert.Contains(favoriteHeart, "isFavorite ? Color.FromArgb(\"#FFE6EF\") : Color.FromArgb(\"#E6FFFFFF\")");
        StringAssert.Contains(favoriteHeart, "public static Button CreateButton(bool isFavorite, double fontSize)");
        StringAssert.Contains(mobileProject, "fa-regular-400.ttf");
        StringAssert.Contains(mobileProject, "fa-solid-900.ttf");
        StringAssert.Contains(helper, "HorizontalOptions = LayoutOptions.End");
        StringAssert.Contains(helper, "VerticalOptions = LayoutOptions.Start");
        StringAssert.Contains(helper, "heart.Clicked += async (_, _) => await onFavoriteTap(story);");
        StringAssert.Contains(helper, "Text = story.IsLocked ? \"Maak oop\" : \"Luister nou\"");
        Assert.IsFalse(helper.Contains("Text = story.IsFavorite ? \"Hartjie af\" : \"Hartjie\"", StringComparison.Ordinal));
        Assert.IsFalse(helper.Contains("new HorizontalStackLayout", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "WidthRequest = IsAndroid ? 148 : 168");
        StringAssert.Contains(luisterPage, "StrokeShape = BuildArtworkShape(16)");
        StringAssert.Contains(luisterPage, "var target = MobileFavoriteHeart.CreateButton(story.IsFavorite, 25);");
        StringAssert.Contains(luisterPage, "target.ZIndex = 20;");
        StringAssert.Contains(luisterPage, "target.Clicked += async (_, _) => await ToggleFavoriteAsync(story);");
        StringAssert.Contains(webLuister, "fa-@(IsStoryFavorite(story.Slug) ? \"solid\" : \"regular\") fa-heart");
        StringAssert.Contains(webStyles, "color: rgba(255, 255, 255, 0.9)");
        StringAssert.Contains(webStyles, "color: #ffe6ef");
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
        StringAssert.Contains(luisterPage, "rankedStories,\n            GetStoryCarouselHeight(isRanked: true),");
        StringAssert.Contains(luisterPage, "new RankedLuisterStory(story, index + 1)");
        StringAssert.Contains(luisterPage, "BuildLuisterStoryCarouselCard(playlist, rankedStory.Story, rankedStory.Rank)");
        StringAssert.Contains(luisterPage, "if (rank is not null)");
        StringAssert.Contains(luisterPage, "cardShell.Children.Add(BuildStoryRankBadge(rank.Value));");
        StringAssert.Contains(luisterPage, "private static View BuildStoryRankBadge(int rank)");
        StringAssert.Contains(luisterPage, "Text = rank.ToString(CultureInfo.InvariantCulture)");
        StringAssert.Contains(luisterPage, "FontFamily = \"Arial Rounded MT Bold\"");
        StringAssert.Contains(luisterPage, "FontSize = 76");
        StringAssert.Contains(luisterPage, "LineHeight = 0.82");
        StringAssert.Contains(luisterPage, "TranslationY = IsAndroid ? -13 : 0");
        StringAssert.Contains(luisterPage, "HorizontalOptions = LayoutOptions.Start");
        StringAssert.Contains(luisterPage, "VerticalOptions = LayoutOptions.Start");
        StringAssert.Contains(luisterPage, "ZIndex = 6");
        StringAssert.Contains(luisterPage, "nativeLabel.SetIncludeFontPadding(false);");
    }

    [TestMethod]
    public void MobileLuisterUsesTheWebsiteLuisterGradientBackground()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "LuisterBackgroundBrush = new LinearGradientBrush");
        StringAssert.Contains(luisterPage, "new GradientStop(Color.FromArgb(\"#408D93\"), 0)");
        StringAssert.Contains(luisterPage, "new GradientStop(Color.FromArgb(\"#4F9DB3\"), 0.22f)");
        StringAssert.Contains(luisterPage, "new GradientStop(Color.FromArgb(\"#D4CF69\"), 0.56f)");
        StringAssert.Contains(luisterPage, "new GradientStop(Color.FromArgb(\"#EFEFEF\"), 0.86f)");
        StringAssert.Contains(luisterPage, "Background = LuisterBackgroundBrush,");
        StringAssert.Contains(luisterPage, "_feedView.Scrolled += OnFeedViewScrolled;");
        StringAssert.Contains(luisterPage, "private void QueueLuisterScrollUpdate(double scrollOffset)");
        StringAssert.Contains(luisterPage, "private void ApplyLuisterGradientForScroll(double scrollOffset)");
        StringAssert.Contains(luisterPage, "LuisterBackgroundBrush.StartPoint = new Point(0, -_lastGradientScrollOffset / viewportHeight);");
        StringAssert.Contains(luisterPage, "(travelDistance - _lastGradientScrollOffset) / viewportHeight");
        Assert.IsFalse(luisterPage.Contains("BackgroundColor = LuisterBackgroundColor", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "Background = Brush.Transparent");
    }

    [TestMethod]
    public void MobileLuisterFavoriteHeartPersistsAndUpdatesVisibleStoryState()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var favoriteService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryFavoriteService.cs"));
        var favoriteMethodStart = luisterPage.IndexOf("private async Task ToggleFavoriteAsync", StringComparison.Ordinal);
        var favoriteMethodEnd = luisterPage.IndexOf("private static string BuildFavoriteRequestKey", StringComparison.Ordinal);
        var favoriteMethod = luisterPage[favoriteMethodStart..favoriteMethodEnd];

        StringAssert.Contains(client, "var resolvedIsFavorite = isFavorite\n            ? result?.IsFavorite ?? false\n            : false;");
        StringAssert.Contains(client, "_sessionState.SetFavoriteStory(slug, resolvedIsFavorite);");
        StringAssert.Contains(client, "return resolvedIsFavorite;");
        StringAssert.Contains(favoriteService, "if (deleteResponse.IsSuccessStatusCode)\n                {\n                    return false;");
        StringAssert.Contains(luisterPage, "var previousIsFavorite = story.IsFavorite;");
        StringAssert.Contains(luisterPage, "UpdateFavoriteState(story.Slug, !previousIsFavorite);");
        StringAssert.Contains(luisterPage, "var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !previousIsFavorite);");
        StringAssert.Contains(luisterPage, "_favoriteRequestsInFlight.Add(favoriteKey)");
        StringAssert.Contains(luisterPage, "_favoriteRequestsInFlight.Remove(favoriteKey)");
        StringAssert.Contains(luisterPage, "UpdateFavoriteState(story.Slug, isFavorite);");
        StringAssert.Contains(luisterPage, "UpdateFavoriteState(story.Slug, previousIsFavorite);");
        StringAssert.Contains(luisterPage, "RenderPlaylistContent();");
        StringAssert.Contains(luisterPage, "private void UpdateFavoriteState(string slug, bool isFavorite)");
        StringAssert.Contains(luisterPage, "playlist.ShowcaseStory is null ? null : UpdateStoryFavoriteState(playlist.ShowcaseStory, slug, isFavorite)");
        StringAssert.Contains(luisterPage, "story with { IsFavorite = isFavorite }");
        Assert.IsFalse(favoriteMethod.Contains("LoadAsync", StringComparison.Ordinal));
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

        StringAssert.Contains(luisterPage, "await LoadAsync();");
        StringAssert.Contains(luisterPage, "var renderedCachedData = !forceRefresh && await TryRenderCachedLuisterAsync(cancellationToken);");
        StringAssert.Contains(luisterPage, "if (!renderedCachedData)");
        StringAssert.Contains(luisterPage, "private async Task<bool> TryRenderCachedLuisterAsync(CancellationToken cancellationToken)");
        StringAssert.Contains(luisterPage, "var cachedResponse = await _apiClient.GetCachedLuisterAsync(cancellationToken);");
        StringAssert.Contains(luisterPage, "ApplyLuisterResponse(cachedResponse);");
        StringAssert.Contains(luisterPage, "private void ApplyLuisterResponse(MobileLuisterResponse response)");
        StringAssert.Contains(luisterPage, "_sections = ApplyCurrentFavoriteState(sections);");
        Assert.IsFalse(luisterPage.Contains("LoadPlayableDownloadsSafelyAsync", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "private IReadOnlyList<MobileLuisterSection> ApplyCurrentFavoriteState(IReadOnlyList<MobileLuisterSection> sections)");
        StringAssert.Contains(luisterPage, "favoriteSlugs.Contains(story.Slug)");
    }

    [TestMethod]
    public void MobileStoryDetailCachesStoryDataForFastOpen()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(client, "private static readonly TimeSpan DefaultStoryDetailCacheMaxAge = TimeSpan.FromHours(12);");
        StringAssert.Contains(client, "public Task<MobileStoryDetailResponse?> GetCachedStoryAsync(");
        StringAssert.Contains(client, "private async Task<MobileStoryDetailResponse?> GetAndCacheStoryAsync(");
        StringAssert.Contains(client, "await SaveStoryDetailCacheAsync(slug, source, response, cancellationToken);");
        StringAssert.Contains(client, "private async Task SaveStoryDetailCacheAsync(");
        StringAssert.Contains(client, "new MobileStoryDetailCacheEntry(DateTimeOffset.UtcNow, response)");
        StringAssert.Contains(client, "return System.IO.Path.Combine(cacheDirectory, $\"story-{BuildStoryDetailCacheKey(slug, source)}.json\");");
        StringAssert.Contains(client, "private sealed record MobileStoryDetailCacheEntry(DateTimeOffset CachedAtUtc, MobileStoryDetailResponse Response);");

        StringAssert.Contains(storyDetail, "var renderedCachedDetail = false;");
        StringAssert.Contains(storyDetail, "renderedCachedDetail = await TryRenderCachedStoryAsync(cancellationToken);");
        StringAssert.Contains(storyDetail, "RenderDetail(detail, trackView: !renderedCachedDetail);");
        StringAssert.Contains(storyDetail, "private async Task<bool> TryRenderCachedStoryAsync(CancellationToken cancellationToken)");
        StringAssert.Contains(storyDetail, "var cachedDetail = await _apiClient.GetCachedStoryAsync(");
        StringAssert.Contains(storyDetail, "RenderDetail(cachedDetail);");
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
        StringAssert.Contains(storyDetail, "HeightRequest = 56");
        StringAssert.Contains(storyDetail, "WidthRequest = 50");
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
        StringAssert.Contains(service, "string? OwnerKey = null");
        StringAssert.Contains(service, "LastSignedInOwnerKeyPreferenceKey");
        StringAssert.Contains(service, "OfflineDownloadAccessPolicy.IsPlayable(");
        StringAssert.Contains(service, "ClaimLegacyPaidDownloadsUnsafeAsync(");
        Assert.IsFalse(service.Contains("DeletePaidDownloadsAsync", StringComparison.Ordinal));
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
    public void MobileOfflineDownloadedListeningQueuesAndSyncsTracking()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var offlineService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "OfflineStoryDownloadService.cs"));

        StringAssert.Contains(storyDetail, "var offlinePlaybackUrl = await _offlineDownloadService.ResolvePlayableAudioAsync(detail);");
        StringAssert.Contains(storyDetail, "await PlayPreparedAudioAsync(offlinePlaybackUrl, detail, ResolveTrackingSessionId(detail), playButton);");
        StringAssert.Contains(storyDetail, "_apiClient.TrackStoryListenAsync(");
        StringAssert.Contains(offlineService, "FileSystem.AppDataDirectory");
        StringAssert.Contains(client, "private readonly SemaphoreSlim _offlineStoryListenQueueLock = new(1, 1);");
        StringAssert.Contains(client, "await EnqueueStoryListenAsync(");
        StringAssert.Contains(client, "public async Task FlushQueuedStoryListensAsync");
        StringAssert.Contains(client, "BuildOfflineStoryListenQueuePath()");
        StringAssert.Contains(client, "FileSystem.AppDataDirectory, \"offline-tracking\"");
        StringAssert.Contains(client, "TakeLast(300)");
        StringAssert.Contains(client, "private sealed record QueuedStoryListenEvent(");
        StringAssert.Contains(client, "flushQueuedListens: false");
        StringAssert.Contains(client, "_ = FlushQueuedStoryListensAsync();");
    }

    [TestMethod]
    public void MobileLuisterKeepsDownloadsOnDedicatedPageOnly()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var downloadedPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "DownloadedPage.cs"));

        Assert.IsFalse(luisterPage.Contains("IOfflineStoryDownloadService", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("_downloadedStories", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("BuildDownloadedSection", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("LuisterFeedItemKind.Downloaded", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("OpenDownloadedStoryAsync", StringComparison.Ordinal));
        StringAssert.Contains(downloadedPage, "IOfflineStoryDownloadService offlineDownloadService");
        StringAssert.Contains(downloadedPage, "GetPlayableDownloadsAsync()");
        StringAssert.Contains(downloadedPage, "OpenDownloadedStoryAsync(");
    }

    [TestMethod]
    public void MobileLuisterMenuOpensDownloadedStoriesPage()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var downloadedPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "DownloadedPage.cs"));
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(luisterPage, "MobileMenuSheet.BuildOverlay(");
        StringAssert.Contains(luisterPage, "\"Karakters\",");
        StringAssert.Contains(luisterPage, "\"Afgelaai\",");
        StringAssert.Contains(luisterPage, "await Shell.Current.GoToAsync(nameof(DownloadedPage), animate: true)");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(DownloadedPage), typeof(DownloadedPage));");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<DownloadedPage>();");
        StringAssert.Contains(downloadedPage, "public sealed class DownloadedPage : ContentPage");
        StringAssert.Contains(downloadedPage, "IOfflineStoryDownloadService offlineDownloadService");
        StringAssert.Contains(downloadedPage, "GetPlayableDownloadsAsync()");
        StringAssert.Contains(downloadedPage, "OpenDownloadedStoryAsync(");
        StringAssert.Contains(downloadedPage, "Title = \"Afgelaai\"");
        StringAssert.Contains(downloadedPage, "Text = \"Afgelaai\"");
        StringAssert.Contains(downloadedPage, "Stories gereed vir offline luister.");
    }

    [TestMethod]
    public void MobileKaraktersPageUsesNativeCharacterEndpointAndMenuRoute()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var karaktersPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(program, "app.MapGet(\"/api/mobile/karakters\"");
        StringAssert.Contains(program, "sealed record MobileCharactersResponse(");
        StringAssert.Contains(program, "sealed record MobileCharacterCardResponse(");
        StringAssert.Contains(program, "BuildMobileCharacterCard(");
        StringAssert.Contains(program, "CharacterUnlockEvaluator.EvaluateUnlockStates(");
        StringAssert.Contains(program, "ResolveMobileCharacterImageUrl(httpContext, imagePath, character.UpdatedAt)");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/karakters/{slug}/listen\"");
        StringAssert.Contains(program, "audioAccessService.CreateSignedAudioUrl(clip.StreamSlug, TimeSpan.FromHours(2))");
        StringAssert.Contains(program, "PreviewAudioClips: previewAudioClips");
        StringAssert.Contains(program, "PrimaryStory: primaryStory");
        StringAssert.Contains(program, "CallToActionLabel: callToActionLabel");

        StringAssert.Contains(models, "public sealed record MobileCharactersResponse(");
        StringAssert.Contains(models, "public sealed record MobileCharacterCard(");
        StringAssert.Contains(client, "GetCharactersAsync");
        StringAssert.Contains(client, "GetCachedCharactersAsync");
        StringAssert.Contains(client, "WarmCharactersCacheAsync");
        StringAssert.Contains(client, "SaveCharactersCacheAsync");
        StringAssert.Contains(client, "TrackCharacterProfileListenAsync");
        StringAssert.Contains(client, "\"/api/mobile/karakters\"");
        StringAssert.Contains(client, "ReadJsonResponseAsync<T>(response, path, cancellationToken)");
        StringAssert.Contains(client, "IsHtmlResponse(response)");
        StringAssert.Contains(client, "LooksLikeHtml(body)");
        StringAssert.Contains(client, "Die app se Karakters-data is nog nie op die webbediener beskikbaar nie.");
        StringAssert.Contains(karaktersPage, "public sealed class KaraktersPage : ContentPage");
        StringAssert.Contains(karaktersPage, "IQueryAttributable");
        StringAssert.Contains(karaktersPage, "IAudioPlaybackService audioPlaybackService");
        StringAssert.Contains(karaktersPage, "await _apiClient.GetCharactersAsync(cancellationToken)");
        StringAssert.Contains(karaktersPage, "await _apiClient.GetCachedCharactersAsync(cancellationToken)");
        StringAssert.Contains(karaktersPage, "private readonly CollectionView _charactersView");
        StringAssert.Contains(karaktersPage, "ItemTemplate = new DataTemplate(BuildCharacterItemView)");
        StringAssert.Contains(karaktersPage, "BuildHero(response)");
        StringAssert.Contains(karaktersPage, "new ReusableCharacterCardView(this)");
        StringAssert.Contains(karaktersPage, "_owner.BuildCharacterImageSource(character.ImageUrl)");
        StringAssert.Contains(karaktersPage, "_apiClient.BuildCachedImageSource(url, \"schink_background.jpeg\")");
        StringAssert.Contains(karaktersPage, "StartImageWarmup(response)");
        StringAssert.Contains(karaktersPage, "await _apiClient.CacheImagesAsync(");
        StringAssert.Contains(karaktersPage, "maxImages: 64");
        StringAssert.Contains(karaktersPage, "maxDegreeOfParallelism: IsAndroid || IsIOS ? 1 : 3");
        Assert.IsFalse(karaktersPage.Contains("_imageSourceCache.Clear();", StringComparison.Ordinal));
        StringAssert.Contains(karaktersPage, "RenderCharacters(response);");
        StringAssert.Contains(karaktersPage, "_owner.OpenPrimaryStoryAsync(_character)");
        StringAssert.Contains(karaktersPage, "_owner.ShowCharacterProfileAsync(_character)");
        StringAssert.Contains(karaktersPage, "_owner.PlayCharacterAudioAsync(_character)");
        StringAssert.Contains(karaktersPage, "BuildRelatedStories(character)");
        StringAssert.Contains(karaktersPage, "BuildFriends(character)");
        StringAssert.Contains(karaktersPage, "ShakeLockedCardAsync(_card)");
        StringAssert.Contains(karaktersPage, "TryOpenPendingCharacterAsync()");
        StringAssert.Contains(karaktersPage, "var parameters = new ShellNavigationQueryParameters");
        StringAssert.Contains(karaktersPage, "[\"slug\"] = story.Slug");
        StringAssert.Contains(karaktersPage, "[\"source\"] = story.Source");
        StringAssert.Contains(karaktersPage, "nameof(StoryDetailPage),\n            animate: false,\n            parameters");
        Assert.IsFalse(karaktersPage.Contains("Browser.OpenAsync", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "MobileMenuSheet.BuildOverlay(");
        StringAssert.Contains(luisterPage, "\"Karakters\",");
        StringAssert.Contains(luisterPage, "\"Bestuur rekening\"");
        StringAssert.Contains(luisterPage, "await Shell.Current.GoToAsync(\"//Karakters\", animate: false)");
        StringAssert.Contains(luisterPage, "StartKaraktersDestinationWarmup(_pageActivityCancellation.Token);");
        StringAssert.Contains(luisterPage, "await _apiClient.WarmCharactersCacheAsync(cancellationToken);");
        StringAssert.Contains(luisterPage, "await karaktersPage.PreloadCachedContentAsync(cancellationToken);");
        StringAssert.Contains(mobileTopBar, "\"Karakters\",");
        StringAssert.Contains(mobileTopBar, "\"Karakter-pare\",");
        StringAssert.Contains(mobileTopBar, "\"Instellings\",");
        StringAssert.Contains(mobileTopBar, "\"Bestuur rekening\"");
        StringAssert.Contains(mobileTopBar, "await Shell.Current.GoToAsync(\"//Karakters\", animate: false)");
        StringAssert.Contains(appShell, "Route = \"Karakters\"");
        StringAssert.Contains(appShell, "ContentTemplate = new DataTemplate(() => _services.GetRequiredService<KaraktersPage>())");
        StringAssert.Contains(appShell, "Shell.SetFlyoutItemIsVisible(karaktersContent, false);");
        Assert.IsFalse(appShell.Contains("Routing.RegisterRoute(nameof(KaraktersPage)", StringComparison.Ordinal));
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<KaraktersPage>();");
        StringAssert.Contains(karaktersPage, "internal async Task PreloadCachedContentAsync(CancellationToken cancellationToken)");
    }

    [TestMethod]
    public void MobileKaraktersPageMatchesReferenceGalleryAndProfileBehavior()
    {
        var karaktersPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));

        StringAssert.Contains(karaktersPage, "BackgroundColor = Color.FromArgb(\"#46969E\")");
        StringAssert.Contains(karaktersPage, "ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem");
        StringAssert.Contains(karaktersPage, "ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical)");
        StringAssert.Contains(karaktersPage, "HorizontalItemSpacing = 8");
        StringAssert.Contains(karaktersPage, "VerticalItemSpacing = 10");
        StringAssert.Contains(karaktersPage, "Margin = new Thickness(16, 0, 16, 0)");
        StringAssert.Contains(karaktersPage, "private readonly Grid _topBarOverlay;");
        StringAssert.Contains(karaktersPage, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(karaktersPage, "Padding = new Thickness(0, FloatingTopBarContentInset, 0, 16)");
        StringAssert.Contains(karaktersPage, "ZIndex = 100");
        StringAssert.Contains(karaktersPage, "_topBarOverlay,");
        StringAssert.Contains(karaktersPage, "Source = \"karakters_title.png\"");
        StringAssert.Contains(karaktersPage, "Aspect = Aspect.AspectFill");
        StringAssert.Contains(karaktersPage, "Source = \"schink_character_lineup.png\"");
        StringAssert.Contains(karaktersPage, "BuildUnlockProgressText(response)");
        StringAssert.Contains(karaktersPage, "Text = \" oopgesluit\"");
        StringAssert.Contains(karaktersPage, "private sealed class ReusableCharacterCardView : ContentView");
        StringAssert.Contains(karaktersPage, "_summary.Text = character.SummaryText;");
        StringAssert.Contains(karaktersPage, "_storyButton.Text = character.CallToActionLabel;");
        StringAssert.Contains(karaktersPage, "_storyButton.BackgroundColor = character.IsUnlocked");
        StringAssert.Contains(karaktersPage, "MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(karaktersPage, "notificationAction: OpenStoriesNotificationsAsync");
        StringAssert.Contains(karaktersPage, "Shell.Current.GoToAsync(nameof(SearchPage), animate: false)");
        StringAssert.Contains(karaktersPage, "Shell.Current.GoToAsync(\"//Luister?surface=notifications\", animate: false)");
        StringAssert.Contains(karaktersPage, "CharacterIconPlacement.TopRight");
        StringAssert.Contains(karaktersPage, "new SpeakerDrawable()");
        StringAssert.Contains(karaktersPage, "new LockDrawable()");
        StringAssert.Contains(karaktersPage, "HeightRequest = 28");
        StringAssert.Contains(karaktersPage, "FontFamily = PoppinsBoldFontFamily");
        StringAssert.Contains(karaktersPage, "AutomationId = \"character-profile-overlay\"");
        StringAssert.Contains(karaktersPage, "_profileOverlay.Children.Add(backdrop);");
        StringAssert.Contains(karaktersPage, "_profileOverlay.Children.Add(profileCard);");
        StringAssert.Contains(karaktersPage, "_refreshView.InputTransparent = true;");
        StringAssert.Contains(karaktersPage, "_topBarOverlay.InputTransparent = true;");
        StringAssert.Contains(karaktersPage, "_profileOverlay.InputTransparent = false;");
        StringAssert.Contains(karaktersPage, "_refreshView.InputTransparent = false;");
        StringAssert.Contains(karaktersPage, "Grid.SetRow(profileScroll, 1);");
        StringAssert.Contains(karaktersPage, "profileCard.FadeToAsync(1, 120, Easing.CubicOut)");
        Assert.IsFalse(karaktersPage.Contains("_profileOverlay.FadeToAsync", StringComparison.Ordinal));
        StringAssert.Contains(karaktersPage, "CloseCharacterProfile();");
        Assert.IsFalse(karaktersPage.Contains("ItemsLayout = new LinearItemsLayout", StringComparison.Ordinal));
        Assert.IsFalse(karaktersPage.Contains("Navigation.PushModalAsync(page", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileKaraktersNavigationPaintsImmediatelyWithoutTransitionDelay()
    {
        var karaktersPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var mobileMenuSheet = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileMenuSheet.cs"));

        StringAssert.Contains(karaktersPage, "Content = _rootLayout;\n        RenderLoadingState();");
        StringAssert.Contains(karaktersPage, "if (!renderedCachedData)\n        {\n            RenderLoadingState();");
        StringAssert.Contains(luisterPage, "GoToAsync(\"//Karakters\", animate: false)");
        StringAssert.Contains(mobileTopBar, "GoToAsync(\"//Karakters\", animate: false)");
        StringAssert.Contains(mobileTopBar, "GoToAsync(\"//Luister\", animate: false)");
        StringAssert.Contains(luisterPage, "RefreshVisibleStateAfterNavigationAsync(_pageActivityCancellation.Token)");
        StringAssert.Contains(luisterPage, "await Task.Delay(120, cancellationToken);");
        StringAssert.Contains(luisterPage, "await RefreshSessionInBackgroundAsync();");
        StringAssert.Contains(mobileMenuSheet, "PopModalAsync(animated: false)");
        StringAssert.Contains(mobileMenuSheet, "PushModalAsync(sheetPage, animated: false)");
        StringAssert.Contains(mobileMenuSheet, "public static Grid BuildOverlay(");
        StringAssert.Contains(mobileMenuSheet, "button.Opacity = 0.72;");
        Assert.IsFalse(luisterPage.Contains("MobileMenuSheet.ShowAsync(this, \"Menu\"", StringComparison.Ordinal));
        Assert.IsFalse(mobileTopBar.Contains("Navigation.PopAsync()", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("GoToAsync(nameof(KaraktersPage)", StringComparison.Ordinal));
        Assert.IsFalse(mobileTopBar.Contains("GoToAsync(nameof(KaraktersPage)", StringComparison.Ordinal));

        var onAppearingStart = luisterPage.IndexOf("protected override async void OnAppearing()", StringComparison.Ordinal);
        var onDisappearingStart = luisterPage.IndexOf("protected override void OnDisappearing()", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, onAppearingStart);
        Assert.IsGreaterThan(onAppearingStart, onDisappearingStart);
        var onAppearingSource = luisterPage[onAppearingStart..onDisappearingStart];
        Assert.IsFalse(onAppearingSource.Contains("RenderContent();", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "RenderFloatingTopBar();\n        RenderLoadingState();");
        StringAssert.Contains(luisterPage, "Loaded += (_, _) => _ = StartPageActivityAsync();");
        StringAssert.Contains(luisterPage, "private async Task StartPageActivityAsync()");
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
        StringAssert.Contains(storyDetail, "var target = MobileFavoriteHeart.CreateButton(detail.Story.IsFavorite, 25);");
        StringAssert.Contains(storyDetail, "target.ZIndex = 20;");
        StringAssert.Contains(storyDetail, "target.Clicked += async (_, _) => await ToggleFavoriteAsync(detail);");
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
        StringAssert.Contains(storyDetail, "BuildCompactTransportButton(PlaybackTransportDirection.Previous)");
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
        StringAssert.Contains(storyDetail, "FormatPlaybackSpeed(_audioPlaybackService.PlaybackSpeed)");
        StringAssert.Contains(storyDetail, "CyclePlaybackSpeed(detail)");
        StringAssert.Contains(storyDetail, "AutoplayIconGlyph");
        StringAssert.Contains(storyDetail, "FormatAutoplayLimit()");
        StringAssert.Contains(storyDetail, "CycleAutoplayLimit(detail)");
        StringAssert.Contains(storyDetail, "ShuffleIconGlyph");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.SetAutoplay(!_playlistPlaybackState.IsAutoplayEnabled);");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.SetAutoplayLimit(nextLimit, detail.Story);");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.SetShuffle(!_playlistPlaybackState.IsShuffleEnabled, detail.Story);");
        StringAssert.Contains(storyDetail, "await OpenPlaylistStoryAsync(previousStory, autoplay: ShouldAutoplaySelection());");
        StringAssert.Contains(storyDetail, "await OpenPlaylistStoryAsync(nextStory, autoplay: ShouldAutoplaySelection());");
        StringAssert.Contains(storyDetail, "await ReplaceActiveStoryAsync(nextStory, autoplay: ShouldAutoplaySelection());");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.CanAutoplayAdvance(currentDetail.Story)");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.TrackAutoplayAdvance(story);");
        StringAssert.Contains(storyDetail, "_playlistPlaybackState.TrackManualStorySelection(story);");
        StringAssert.Contains(storyDetail, "await ReplaceActiveStoryAsync(nextStory, autoplay: true);");
        StringAssert.Contains(playlistState, "public bool IsAutoplayEnabled { get; private set; }");
        StringAssert.Contains(playlistState, "public bool IsShuffleEnabled { get; private set; }");
        StringAssert.Contains(playlistState, "public int? AutoplayLimitStories { get; private set; }");
        StringAssert.Contains(playlistState, "public bool CanAutoplayAdvance(MobileStorySummary? currentStory)");
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
        StringAssert.Contains(accountPage, "var estimatedPanelHeight = _authPanelMode == AuthPanelMode.SignIn ? 470 : 620;");
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
        StringAssert.Contains(accountPage, "Text = \"\\\"Rustige, opbouende\"");
        StringAssert.Contains(accountPage, "Text = \"\\nAfrikaanse storietyd.\\\"\"");
        StringAssert.Contains(accountPage, "FontAttributes = FontAttributes.Bold | FontAttributes.Italic");
        StringAssert.Contains(accountPage, "Text = \"Rustige, opbouende \"");
        StringAssert.Contains(accountPage, "Text = \"Afrikaanse storietyd\"");
        StringAssert.Contains(accountPage, "Text = \"R 79 per maand. Kanselleer enige tyd.\"");
        StringAssert.Contains(accountPage, "Source = \"schink_login_mouse.png\"");
        StringAssert.Contains(accountPage, "LogoHeight: Math.Clamp(height * (tight ? 0.17 : 0.2), 124, 194)");
        StringAssert.Contains(accountPage, "TitleSublineFontSize: Math.Clamp(height * (tight ? 0.0231 : 0.0259), 16, 24)");
        StringAssert.Contains(accountPage, "TitleMargin: new Thickness(0, tight ? 2 : 6, 0, 0)");
        StringAssert.Contains(accountPage, "CharacterHeight: Math.Clamp(height * (tight ? 0.085 : 0.105), 64, 112)");
        StringAssert.Contains(accountPage, "ModeButtonHeight: tight ? 44 : compact ? 44 : 48");
        StringAssert.Contains(accountPage, "var landingControlWidth = Math.Clamp(fullLandingWidth * 0.7, 240, 360);");
        StringAssert.Contains(accountPage, "Spacing = metrics.PanelContentSpacing");
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
        var iOSEntitlements = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Entitlements.plist"));
        var iOSAppDelegate = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "AppDelegate.cs"));
        var androidManifest = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "AndroidManifest.xml"));
        var androidCallback = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "GoogleAuthCallbackActivity.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var assetLinks = File.ReadAllText(GetRepoPath("Shink", "wwwroot", ".well-known", "assetlinks.json"));
        var appleAssociation = File.ReadAllText(GetRepoPath("Shink", "wwwroot", ".well-known", "apple-app-site-association"));

        StringAssert.Contains(accountPage, "Teken in met Google");
        StringAssert.Contains(accountPage, "Source = ImageSource.FromFile(\"google_logo.png\")");
        Assert.IsTrue(File.Exists(GetRepoPath("Shink.Mobile", "Resources", "Images", "google_logo.png")));
        Assert.IsFalse(accountPage.Contains("GoogleLogoDrawable", StringComparison.Ordinal));
        StringAssert.Contains(accountPage, "WebAuthenticator.Default.AuthenticateAsync(");
        StringAssert.Contains(accountPage, "_apiClient.BuildGoogleSignInStartUri()");
        StringAssert.Contains(accountPage, "new Uri(MobileApiClient.GoogleCallbackUrl)");
        StringAssert.Contains(accountPage, "CompleteGoogleSignInAsync(token)");
        StringAssert.Contains(apiClient, "public const string GoogleCallbackUrl = \"schinkstories://auth/google\";");
        StringAssert.Contains(apiClient, "private const string GoogleStartPath = \"/api/mobile/auth/google/start?callback=custom-scheme\";");
        Assert.IsFalse(apiClient.Contains("#if DEBUG", StringComparison.Ordinal));
        Assert.IsFalse(apiClient.Contains("GoogleCallbackUrl = \"https://www.schink.co.za/mobile-auth/google/callback\"", StringComparison.Ordinal));
        Assert.IsFalse(apiClient.Contains("public const string GoogleCallbackUrl = \"https://www.schink.co.za/mobile-auth/google/callback\";", StringComparison.Ordinal));
        Assert.IsFalse(apiClient.Contains("private const string GoogleStartPath = \"/api/mobile/auth/google/start\";", StringComparison.Ordinal));
        StringAssert.Contains(apiClient, "BuildUri(GoogleStartPath)");
        StringAssert.Contains(apiClient, "\"/api/mobile/auth/google/complete\"");
        StringAssert.Contains(iOSEntitlements, "applinks:www.schink.co.za");
        StringAssert.Contains(iOSAppDelegate, "WebAuthenticator.Default.ContinueUserActivity(application, userActivity, completionHandler)");
        StringAssert.Contains(iOSInfo, "<string>schinkstories</string>");
        StringAssert.Contains(androidManifest, "android:allowBackup=\"false\"");
        StringAssert.Contains(androidCallback, "WebAuthenticatorCallbackActivity");
        StringAssert.Contains(androidCallback, "AutoVerify = true");
        StringAssert.Contains(androidCallback, "DataScheme = \"https\"");
        StringAssert.Contains(androidCallback, "DataHost = \"www.schink.co.za\"");
        StringAssert.Contains(androidCallback, "DataPath = \"/mobile-auth/google/callback\"");
        StringAssert.Contains(androidCallback, "DataScheme = \"schinkstories\"");
        StringAssert.Contains(androidCallback, "DataHost = \"auth\"");
        StringAssert.Contains(androidCallback, "DataPath = \"/google\"");
        StringAssert.Contains(assetLinks, "\"package_name\": \"com.schink.stories.mobile\"");
        StringAssert.Contains(assetLinks, "\"46:EB:77:79:B5:EE:8F:AF:3A:33:82:F4:EC:F9:4A:6F:50:DE:7D:DF:1C:F3:00:E7:C3:DF:32:2A:69:A6:EA:AA\"");
        StringAssert.Contains(appleAssociation, "\"7CCCKUVX8Q.com.schink.stories.mobile\"");
        StringAssert.Contains(appleAssociation, "\"/mobile-auth/google/callback\"");
        StringAssert.Contains(program, "app.MapGet(\"/api/mobile/auth/google/start\"");
        StringAssert.Contains(program, "IsMobileGoogleCustomSchemeCallback(callback)");
        StringAssert.Contains(program, "app.MapGet(\"/auth/mobile/google/callback\"");
        StringAssert.Contains(program, "app.MapGet(\"/mobile-auth/google/callback\"");
        StringAssert.Contains(program, "BuildMobileGoogleCustomSchemeCallbackUrl(httpContext.Request.QueryString)");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/auth/google/complete\"");
        StringAssert.Contains(program, "MobileGoogleAuthTokenProtectorPurpose");
        StringAssert.Contains(program, "MobileGoogleCallbackUrl = \"https://www.schink.co.za/mobile-auth/google/callback\"");
        StringAssert.Contains(program, "MobileGoogleCustomSchemeCallbackUrl = \"schinkstories://auth/google\"");
        StringAssert.Contains(program, "$\"{MobileGoogleCustomSchemeCallbackUrl}{queryString.Value}\"");
        StringAssert.Contains(program, "UseMobileCustomSchemeCallback: useCustomSchemeCallback");
        StringAssert.Contains(program, "bool UseMobileCustomSchemeCallback = false");
        StringAssert.Contains(program, "TryResolveMobileAssociationFile(httpContext.Request.Path");
    }

    [TestMethod]
    public void MobileSignInSupportsNativeAppleIdentityTokenFlowOnIos()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var appleButton = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AppleSignInButton.cs"));
        var appleService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "AppleSignInService.cs"));
        var buttonHandler = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "AppleSignInButtonHandler.cs"));
        var apiClient = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var entitlements = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Entitlements.plist"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var supabaseAuth = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAuthService.cs"));

        StringAssert.Contains(accountPage, "AppleSignInService");
        StringAssert.Contains(accountPage, "CompleteAppleSignInAsync");
        StringAssert.Contains(appleButton, "RaisePressed");
        StringAssert.Contains(buttonHandler, "ASAuthorizationAppleIdButton");
        StringAssert.Contains(appleService, "ASAuthorizationAppleIdProvider");
        StringAssert.Contains(appleService, "request.Nonce = HashNonce(_rawNonce)");
        StringAssert.Contains(appleService, "ASAuthorizationScope.FullName");
        StringAssert.Contains(appleService, "SHA256.HashData");
        StringAssert.Contains(apiClient, "\"/api/mobile/auth/apple/complete\"");
        StringAssert.Contains(entitlements, "com.apple.developer.applesignin");
        StringAssert.Contains(entitlements, "<string>Default</string>");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/auth/apple/complete\"");
        StringAssert.Contains(program, "ExchangeAppleIdentityTokenAsync");
        StringAssert.Contains(program, "GetSubscriberProfileAsync");
        StringAssert.Contains(supabaseAuth, "grant_type=id_token");
        StringAssert.Contains(supabaseAuth, "JsonPropertyName(\"id_token\")");
    }

    [TestMethod]
    public void MobileApiMutationsRequireMobileAppHeader()
    {
        var apiClient = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(apiClient, "private const string MobileAppHeaderName = \"X-Schink-Mobile-App\";");
        StringAssert.Contains(apiClient, "private const string MobileAppHeaderValue = \"1\";");
        StringAssert.Contains(apiClient, "private static void AddMobileAppHeaderIfNeeded(HttpRequestMessage request, string path)");
        StringAssert.Contains(apiClient, "requestPath.StartsWith(\"/api/mobile/\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(apiClient, "request.Headers.TryAddWithoutValidation(MobileAppHeaderName, MobileAppHeaderValue);");
        StringAssert.Contains(apiClient, "AddMobileAppHeaderIfNeeded(request, path);");

        StringAssert.Contains(program, "const string MobileAppHeaderName = \"X-Schink-Mobile-App\";");
        StringAssert.Contains(program, "static bool IsMobileAppRequest(HttpContext httpContext)");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/auth/google/complete\"");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/profile\"");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/stories/{slug}/favorite\"");
        Assert.IsTrue(CountOccurrences(program, "if (!IsMobileAppRequest(httpContext))") >= 3);
        Assert.IsTrue(CountOccurrences(program, "}).RequireRateLimiting(\"auth-submit\").DisableAntiforgery();") >= 1);
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
        StringAssert.Contains(accountPage, "_sessionState.Changed += OnSessionStateChanged;");
        StringAssert.Contains(accountPage, "_sessionState.Changed -= OnSessionStateChanged;");
        StringAssert.Contains(accountPage, "private void OnSessionStateChanged(MobileSession session)");
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
        Assert.IsFalse(androidMainActivity.Contains("Icon =", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, iconBytes.Take(4).ToArray());
        Assert.IsTrue(iconBytes.Length > 100_000);
    }

    [TestMethod]
    public void MobileSplashScreenFillsTheDisplayWithoutInsetBorders()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var app = File.ReadAllText(GetRepoPath("Shink.Mobile", "App.xaml.cs"));
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));
        var launchScreen = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "LaunchScreen.storyboard"));
        var splashPath = GetRepoPath("Shink.Mobile", "Resources", "Splash", "schink_stories_full_splash.png");
        var splashBytes = File.ReadAllBytes(splashPath);

        StringAssert.Contains(project, "<EnableBlankMauiSplashScreen>false</EnableBlankMauiSplashScreen>");
        StringAssert.Contains(project, "<MauiSplashScreen Include=\"Resources/Splash/schink_stories_logo_white.png\"");
        StringAssert.Contains(project, "Link=\"Resources/Images/schink_stories_full_splash_runtime.png\"");
        Assert.IsFalse(project.Contains("<MauiSplashScreen Include=\"Resources/Splash/schink_stories_full_splash.png\"", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("<MauiSplashScreen Include=\"Resources/Splash/splash.svg\"", StringComparison.Ordinal));
        StringAssert.Contains(infoPlist, "<string>LaunchScreen</string>");
        StringAssert.Contains(launchScreen, "contentMode=\"scaleAspectFill\"");
        StringAssert.Contains(launchScreen, "firstAttribute=\"top\" secondItem=\"launch-view\" secondAttribute=\"top\"");
        StringAssert.Contains(launchScreen, "firstAttribute=\"bottom\" secondItem=\"launch-image\" secondAttribute=\"bottom\"");
        StringAssert.Contains(app, "var window = new Window(_shell);");
        Assert.IsFalse(app.Contains("FullScreenSplashPage", StringComparison.Ordinal));
        Assert.IsFalse(app.Contains("MainThread.BeginInvokeOnMainThread", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, splashBytes.Take(4).ToArray());
        Assert.IsTrue(splashBytes.Length > 100_000);
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
        StringAssert.Contains(luisterPage, "Text = \"×\"");
        StringAssert.Contains(luisterPage, "SemanticProperties.SetDescription(clearButton, \"Maak skoon\")");
        StringAssert.Contains(luisterPage, "clearButton.Clicked += (_, _) => ClearContinueListening();");
        StringAssert.Contains(luisterPage, "private void ClearContinueListening()");
        StringAssert.Contains(luisterPage, "_continueListeningState.Clear();");
        StringAssert.Contains(luisterPage, "ResolveContinueListeningStory(item)");
        StringAssert.Contains(luisterPage, "await OpenContinueListeningAsync(item)");
        StringAssert.Contains(luisterPage, "MergeContinueListeningMetadata(resolvedStory.Value.Story, item)");
        StringAssert.Contains(luisterPage, "DurationSeconds = story.DurationSeconds is > 0 ? story.DurationSeconds : item.DurationSeconds");
        StringAssert.Contains(luisterPage, "nextItems.Add(LuisterFeedItem.ContinueListening());");
    }

    [TestMethod]
    public void MobileLuisterAndroidFeedAvoidsScrollJankWork()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(luisterPage, "ItemsSource = Array.Empty<LuisterFeedItem>()");
        StringAssert.Contains(luisterPage, "ItemTemplate = new LuisterFeedTemplateSelector(this)");
        StringAssert.Contains(luisterPage, "LuisterFeedItem { Kind: LuisterFeedItemKind.ContinueListening } => _continueListeningTemplate");
        Assert.IsFalse(luisterPage.Contains("LuisterFeedItemKind.Downloaded", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "private View BuildFeedItemView()");
        StringAssert.Contains(luisterPage, "private void ReplaceFeedItems(IReadOnlyList<LuisterFeedItem> nextItems)");
        StringAssert.Contains(luisterPage, "_feedView!.ItemsSource = nextItems.ToArray();");
        StringAssert.Contains(luisterPage, "MainThread.BeginInvokeOnMainThread(RenderFloatingTopBar);");
        StringAssert.Contains(luisterPage, "private ImageSource BuildLuisterImageSource(string? url, string? fallbackFile = null)");
        StringAssert.Contains(luisterPage, "private static Shadow BuildScrollContentShadow(Brush brush, Point offset, float radius, float opacity)");
        StringAssert.Contains(luisterPage, "private static IShape? BuildArtworkShape(double cornerRadius)");
        StringAssert.Contains(luisterPage, "new RoundRectangle { CornerRadius = cornerRadius };");
        StringAssert.Contains(luisterPage, "private MobileSession? _lastRenderedSession;");
        StringAssert.Contains(luisterPage, "IsFeedSessionEquivalent(_lastRenderedSession, session)");
        StringAssert.Contains(luisterPage, "_lastRenderedSession = _sessionState.Current;");
        Assert.IsFalse(luisterPage.Contains("IsAndroid ? null : new RoundRectangle { CornerRadius = cornerRadius };", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;");
        StringAssert.Contains(luisterPage, "IsAndroid\n            ? null!");
        StringAssert.Contains(luisterPage, "private const double StoryCarouselImageAspectRatio = 3d / 4d;");
        StringAssert.Contains(luisterPage, "return width / StoryCarouselImageAspectRatio;");
        StringAssert.Contains(luisterPage, "var artworkHeight = cardWidth / PlaylistCarouselImageAspectRatio;");
        StringAssert.Contains(luisterPage, "StrokeShape = BuildArtworkShape(16)");
        StringAssert.Contains(luisterPage, "var imageWarmupMaxImages = IsAndroid ? 56 : 80;");
        StringAssert.Contains(luisterPage, "Take(imageWarmupMaxImages)");
        StringAssert.Contains(luisterPage, "maxImages: imageWarmupMaxImages");
        StringAssert.Contains(luisterPage, "maxDegreeOfParallelism: IsAndroid || IsIOS ? 1 : 4");
        StringAssert.Contains(luisterPage, "await Task.Delay(TimeSpan.FromMilliseconds(750), token);");
        Assert.IsFalse(luisterPage.Contains("_imageSourceCache.Clear();", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "private IEnumerable<string?> EnumeratePrioritizedLuisterImageUrls()");
        Assert.IsFalse(luisterPage.Contains("ResolveVisibleDownloadedStories", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("if (IsAndroid)\n        {\n            return;\n        }", StringComparison.Ordinal));
        StringAssert.Contains(client, "NormalizeIncomingImageUrl(url)");
        StringAssert.Contains(client, ".Select(NormalizeIncomingImageUrl)");
        StringAssert.Contains(client, ".Where(url => !IsBundledImageName(url))");
        StringAssert.Contains(client, ".Select(BuildAbsoluteImageUrl)");
        StringAssert.Contains(luisterPage, "ResolvePlaylistShowcaseCoverHeight(wideLayout, pageWidth)");
        Assert.IsFalse(luisterPage.Contains("cover.SizeChanged +=", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("BuildDownloadedStoryCard", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("_playlistContent.Children.Clear();", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("_content.Children.Clear();", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("ObservableCollection<LuisterFeedItem>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileAuthCookiesPersistForApkUpdateDemoInstalls()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var demoBuildScript = File.ReadAllText(GetRepoPath("scripts", "build-mobile-demo-apk.sh"));
        var agents = File.ReadAllText(GetRepoPath("AGENTS.md"));

        StringAssert.Contains(client, "private readonly CookieContainer _cookieContainer;");
        StringAssert.Contains(client, "SecureStorage.Default.GetAsync(BuildAuthCookieStorageKey())");
        StringAssert.Contains(client, "SecureStorage.Default.SetAsync(BuildAuthCookieStorageKey(), serializedCookies)");
        StringAssert.Contains(client, "await EnsureAuthCookiesLoadedAsync(cancellationToken);");
        StringAssert.Contains(client, "await SaveAuthCookiesAsync(cancellationToken);");
        StringAssert.Contains(client, "await ClearPersistedAuthCookiesAsync();");
        StringAssert.Contains(client, "private sealed record PersistedAuthCookie(");

        StringAssert.Contains(project, "<ApplicationId>com.schink.stories.mobile</ApplicationId>");
        var applicationVersionMatch = System.Text.RegularExpressions.Regex.Match(
            project,
            @"<ApplicationVersion>(\d+)</ApplicationVersion>");
        Assert.IsTrue(applicationVersionMatch.Success);
        Assert.IsGreaterThan(0, int.Parse(applicationVersionMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        StringAssert.Contains(project, "<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>");
        StringAssert.Contains(project, "SCHINK_ANDROID_DEMO_KEYSTORE");
        StringAssert.Contains(project, "<AndroidKeyStore>true</AndroidKeyStore>");
        StringAssert.Contains(project, "<AndroidSigningKeyStore>$(SchinkAndroidDemoKeyStore)</AndroidSigningKeyStore>");
        StringAssert.Contains(project, "<AndroidSigningKeyAlias>$(SchinkAndroidDemoKeyAlias)</AndroidSigningKeyAlias>");

        StringAssert.Contains(demoBuildScript, "$HOME/.android/schink-stories-demo.keystore");
        StringAssert.Contains(demoBuildScript, "Schink Stories Android Demo Keystore");
        StringAssert.Contains(demoBuildScript, "SCHINK_ANDROID_DEMO_STORE_PASS");
        StringAssert.Contains(demoBuildScript, "-p:AndroidPackageFormat=apk");

        StringAssert.Contains(agents, "Keep the mobile package ID fixed at `com.schink.stories.mobile`.");
        StringAssert.Contains(agents, "same stable release/demo keystore");
        StringAssert.Contains(agents, "`ApplicationVersion` before producing every shareable APK");
        StringAssert.Contains(agents, "install the new APK over the old one instead of uninstalling first");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
