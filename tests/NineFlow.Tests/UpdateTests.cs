using NineFlow.Core;

namespace NineFlow.Tests;

/// <summary>更新检查的纯逻辑测试：不联网，只解析固定的 Release JSON。</summary>
internal static class UpdateTests
{
    private const string NewerReleaseJson = """
    {
      "tag_name": "v1.1.6",
      "draft": false,
      "prerelease": false,
      "html_url": "https://github.com/chaotools/NineFlow/releases/tag/v1.1.6",
      "assets": [
        {
          "name": "NineFlow-UserGuide-zh.md",
          "size": 7715,
          "browser_download_url": "https://github.com/chaotools/NineFlow/releases/download/v1.1.6/NineFlow-UserGuide-zh.md"
        },
        {
          "name": "NineFlow-v1.1.6.exe",
          "size": 66283931,
          "browser_download_url": "https://github.com/chaotools/NineFlow/releases/download/v1.1.6/NineFlow-v1.1.6.exe"
        }
      ]
    }
    """;

    private const string SameReleaseJson = """
    {
      "tag_name": "v1.1.5",
      "draft": false,
      "prerelease": false,
      "assets": [
        {
          "name": "NineFlow-v1.1.5.exe",
          "size": 66283931,
          "browser_download_url": "https://github.com/chaotools/NineFlow/releases/download/v1.1.5/NineFlow-v1.1.5.exe"
        }
      ]
    }
    """;

    public static void Run()
    {
        var current = new Version(1, 1, 5, 0);

        UpdateCheckResult newer = UpdateService.ParseReleaseJson(NewerReleaseJson, current);
        Program.Check("发现新版本", newer.Status == UpdateCheckStatus.Available, newer.Status.ToString());
        Program.Equal("新版本号", new Version(1, 1, 6), newer.Release?.Version);
        Program.Equal("挑选 exe 资源", "NineFlow-v1.1.6.exe", newer.Release?.AssetName);
        Program.Equal("资源大小", 66283931L, newer.Release?.Size);
        Program.Equal("发布页", "https://github.com/chaotools/NineFlow/releases/tag/v1.1.6", newer.Release?.ReleasePage?.ToString());

        UpdateCheckResult same = UpdateService.ParseReleaseJson(SameReleaseJson, current);
        Program.Check("同版本视为最新", same.Status == UpdateCheckStatus.UpToDate, same.Status.ToString());
        Program.Check("最新时不返回发布信息", same.Release == null);

        UpdateCheckResult older = UpdateService.ParseReleaseJson(SameReleaseJson, new Version(1, 1, 9));
        Program.Check("当前版本更高时不降级", older.Status == UpdateCheckStatus.UpToDate, older.Status.ToString());

        UpdateCheckResult noAsset = UpdateService.ParseReleaseJson(
            """
            {"tag_name":"v1.1.6","draft":false,"prerelease":false,"assets":[{"name":"NineFlow-UserGuide-zh.md","size":1,"browser_download_url":"https://github.com/chaotools/NineFlow/releases/download/v1.1.6/NineFlow-UserGuide-zh.md"}]}
            """,
            current);
        Program.Check("没有 exe 资源时失败", noAsset.Status == UpdateCheckStatus.Failed, noAsset.Status.ToString());

        UpdateCheckResult untrusted = UpdateService.ParseReleaseJson(
            """
            {"tag_name":"v1.1.6","draft":false,"prerelease":false,"assets":[{"name":"NineFlow-v1.1.6.exe","size":1,"browser_download_url":"http://example.com/NineFlow-v1.1.6.exe"}]}
            """,
            current);
        Program.Check("非 GitHub 下载地址被拒绝", untrusted.Status == UpdateCheckStatus.Failed, untrusted.Status.ToString());

        UpdateCheckResult draft = UpdateService.ParseReleaseJson(
            """{"tag_name":"v1.1.6","draft":true,"prerelease":false,"assets":[]}""",
            current);
        Program.Check("草稿发布不参与更新", draft.Status == UpdateCheckStatus.Failed, draft.Status.ToString());

        UpdateCheckResult prerelease = UpdateService.ParseReleaseJson(
            """{"tag_name":"v1.1.6","draft":false,"prerelease":true,"assets":[]}""",
            current);
        Program.Check("预发行版不参与更新", prerelease.Status == UpdateCheckStatus.Failed, prerelease.Status.ToString());

        UpdateCheckResult broken = UpdateService.ParseReleaseJson("{ not json", current);
        Program.Check("坏 JSON 报失败", broken.Status == UpdateCheckStatus.Failed, broken.Status.ToString());

        Program.Equal("标签 v1.2.3", new Version(1, 2, 3), UpdateService.ParseTagVersion("v1.2.3"));
        Program.Equal("标签带预发行后缀", new Version(1, 2, 3), UpdateService.ParseTagVersion("1.2.3-beta.1"));
        Program.Equal("标签带构建号", new Version(1, 2, 3), UpdateService.ParseTagVersion("v1.2.3+abc"));
        Program.Check("无效标签返回 null", UpdateService.ParseTagVersion("release") == null);

        Program.Check(
            "1.1.5 与 1.1.5.0 相等",
            UpdateService.Normalize(new Version(1, 1, 5)) == UpdateService.Normalize(new Version(1, 1, 5, 0)));

        Program.Check(
            "GitHub 下载地址受信任",
            UpdateService.IsTrustedDownloadUrl(new Uri("https://github.com/chaotools/NineFlow/releases/download/v1.1.6/NineFlow-v1.1.6.exe")));
        Program.Check(
            "GitHub 资源域名受信任",
            UpdateService.IsTrustedDownloadUrl(new Uri("https://objects.githubusercontent.com/github-production-release-asset/x")));
        Program.Check(
            "HTTP 地址不受信任",
            !UpdateService.IsTrustedDownloadUrl(new Uri("http://github.com/x.exe")));
        Program.Check(
            "第三方域名不受信任",
            !UpdateService.IsTrustedDownloadUrl(new Uri("https://evil.example.com/x.exe")));

        string script = UpdateInstaller.BuildCommandScript();
        Program.Check("替换脚本等待旧进程", script.Contains("tasklist", StringComparison.Ordinal));
        Program.Check("替换脚本覆盖 exe", script.Contains("copy /Y", StringComparison.Ordinal));
        Program.Check("替换脚本重新启动", script.Contains("start \"\" \"%TARGET%\"", StringComparison.Ordinal));
        Program.Check("替换脚本会清理自身", script.Contains("del \"%~f0\"", StringComparison.Ordinal));
    }
}
