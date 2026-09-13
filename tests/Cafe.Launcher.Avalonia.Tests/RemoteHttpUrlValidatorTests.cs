using System.Net;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteHttpUrlValidatorTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("/relative/path")]
    public async Task ValidateAsync_WhenUrlIsNotAbsolute_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Theory]
    [InlineData("ftp://example.test/file")]
    [InlineData("file:///C:/temp/file.bin")]
    public async Task ValidateAsync_WhenSchemeIsNotHttpOrHttps_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Fact]
    public async Task ValidateAsync_WhenUrlContainsUserInfo_Throws()
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://user:pass@example.test/file"));
    }

    [Theory]
    [InlineData("http://example.test:81/file")]
    [InlineData("https://example.test:444/file")]
    public async Task ValidateAsync_WhenPortIsNotDefaultHttpOrHttps_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Fact]
    public async Task ValidateAsync_WhenHostEndsWithDotLocalhost_Throws()
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://service.localhost/file"));
    }

    [Theory]
    [InlineData("http://127.0.0.1/file")]
    [InlineData("http://10.0.0.1/file")]
    [InlineData("http://169.254.1.1/file")]
    [InlineData("http://172.16.0.1/file")]
    [InlineData("http://192.0.0.1/file")]
    [InlineData("http://192.168.1.1/file")]
    [InlineData("http://224.0.0.1/file")]
    [InlineData("http://[::1]/file")]
    [InlineData("http://[::]/file")]
    [InlineData("http://[fe80::1]/file")]
    [InlineData("http://[fc00::1]/file")]
    [InlineData("http://[2001:db8::1]/file")]
    [InlineData("http://[::ffff:127.0.0.1]/file")]
    [InlineData("http://localhost/file")]
    public async Task ValidateAsync_WhenTargetIsLocalOrPrivate_Throws(string url)
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(url));
    }

    [Fact]
    public async Task ValidateAsync_WhenDnsContainsPrivateAddress_Throws()
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => Task.FromResult<IPAddress[]>(
                [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("192.168.1.1")]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://example.test/image.png"));
    }

    [Fact]
    public async Task ValidateAsync_WhenDnsReturnsNoAddresses_Throws()
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => Task.FromResult(Array.Empty<IPAddress>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://example.test/image.png"));
    }

    [Fact]
    public async Task ValidateAsync_WhenLiteralAddressIsPublic_ReturnsUri()
    {
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        var uri = await validator.ValidateAsync("https://93.184.216.34/file");

        Assert.Equal("93.184.216.34", uri.Host);
    }

    [Fact]
    public async Task ValidateAsync_WhenLiteralAddressIsCarrierGradeNat_ReturnsUri()
    {
        // RFC 6598 100.64.0.0/10 is ISP-side Shared Address Space.
        // CDN edge nodes commonly use addresses in this range.
        var validator = RemoteHttpUrlValidator.CreateForTesting();

        var uri = await validator.ValidateAsync("https://100.64.0.1/file");

        Assert.Equal("100.64.0.1", uri.Host);
    }

    [Fact]
    public async Task ValidateAsync_WhenSameHostValidatedRepeatedlyWithinCacheLifetime_ResolvesOnce()
    {
        var resolvedHosts = new List<string>();
        var validator = new RemoteHttpUrlValidator((host, _) =>
        {
            resolvedHosts.Add(host);
            return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
        });

        for (var request = 0; request < 3; request++)
        {
            await validator.ValidateAsync("https://example.test/file");
        }

        Assert.Equal(["example.test"], resolvedHosts);
    }

    [Fact]
    public async Task ValidateAsync_WhenCacheLifetimeExpires_ResolvesHostAgain()
    {
        // 守卫（DNS 缓存）：30s 生命周期内复用解析结果；到达边界后必须重新解析，
        // 保证「DNS 记录翻转为私网地址」最迟在下一个生命周期被发现。
        var resolvedHosts = new List<string>();
        var now = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var validator = new RemoteHttpUrlValidator(
            (host, _) =>
            {
                resolvedHosts.Add(host);
                return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
            },
            cacheLifetime: RemoteHttpUrlValidator.DefaultCacheLifetime,
            utcNow: () => now);

        await validator.ValidateAsync("https://example.test/file");
        now += RemoteHttpUrlValidator.DefaultCacheLifetime - TimeSpan.FromSeconds(1);
        await validator.ValidateAsync("https://example.test/file");
        Assert.Single(resolvedHosts);

        now += TimeSpan.FromSeconds(1);
        await validator.ValidateAsync("https://example.test/file");

        Assert.Equal(2, resolvedHosts.Count);
        Assert.All(resolvedHosts, host => Assert.Equal("example.test", host));
    }

    [Fact]
    public async Task ValidateAsync_WhenHostResolvesToPrivateAddress_ResultIsNotCached()
    {
        var responses = new Queue<IPAddress[]>();
        responses.Enqueue([IPAddress.Parse("192.168.1.1")]);
        responses.Enqueue([IPAddress.Parse("93.184.216.34")]);
        var validator = new RemoteHttpUrlValidator(
            (_, _) => Task.FromResult(responses.Dequeue()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://example.test/file"));
        var uri = await validator.ValidateAsync("https://example.test/file");

        // 队列被清空 = 私网结果未入缓存，第二次校验真实地重新解析了主机。
        Assert.Empty(responses);
        Assert.Equal("example.test", uri.Host);
    }

    [Fact]
    public async Task ValidateAsync_WhenResolutionThrows_ResultIsNotCached()
    {
        var callCount = 0;
        var validator = new RemoteHttpUrlValidator((_, _) =>
        {
            callCount++;
            return callCount == 1
                ? throw new InvalidOperationException("transient resolver failure")
                : Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("https://example.test/file"));
        var uri = await validator.ValidateAsync("https://example.test/file");

        Assert.Equal(2, callCount);
        Assert.Equal("example.test", uri.Host);
    }

    [Fact]
    public async Task ValidateAsync_WhenConnectionUsesProxy_BypassesLocalDnsResolution()
    {
        // Local DNS for the target host is blocked/poisoned (would resolve to a private
        // address or fail). A proxy connection must not depend on local resolution.
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => throw new InvalidOperationException(
                "Local DNS must not be resolved when the connection egresses through a proxy."));

        var uri = await validator.ValidateAsync(
            new Uri("https://api-launcher-jp.yo-star.com/path"),
            connectionUsesProxy: true);

        Assert.Equal("api-launcher-jp.yo-star.com", uri.Host);
    }

    [Theory]
    [InlineData("http://127.0.0.1/file")]
    [InlineData("http://10.0.0.1/file")]
    [InlineData("http://192.168.1.1/file")]
    [InlineData("http://localhost/file")]
    public async Task ValidateAsync_WhenConnectionUsesProxyAndHostIsLiteralLocalOrPrivate_StillThrows(string url)
    {
        var validator = new RemoteHttpUrlValidator(
            static (_, _) => throw new InvalidOperationException("DNS must not be resolved."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(new Uri(url), connectionUsesProxy: true));
    }
}
