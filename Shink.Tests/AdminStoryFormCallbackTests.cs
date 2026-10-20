using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MudBlazor;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

// Inspect the compiled Razor callbacks without starting authentication, JS, or network services.
#pragma warning disable BL0006
[TestClass]
public sealed class AdminStoryFormCallbackTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    [DataRow("NewStoryEditor", "admin-solid-dialog", 9, "af")]
    [DataRow("NewStoryEditor", "admin-solid-dialog", 9, "en")]
    [DataRow("StoryEditor", "admin-solid-dialog admin-story-dialog", 10, "af")]
    [DataRow("StoryEditor", "admin-solid-dialog admin-story-dialog", 10, "en")]
    public void StoryFields_DelayedChangesAfterEditorClears_DoNotThrow(
        string editorProperty, string dialogClass, int fieldCount, string language)
    {
        var page = CreatePage();
        Set(page, "CurrentLanguage", typeof(Admin).GetMethod("ParseLanguage", PrivateInstance)!.Invoke(page, [language]));
        OpenEditor(page, editorProperty);
        var renderedEditor = Get(page, editorProperty)!;
        var callbacks = GetTextCallbacks(page, dialogClass);
        Assert.HasCount(fieldCount, callbacks);
        ClearEditor(page, editorProperty);

        foreach (var (_, callback) in callbacks)
            callback("delayed change");

        Assert.IsNull(Get(page, editorProperty));
        foreach (var property in new[] { "Title", "Summary", "Description", "YouTubeUrl", "Synopsis",
                     "ValuesText", "LessonsText", "ConversationQuestionsText", "CharactersText" })
            Assert.AreEqual("delayed change", renderedEditor.GetType().GetProperty(property)!.GetValue(renderedEditor), property);
    }

    [TestMethod]
    [DataRow("NewStoryEditor", "admin-solid-dialog")]
    [DataRow("StoryEditor", "admin-solid-dialog admin-story-dialog")]
    public void StoryFields_DelayedChangesFromPreviousEditor_DoNotOverwriteReplacement(
        string editorProperty, string dialogClass)
    {
        var page = CreatePage();
        OpenEditor(page, editorProperty);
        var previousDraft = Get(page, editorProperty)!;
        var callbacks = GetTextCallbacks(page, dialogClass);
        ClearEditor(page, editorProperty);
        OpenEditor(page, editorProperty);
        var nextDraft = Get(page, editorProperty)!;
        GetTextCallbacks(page, dialogClass).Single(field => field.Name == "Titel").Callback("Next story");

        callbacks.Single(field => field.Name == "Titel").Callback("Previous story");

        Assert.AreEqual("Next story", nextDraft.GetType().GetProperty("Title")!.GetValue(nextDraft));
        Assert.AreEqual("Previous story", previousDraft.GetType().GetProperty("Title")!.GetValue(previousDraft));
    }

    private static void OpenEditor(Admin page, string editorProperty)
    {
        if (editorProperty == "NewStoryEditor") Invoke(page, "StartNewStory");
        else Set(page, editorProperty, Activator.CreateInstance(typeof(Admin).GetNestedType("EditableStory", BindingFlags.NonPublic)!)!);
    }

    private static void ClearEditor(Admin page, string editorProperty)
    {
        if (editorProperty == "NewStoryEditor") Invoke(page, "CloseNewStoryDialogCore");
        else Set(page, editorProperty, null);
    }

    private static Admin CreatePage()
    {
        var page = new Admin();
        Set(page, "NavigationManager", new TestNavigationManager());
        Set(page, "CloudflareR2OptionsAccessor", Options.Create(new CloudflareR2Options()));
        Set(page, "IsLoading", false);
        return page;
    }

    private static List<(string Name, Action<string> Callback)> GetTextCallbacks(Admin page, string dialogClass)
    {
        using var root = new RenderTreeBuilder();
        typeof(Admin).GetMethod("BuildRenderTree", PrivateInstance)!.Invoke(page, [root]);
        var callbacks = new List<(string, Action<string>)>();
        var frames = root.GetFrames();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType != RenderTreeFrameType.Component || frame.ComponentType != typeof(MudDialog)) continue;
            var attributes = ReadAttributes(frames, i);
            if (!Equals(attributes.GetValueOrDefault("Class"), dialogClass)) continue;
            CollectTextCallbacks((RenderFragment)attributes["DialogContent"]!, callbacks);
        }
        return callbacks;
    }

    private static void CollectTextCallbacks(RenderFragment fragment, List<(string, Action<string>)> callbacks)
    {
        using var builder = new RenderTreeBuilder();
        fragment(builder);
        var frames = builder.GetFrames();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType != RenderTreeFrameType.Component) continue;
            var attributes = ReadAttributes(frames, i);
            if (frame.ComponentType == typeof(MudTextField<string>))
            {
                var callback = (EventCallback<string>)attributes["ValueChanged"]!;
                var action = (Action<string>)typeof(EventCallback<string>).GetField("Delegate", PrivateInstance)!.GetValue(callback)!;
                callbacks.Add(((string)(attributes.GetValueOrDefault("Label") ?? attributes["Placeholder"])!, action));
            }
            foreach (var child in attributes.Values.OfType<RenderFragment>()) CollectTextCallbacks(child, callbacks);
        }
    }

    private static Dictionary<string, object?> ReadAttributes(ArrayRange<RenderTreeFrame> frames, int index)
    {
        var attributes = new Dictionary<string, object?>();
        for (var i = index + 1; i < frames.Count && frames.Array[i].FrameType == RenderTreeFrameType.Attribute; i++)
            attributes[frames.Array[i].AttributeName] = frames.Array[i].AttributeValue;
        return attributes;
    }

    private static object? Get(Admin page, string name) => typeof(Admin).GetProperty(name, PrivateInstance)!.GetValue(page);
    private static void Set(Admin page, string name, object? value) => typeof(Admin).GetProperty(name, PrivateInstance)!.SetValue(page, value);
    private static void Invoke(Admin page, string name) => typeof(Admin).GetMethod(name, PrivateInstance)!.Invoke(page, null);

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://schink.example/", "https://schink.example/admin");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Rendering must not navigate.");
    }
}
#pragma warning restore BL0006
