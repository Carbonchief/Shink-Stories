using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components;

namespace Shink.Tests;

[TestClass]
public sealed class PrerenderedPageTests
{
    [TestMethod]
    public async Task InteractivePageConsumesOnlyItsOwnAccountAndLookupOnce()
    {
        using var services = new ServiceCollection().AddLogging().AddMemoryCache().BuildServiceProvider();
        await using var initial = new ProbeRenderer(services, false);
        var page = await initial.CreatePageAsync();
        var first = await page.Read("catalogue", "listener");
        var initialId = page.PageLoadId;
        Assert.AreNotEqual(Guid.Empty, initialId);

        await using var connected = new ProbeRenderer(services, true);
        var next = await connected.CreatePageAsync();
        next.PageLoadId = initialId;
        Assert.AreNotSame(first, await next.Read("catalogue", "other"));
        Assert.AreNotSame(first, await next.Read("different-slug", "listener"));
        Assert.AreSame(first, await next.Read("catalogue", "listener"));
        Assert.AreNotSame(first, await next.Read("catalogue", "listener"));
    }

    private sealed class Probe : PrerenderedPage
    {
        public Task<object> Read(string name, string account) =>
            ReadPageDataAsync(name, account, () => Task.FromResult(new object()));
    }

    private sealed class ProbeRenderer(IServiceProvider services, bool interactive)
        : StaticHtmlRenderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        protected override RendererInfo RendererInfo { get; } = new(interactive ? "Server" : "Static", interactive);

        public Task<Probe> CreatePageAsync() => Dispatcher.InvokeAsync(() =>
        {
            // Instantiate through the renderer so component injection follows the normal path.
            var attached = (Probe)InstantiateComponent(typeof(Probe));
            AssignRootComponentId(attached);
            return attached;
        });
    }
}
