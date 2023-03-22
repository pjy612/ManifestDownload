using System.Reflection.Metadata;
using System.Text;
using CommandDotNet;
using CommandDotNet.NameCasing;
using Microsoft.Extensions.Configuration;
using Octokit;
using Octokit.Internal;
using PPlus;
using PPlus.CommandDotNet;
using PPlus.Objects;
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

        static Task Init()
        {
            PromptPlus.WaitProcess("获取远程清单仓库")
                .AddProcess(new SingleProcess(async t =>
                {
                    github = new GitHubClient(new ProductHeaderValue("SteamDepotDownload"));
                    string token = Config[tokenKey];
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        github.Credentials = new Credentials(token);
                    }
                    repo = await github.Repository.Get(repoOwner, repoName);
                    return "Done";
                })).Run();
            return Task.CompletedTask;
        }

        static async Task<int> Main(string[] args)
        {
            PromptPlus.Banner("ManifestDownload").Run(ConsoleColor.Green);
            PromptPlus.WriteLine("By [cyan]pjy612");
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
            await Init();
            foreach (string appId in appIds.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()))
            {
                await github.DownloadAllByAppid(repo.Id, appId);
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
            await Init();
            Task[] array = manifestIds.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Select(manifestId => github.DownloadAllByManifest(repo.Id, manifestId)).ToArray();
            Task.WaitAll(array);
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
                    if (treeItem.Type.Value == TreeType.Blob)
                    {
                        Blob blob = await github.Git.Blob.Get(repoId, treeItem.Sha);
                        byte[] bytes = blob.GetContent();
                        if (bytes.Any())
                        {
                            PromptPlus.WriteLine($"下载完毕:[cyan]{treeItem.Path}");
                            await File.WriteAllBytesAsync(Path.Combine(basePath, treeItem.Path), bytes);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PromptPlus.WriteLine($"[red]下载出错[/] AppId:[cyan]{appid}\n[red]{e.Message}");
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
                                await File.WriteAllBytesAsync(Path.Combine(basePath, treeItem.Path), bytes);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    PromptPlus.WriteLine($"下载出错:{manifestId} {e.Message}", ConsoleColor.Red);
                }
            }
        }
    }
}