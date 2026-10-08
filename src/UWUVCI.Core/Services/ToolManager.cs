using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Services
{
    /// <summary>
    /// Downloads the data files the injector needs from the UWUVCI tools repository, like the Windows app does on first
    /// start. Only data and managed libraries are fetched; none of the repository's .exe tools are used on macOS.
    /// </summary>
    public static class ToolManager
    {
        public const string ToolsUrl = "https://github.com/Hotbrawl20/UWUVCI-Tools/raw/master/";

        public static readonly string[] ToolNames =
        {
            "BASE.zip",
            "nintendont.dol",
            "nintendont_force.dol",
            "forwarder.dol",
            "goomba.gba",
            "blank.ini",
            "iconTex.tga",
            "bootTvTex.png",
            "font.otf",
            "font2.ttf",
            "DSLayoutScreens.zip",
            "NKit.dll",
            "SharpCompress.dll",
            "NKit.dll.config",
        };

        public static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UWUVCI-Mac/1.0");
            return client;
        }

        public static List<string> MissingTools() =>
            ToolNames.Where(name => !File.Exists(Path.Combine(AppPaths.Tools, name))).ToList();

        /// <summary>Downloads every missing file. <paramref name="progress"/> receives (percent, file name).</summary>
        public static async Task DownloadMissingAsync(IProgress<(int, string)> progress = null, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(AppPaths.Tools);
            List<string> missing = MissingTools();
            for (int i = 0; i < missing.Count; i++)
            {
                progress?.Report((i * 100 / missing.Count, missing[i]));
                await DownloadToolAsync(missing[i], cancel).ConfigureAwait(false);
            }
            progress?.Report((100, ""));
        }

        public static async Task UpdateAllAsync(IProgress<(int, string)> progress = null, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(AppPaths.Tools);
            for (int i = 0; i < ToolNames.Length; i++)
            {
                progress?.Report((i * 100 / ToolNames.Length, ToolNames[i]));
                await DownloadToolAsync(ToolNames[i], cancel).ConfigureAwait(false);
            }
            progress?.Report((100, ""));
        }

        private static async Task DownloadToolAsync(string name, CancellationToken cancel)
        {
            string target = Path.Combine(AppPaths.Tools, name);
            string partial = target + ".part";
            Exception last = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await DownloadFileAsync(ToolsUrl + name, partial, cancel).ConfigureAwait(false);

                    string expected = await TryGetStringAsync(ToolsUrl + name + ".md5", cancel).ConfigureAwait(false);
                    expected = expected?.Split('\n')[0].Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(expected) && expected.Length == 32 && Md5(partial) != expected)
                        throw new IOException($"{name} did not download correctly (checksum mismatch).");

                    File.Move(partial, target, true);
                    Logger.Log($"Downloaded {name}");
                    return;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Logger.Log($"Download of {name} failed (attempt {attempt + 1}): {ex.Message}");
                }
            }

            if (File.Exists(partial))
                File.Delete(partial);
            throw new IOException($"Unable to download {name}: {last?.Message}", last);
        }

        public static async Task DownloadFileAsync(string url, string path, CancellationToken cancel = default)
        {
            using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
            await response.Content.CopyToAsync(file, cancel).ConfigureAwait(false);
        }

        public static async Task<string> TryGetStringAsync(string url, CancellationToken cancel = default)
        {
            try
            {
                using HttpResponseMessage response = await Http.GetAsync(url, cancel).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false) : null;
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                return null;
            }
        }

        public static async Task<bool> RemoteFileExistsAsync(string url, CancellationToken cancel = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using HttpResponseMessage response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                return false;
            }
        }

        public static string Md5(string path)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
