using System.Globalization;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SystemCultureSnapshotTests
{
    [Fact]
    public void Capture_ThenRestore_RestoresBothCultures()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var snapshot = new SystemCultureSnapshot();
            snapshot.Capture();

            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

            Assert.NotEqual(snapshot.Culture.Name, CultureInfo.CurrentCulture.Name);

            snapshot.Restore();

            Assert.Equal(snapshot.Culture.Name, CultureInfo.CurrentCulture.Name);
            Assert.Equal(snapshot.UiCulture.Name, CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }

    [Fact]
    public void Capture_IsIdempotent_KeepsFirstSnapshot()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var snapshot = new SystemCultureSnapshot();
            snapshot.Capture();

            var firstCulture = snapshot.Culture;
            var firstUiCulture = snapshot.UiCulture;

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");

            snapshot.Capture();

            Assert.Equal(firstCulture.Name, snapshot.Culture.Name);
            Assert.Equal(firstUiCulture.Name, snapshot.UiCulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }

    [Fact]
    public void Properties_ReturnReadableCultureObjects()
    {
        var snapshot = new SystemCultureSnapshot();

        Assert.NotNull(snapshot.Culture);
        Assert.NotNull(snapshot.UiCulture);
        Assert.NotNull(snapshot.Culture.Name);
        Assert.NotNull(snapshot.UiCulture.Name);
    }
}
