using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class ErrorLoggingOriginalPathTests
{
    [TestMethod]
    public void KeepsOriginalAssetPathAfterExceptionHandlerReExecution()
    {
        var context = CreateContext();
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("asset mismatch"),
            Path = "/css/app-shell.css"
        });
        context.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature { OriginalPath = "/error/404" });

        Assert.AreEqual("/css/app-shell.css", Capture(context).RequestPath);
    }

    [TestMethod]
    public void KeepsOriginalStatusPagePathWithoutQueryTokens()
    {
        var context = CreateContext();
        context.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
        {
            OriginalPath = "/media/audio/story",
            OriginalQueryString = "?token=secret"
        });

        Assert.AreEqual("/media/audio/story", Capture(context).RequestPath);
    }

    [TestMethod]
    public void KeepsNormalRequestPathWithoutQueryTokens()
    {
        var context = CreateContext();
        context.Request.Path = "/admin";

        Assert.AreEqual("/admin", Capture(context).RequestPath);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/error/500";
        context.Request.QueryString = new QueryString("?token=secret");
        return context;
    }

    private static AppErrorLogEntry Capture(HttpContext context)
    {
        var queue = new AppErrorLogQueue();
        using var provider = new SupabaseErrorLoggingProvider(queue, new HttpContextAccessor { HttpContext = context }, new TestEnvironment());
        provider.CreateLogger("StaticAssets").LogError(new InvalidOperationException("asset mismatch"), "Failed to send file");
        Assert.IsTrue(queue.Reader.TryRead(out var entry));
        return entry!;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Shink";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
