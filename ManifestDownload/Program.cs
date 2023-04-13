using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Reflection.Metadata;
using System.Text;
using CommandDotNet;
using CommandDotNet.NameCasing;
using ManifestDownload;
using Microsoft.Extensions.Configuration;
using Octokit;
using Octokit.Caching;
using Octokit.Internal;
using PPlus;
using PPlus.CommandDotNet;
using PPlus.Objects;
using ValveKeyValue;
using Blob = Octokit.Blob;

namespace GitDownload
{
    internal class Program
    {
        const string repoOwner = "wxy1343";
        const string repoName = "ManifestAutoUpdate";
        const string tokenKey = "GitHubToken";
        private static Repository repo;
        private static GitHubClient github;
        private static RateLimit limit;

        static async Task<bool> Init()
        {
            try
            {
                PromptPlus.WriteLine($"获取请求阈值...");
                github = new GitHubClient(new ProductHeaderValue("SteamDepotDownload"));
                string token = Config[tokenKey];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    github.Credentials = new Credentials(token);
                }
                MiscellaneousRateLimit rateLimit = await github.RateLimit.GetRateLimits();
                limit = rateLimit.Rate;
                PromptPlus.WriteLine($"当前可用请求数：{limit.Remaining}");
                PromptPlus.WriteLine($"重置时间:[cyan]{limit.Reset.LocalDateTime}");
                if (limit.Remaining == 0)
                {
                    PromptPlus.WriteLine($"当前请求已达到上限，请等待重置后再试。重置时间:[cyan]{limit.Reset.LocalDateTime}");
                    if (limit.Limit <= 60)
                    {
                        PromptPlus.WriteLine($"可以在GitHub生成空权限的Person Token，配置到 appsettings.json 中 提高可请求上限。");
                    }
                    return false;
                }

                ResultPromptPlus<IEnumerable<ResultProcess>> result = PromptPlus.WaitProcess("获取远程清单仓库")
                    .AddProcess(new SingleProcess(async t =>
                    {
                        try
                        {
                            Repository repository = await github.Repository.Get(repoOwner, repoName);
                            return repository;
                        }
                        catch (Exception e)
                        {
                            return e;
                        }
                    }, processTextResult: (o) => o is Repository ? "Done" : "Error"))
                    .Run();
                ResultProcess process = result.Value.First();
                if (process.ValueProcess is Repository r)
                {
                    repo = r;
                }
                else if (process.ValueProcess is RateLimitExceededException re)
                {
                    PromptPlus.WriteLine($"当前请求已达到上限，请等待重置后再试。重置时间:[cyan]{re.Reset.LocalDateTime}");
                    if (re.Limit <= 60)
                    {
                        PromptPlus.WriteLine($"可以在GitHub生成空权限的Person Token，配置到 appsettings.json 中 提高可请求上限。");
                    }
                }
                else if (process.ValueProcess is Exception e)
                {
                    throw e;
                }
            }
            catch (Exception e)
            {
                PromptPlus.WriteLine(e.Message.Red());
            }
            return repo != null;
        }

        static async Task<int> Main(string[] args)
        {
            Console.Title = "ManifestDownload By pjy612";
            PromptPlus.Banner("ManifestDownload").Run(ConsoleColor.Green);
            PromptPlus.WriteLine("By [cyan]pjy612[/] [yellow]本软件永久免费，请勿用于商业或非法用途");
            return await new AppRunner<Program>()
                .UseDefaultMiddleware()
                .UseNameCasing(Case.CamelCase)
                .UsePromptPlusAnsiConsole()
                .UsePrompter()
                .UsePromptPlusWizard()
                .UseArgumentPrompter()
                //.UsePromptPlusRepl(colorizeSessionInitMessage: (msg) => msg.Yellow().Underline())
                .RunAsync(args);
        }

        [Command("app",
            Description = "download latest manifests by appid",
            IgnoreUnexpectedOperands = true,
            UsageLines = new[]
            {
                "",
                "app 4000",
                "app 4000 990080",
            })]
        public async Task App(
            [Positional("appid", Description = "list of appIds")]
            params string[] appIds)
        {
            if (await Init())
            {
                DepotKeyStore.LoadData();
                foreach (string appId in appIds.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()))
                {
                    await github.DownloadAllByAppid(repo.Id, appId);
                }
                DepotKeyStore.Save();
            }
        }

        [Command("manifest",
            IgnoreUnexpectedOperands = true,
            Description = "download specify manifest by manifestId",
            UsageLines = new[]
            {
                "",
                "manifest 4004_2300048559188578691",
                "manifest 4004_2300048559188578691 1998962_5952380176714618960"
            })]
        public async Task Manifest(
            [Positional("manifestId", Description = "list of manifestIds")]
            params string[] manifestIds)
        {
            if (await Init())
            {
                Task[] array = manifestIds.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Select(manifestId => github.DownloadAllByManifest(repo.Id, manifestId)).ToArray();
                Task.WaitAll(array);
            }
        }

        [Command("dumpkey",
            IgnoreUnexpectedOperands = true,
            Description = "dump all DecryptionKey to keys.txt",
            UsageLines = new[]
            {
                "dumpkey"
            })]
        public async Task DumpKey()
        {
            DepotKeyStore.LoadData();
            string[] allVdf = Directory.GetFiles("depotcache","*.vdf",SearchOption.AllDirectories);
            foreach (var p in allVdf)
            {
                DumpForVdf(p);
            }
            DepotKeyStore.Save();
        }
        static KVSerializer kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        static void DumpForVdf(string path)
        {
            PromptPlus.WriteLine($"Dumping: [cyan]{path}");
            DumpForVdf(File.OpenRead(path));
        }
        public static void DumpForVdf(byte[] bytes)
        {
            DumpForVdf(new MemoryStream(bytes));
        }
        static void DumpForVdf(Stream stream)
        {
            try
            {
                KVObject depots = kv.Deserialize(stream);
                if (depots.Name == "depots")
                {
                    foreach (KVObject depot in depots.Children)
                    {
                        string depotName = depot.Name;
                        uint depotId = uint.Parse(depotName);
                        if (!DepotKeyStore.LocalKeys.ContainsKey(depotId))
                        {
                            var DecryptionKey = depot.Value["DecryptionKey"].ToString();
                            DepotKeyStore.LocalKeys[depotId] = DecryptionKey;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PromptPlus.WriteLine(e);
            }
        }

        private static IConfigurationRoot Config => GetSetting();

        static IConfigurationRoot GetSetting()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        }

        
    }

    static class GitHubClientExtension
    {
        const string depotcache = "depotcache";

        public static async Task DownloadAllByAppid(this GitHubClient github, long repoId, string appid)
        {
            try
            {
                string appDir = appid;
                TreeResponse tree = await github.Git.Tree.Get(repoId, appid);
                string basePath = Path.Combine(depotcache, appDir);
                Directory.CreateDirectory(basePath);
                if (tree.Tree.Any())
                {
                    PromptPlus.WriteLine($"AppId:[cyan]{appid}[/] [yellow]获取到清单列表...");
                }

                foreach (TreeItem treeItem in tree.Tree)
                {
                    string fullPath = Path.Combine(basePath, treeItem.Path);
                    if (treeItem.Path.EndsWith(".manifest"))
                    {
                        if (Directory.EnumerateFiles(depotcache, treeItem.Path, SearchOption.AllDirectories).Any())
                        {
                            PromptPlus.WriteLine($"已存在:[cyan]{treeItem.Path}");
                            continue;
                        }
                    }
                    if (treeItem.Type.Value == TreeType.Blob)
                    {
                        Blob blob = await github.Git.Blob.Get(repoId, treeItem.Sha);
                        byte[] bytes = blob.GetContent();
                        if (bytes.Any())
                        {
                            if (treeItem.Path.EndsWith(".vdf"))
                            {
                                Program.DumpForVdf(bytes);
                            }
                            PromptPlus.WriteLine($"下载完毕:[cyan]{treeItem.Path}");
                            await File.WriteAllBytesAsync(fullPath, bytes);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PromptPlus.WriteLine($"[red]下载出错[/] AppId:[cyan]{appid}\n[red]{e.Message}");
                if (e is RateLimitExceededException re)
                {
                    if (re.Limit <= 60)
                    {
                        PromptPlus.WriteLine($"可以在GitHub生成空权限的Person Token，配置到 appsettings.json 中 提高可请求上限。");
                    }
                }
            }
        }

        static byte[] GetContent(this Blob blob)
        {
            string blobContent = blob.Content;
            byte[] bytes = Array.Empty<byte>();
            switch (blob.Encoding.Value)
            {
                case EncodingType.Base64:
                    bytes = Convert.FromBase64String(blobContent);
                    break;
                case EncodingType.Utf8:
                    bytes = Encoding.UTF8.GetBytes(blobContent);
                    break;
            }

            return bytes;
        }

        public static async Task DownloadAllByManifest(this GitHubClient github, long repoId, string manifestId)
        {
            string basePath = Path.Combine(depotcache);
            Directory.CreateDirectory(basePath);
            //foreach (string manifestId in manifestIds)
            {
                try
                {
                    TreeResponse tree = await github.Git.Tree.Get(repoId, $"{manifestId}");
                    TreeItem treeItem = tree.Tree.FirstOrDefault(r => r.Path.StartsWith(manifestId));
                    if (treeItem != null)
                    {
                        if (treeItem.Type.Value == TreeType.Blob)
                        {
                            Blob blob = await github.Git.Blob.Get(repoId, treeItem.Sha);
                            byte[] bytes = blob.GetContent();
                            if (bytes.Any())
                            {
                                PromptPlus.WriteLine($"下载完毕:[cyan]{treeItem.Path}");
                                string filePath = Path.Combine(basePath, treeItem.Path);
                                await File.WriteAllBytesAsync(filePath, bytes);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    PromptPlus.WriteLine($"下载出错:{manifestId} {e.Message}", ConsoleColor.Red);
                    if (e is RateLimitExceededException re)
                    {
                        if (re.Limit <= 60)
                        {
                            PromptPlus.WriteLine($"可以在GitHub生成空权限的Person Token，配置到 appsettings.json 中 提高可请求上限。");
                        }
                    }
                }
            }
        }
    }
}