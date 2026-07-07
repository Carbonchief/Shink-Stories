using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Shink.Tests;

[TestClass]
public sealed class LuisterPeekMascotSourceTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string LuisterRazorPath = Path.Combine(RepositoryRoot, "Shink", "Components", "Pages", "Luister.razor");
    private static readonly string LuisterCssPath = Path.Combine(RepositoryRoot, "Shink", "Components", "Pages", "Luister.razor.css");
    private static readonly string LuisterJsPath = Path.Combine(RepositoryRoot, "Shink", "Components", "Pages", "Luister.razor.js");
    private static readonly string MascotAssetPath = Path.Combine(RepositoryRoot, "Shink", "wwwroot", "branding", "Oortjies_Website.png");

    [TestMethod]
    public void LuisterPageUsesPageScopedPeekMascotModuleAndAsset()
    {
        var razor = File.ReadAllText(LuisterRazorPath);

        StringAssert.Contains(razor, "data-luister-peek-mascot");
        StringAssert.Contains(razor, "/branding/Oortjies_Website.png");
        StringAssert.Contains(razor, "/Components/Pages/Luister.razor.js");
        StringAssert.Contains(razor, "initializeLuisterPage");
        StringAssert.Contains(razor, "disposeLuisterPage");
        Assert.IsTrue(File.Exists(MascotAssetPath));
    }

    [TestMethod]
    public void LuisterPeekMascotIsBoundedAndAnimatedFromScreenEdges()
    {
        var css = File.ReadAllText(LuisterCssPath);
        var js = File.ReadAllText(LuisterJsPath);

        StringAssert.Contains(css, ".luister-peek-mascot");
        StringAssert.Contains(css, "--peek-top-clearance");
        StringAssert.Contains(css, "--peek-size: clamp(2.7rem, 6.5vw, 4.45rem);");
        StringAssert.Contains(css, "--peek-size: clamp(2.35rem, 12vw, 3.3rem);");
        StringAssert.Contains(css, "--peek-x");
        StringAssert.Contains(css, "--peek-y");
        StringAssert.Contains(css, "z-index: 230;");
        StringAssert.Contains(css, ".luister-peek-mascot.is-positioning");
        StringAssert.Contains(css, "pointer-events: auto;");
        StringAssert.Contains(css, "--peek-wiggle-transform");
        StringAssert.Contains(css, "--peek-jump-transform");
        StringAssert.Contains(css, "translate(-42%, -50%) rotate(90deg)");
        StringAssert.Contains(css, "translate(42%, -50%) rotate(-90deg)");
        StringAssert.Contains(css, "translate(-50%, -42%) rotate(180deg)");
        StringAssert.Contains(css, "translate(-50%, 42%) rotate(0deg)");
        StringAssert.Contains(css, "rotate(90deg)");
        StringAssert.Contains(css, "rotate(-90deg)");
        StringAssert.Contains(css, "rotate(180deg)");
        StringAssert.Contains(css, "prefers-reduced-motion: reduce");

        foreach (var positionClass in new[] { "peek-side-left", "peek-side-right", "peek-side-top", "peek-side-bottom" })
        {
            StringAssert.Contains(css, positionClass);
            StringAssert.Contains(js, positionClass);
        }

        StringAssert.Contains(js, "const FIVE_MINUTES_MS = 5 * 60 * 1000;");
        StringAssert.Contains(js, "const MAX_PEEKS_PER_WINDOW = 2;");
        StringAssert.Contains(js, "recentPeekTimes.length >= MAX_PEEKS_PER_WINDOW");
        StringAssert.Contains(js, "window.schinkForceLuisterPeek = forceLuisterPeekMascot;");
        StringAssert.Contains(js, "export function forceLuisterPeekMascot");
        StringAssert.Contains(js, "PEEK_HIDE_WIGGLE_MS");
        StringAssert.Contains(js, "PEEK_CLICK_JUMP_MS");
        StringAssert.Contains(js, "handlePeekMascotClick");
        StringAssert.Contains(js, "peekMascotElement.addEventListener(\"click\", handlePeekMascotClick);");
        StringAssert.Contains(js, "animatePeekAway({ jump: false });");
        StringAssert.Contains(js, "animatePeekAway({ jump: true });");
        StringAssert.Contains(js, "readPeekTransform(computedStyle, \"--peek-wiggle-transform\"");
        StringAssert.Contains(js, "readPeekTransform(computedStyle, \"--peek-jump-transform\"");
        StringAssert.Contains(js, "completedAnimation?.cancel();");
        StringAssert.Contains(js, "element.classList.add(PEEK_POSITIONING_CLASS);");
        StringAssert.Contains(js, "void element.offsetWidth;");
        StringAssert.Contains(js, "PEEK_POSITION_ALIASES");
        StringAssert.Contains(js, "buildEdgePosition");
        StringAssert.Contains(js, "peekMascotElement.classList.add(PEEK_POSITIONING_CLASS);");
        StringAssert.Contains(js, "peekMascotElement.style.setProperty(\"--peek-x\", nextEdgePosition.x);");
        StringAssert.Contains(js, "peekMascotElement.style.setProperty(\"--peek-y\", nextEdgePosition.y);");
        StringAssert.Contains(js, "if (!isForced)");
        Assert.IsTrue(Regex.IsMatch(js, @"randomBetween\(NEXT_DELAY_MIN_MS,\s*NEXT_DELAY_MAX_MS\)", RegexOptions.Multiline));
    }
}
