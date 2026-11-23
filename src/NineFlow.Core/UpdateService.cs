using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace NineFlow.Core;

/// <summary>检查结果：已是最新 / 有新版 / 检查失败。</summary>
public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
}

/// <summary>一条可用的发布信息。</summary>
public sealed record UpdateRelease(
    Version Version,
    string Tag,
    string AssetName,
    Uri DownloadUrl,
    long Size,
    Uri? ReleasePage,
    string? Sha256 = null);

/// <summary>一次检查的结果。</summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version CurrentVersion,
    UpdateRelease? Release,
    string? Error);

/// <summary>
/// 更新检查与下载。数据源是 GitHub Releases（公开仓库，无需令牌）。
/// 只负责「查 + 下」，替换 exe 与重启交给 <see cref="UpdateInstaller"/>。
/// </summary>
public static class UpdateService
{
    /// <summary>GitHub 最新的正式 Release。</summary>
    public const string DefaultFeedUrl = "https://api.github.com/repos/chaotools/NineFlow/releases/latest";

    /// <summary>单文件 exe 约 63MB，留足余量后拒绝异常的大文件。</summary>
    public const long MaxDownloadBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NineFlow-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// 只比较主/次/修订三段，避免 1.1.5 与 1.1.5.0 被当成不同版本。
    /// </summary>
    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    /// <summary>把标签（v1.1.6 / 1.1.6-beta）解析成可比较的版本。</summary>
    public static Version? ParseTagVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string text = tag.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) text = text[..cut];

        return Version.TryParse(text, out Version? parsed) ? Normalize(parsed) : null;
    }

    /// <summary>把 "sha256:abcdef…" 归一成小写十六进制；格式不认识时返回 null。</summary>
    public static string? NormalizeSha256(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;

        const string prefix = "sha256:";
        string text = digest.Trim();
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        string hex = text[prefix.Length..].Trim();
        if (hex.Length != 64) return null;

        foreach (char c in hex)
        {
            if (!Uri.IsHexDigit(c)) return null;
        }

        return hex.ToLowerInvariant();
    }

    /// <summary>两个 SHA-256 十六进制串是否一致（忽略大小写）。</summary>
    public static bool DigestMatches(string expectedSha256, string actualSha256) =>
        string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 校验下载完成的文件：SHA-256、大小、内部版本号。独立成纯函数便于单元测试。
    /// 全部通过返回 true；否则返回 false 并在 error 中给出原因。
    /// </summary>
    public static bool TryValidateDownload(
        UpdateRelease release,
        long downloadedSize,
        string actualSha256,
        string? fileVersion,
        out string error)
    {
        error = string.Empty;

        // 发布信息里带摘要时，哈希必须一致；没带（旧发布）则退回大小 + 版本号校验
        if (release.Sha256 is { Length: > 0 } expectedSha256 && !DigestMatches(expectedSha256, actualSha256))
        {
            error = "更新包 SHA-256 与发布信息不一致，已拒绝安装（文件可能被篡改或下载不完整）。";
            return false;
        }

        if (release.Size > 0 && downloadedSize != release.Size)
        {
            error = $"下载大小与发布信息不一致（{downloadedSize} / {release.Size} 字节）。";
            return false;
        }

        if (!Version.TryParse(fileVersion, out Version? parsed) ||
            Normalize(parsed) < Normalize(release.Version))
        {
            error = "下载的文件不是预期版本，可能被替换或下载不完整。";
            return false;
        }

        return true;
    }

    /// <summary>只有 GitHub 的 HTTPS 地址才允许下载，避免被改成任意来源。</summary>
    public static bool IsTrustedDownloadUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        string host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>联网检查；网络类错误统一折算成 Failed，不抛异常。</summary>
    public static async Task<UpdateCheckResult> CheckAsync(
        Version current,
        string? feedUrl = null,
        CancellationToken cancellationToken = default)
    {
        current = Normalize(current);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            string json = await Http
                .GetStringAsync(feedUrl ?? DefaultFeedUrl, timeout.Token)
                .ConfigureAwait(false);

            return ParseReleaseJson(json, current);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, "连接 GitHub 超时，请稍后再试。");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, Describe(ex));
        }
    }

    /// <summary>解析 GitHub Release JSON；独立出来便于单元测试。</summary>
    public static UpdateCheckResult ParseReleaseJson(string json, Version current)
    {
        current = Normalize(current);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, "最新发布还是草稿，暂不更新。");
            }

            if (root.TryGetProperty("prerelease", out JsonElement prerelease) && prerelease.ValueKind == JsonValueKind.True)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, "最新发布是预发行版，暂不更新。");
            }

            string tag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;

            Version? releaseVersion = ParseTagVersion(tag);
            if (releaseVersion == null)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, $"无法识别发布版本号“{tag}”。");
            }

            Uri? releasePage = null;
            if (root.TryGetProperty("html_url", out JsonElement pageElement) &&
                Uri.TryCreate(pageElement.GetString(), UriKind.Absolute, out Uri? page))
            {
                releasePage = page;
            }

            string assetName = string.Empty;
            Uri? downloadUrl = null;
            long size = 0;
            string? sha256 = null;
            bool hasFallback = false;

            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out JsonElement nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                    string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                        ? urlElement.GetString()
                        : null;

                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsTrustedDownloadUrl(uri)) continue;

                    // GitHub 会为发布资源提供 digest（sha256:...）：有就必须校验，
                    // 声明了但格式不认识说明发布信息异常，直接跳过这个资源
                    string? assetSha256 = null;
                    if (asset.TryGetProperty("digest", out JsonElement digestElement))
                    {
                        if (digestElement.ValueKind == JsonValueKind.String)
                        {
                            assetSha256 = NormalizeSha256(digestElement.GetString());
                            if (assetSha256 == null) continue;
                        }
                        else if (digestElement.ValueKind != JsonValueKind.Null)
                        {
                            continue;
                        }
                    }

                    long assetSize = asset.TryGetProperty("size", out JsonElement sizeElement) &&
                                     sizeElement.TryGetInt64(out long parsedSize)
                        ? parsedSize
                        : 0;

                    // 优先采用 NineFlow 命名的 exe（自动发布的约定），没有就退回任意 exe：
                    // 手动发版把文件命名成「九序-1.2.0.exe」也不会让自动更新静默失效
                    bool preferred = name.StartsWith("NineFlow", StringComparison.OrdinalIgnoreCase);
                    if (preferred)
                    {
                        assetName = name;
                        downloadUrl = uri;
                        sha256 = assetSha256;
                        size = assetSize;
                        break;
                    }

                    if (!hasFallback)
                    {
                        hasFallback = true;
                        assetName = name;
                        downloadUrl = uri;
                        sha256 = assetSha256;
                        size = assetSize;
                    }
                }
            }

            if (downloadUrl == null)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, "这次发布里没有找到可下载的 exe。");
            }

            var release = new UpdateRelease(releaseVersion, tag, assetName, downloadUrl, size, releasePage, sha256);
            return releaseVersion > current
                ? new UpdateCheckResult(UpdateCheckStatus.Available, current, release, null)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, current, null, null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, current, null, "解析更新信息失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 下载更新包并校验：SHA-256 与发布信息一致（发布方提供时）、大小一致、
    /// 确实是程序本体、内部版本不低于发布版本。
    /// 校验不过就删除临时文件并抛出，绝不把可疑文件交给替换步骤。
    /// </summary>
    public static async Task DownloadAsync(
        UpdateRelease release,
        string destinationPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsTrustedDownloadUrl(release.DownloadUrl))
        {
            throw new InvalidOperationException("下载地址不是受信任的 GitHub 地址，已拒绝。");
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string tempPath = destinationPath + ".part";

        try
        {
            using HttpResponseMessage response = await Http
                .GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            if (total is > MaxDownloadBytes)
            {
                throw new InvalidOperationException($"更新包过大（{total.Value / 1024 / 1024} MB），已拒绝。");
            }

            await using Stream input = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            string actualSha256;
            await using (var output = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int lastPercent = -1;

                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0) break;

                    received += read;
                    if (received > MaxDownloadBytes)
                    {
                        throw new InvalidOperationException("更新包超过允许的大小，已中止。");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);

                    if (progress != null && total is > 0)
                    {
                        int percent = (int)Math.Min(100, received * 100 / total.Value);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress.Report(percent);
                        }
                    }
                }

                actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            long downloaded = new FileInfo(tempPath).Length;
            string? fileVersion = FileVersionInfo.GetVersionInfo(tempPath).FileVersion;

            if (!TryValidateDownload(release, downloaded, actualSha256, fileVersion, out string validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // 清理失败不影响主流程
            }

            throw;
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: not null } http =>
            $"GitHub 返回 {(int)http.StatusCode.Value}，可能触发了访问频率限制。",
        HttpRequestException =>
            "无法连接 GitHub：" + ex.Message,
        _ => ex.Message,
    };
}
