using HtmlAgilityPack;
using Newtonsoft.Json;
using RestSharp;
using System.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SteamAchievementsGenerator
{
    class Program
    {
        private static string maindir;
        private static string filesdir;
        private static string folder;
        private static AchGen achgen;
        private static readonly string invalidChars = new string(Path.GetInvalidFileNameChars());
        private static readonly Dictionary<string, string> lang_names = new Dictionary<string, string>();

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            return EmbeddedAssembly.Get(args.Name);
        }

        static void Main(string[] args)
        {
            Console.WriteLine(@"Achievements Generator
Version 1.2.0 (2025 layout support)
Programmed by Colmines92 / Updated by khanhkhoktvn
");

            string resource1 = "SteamAchievementsGenerator.Resources.HtmlAgilityPack.dll";
            string resource2 = "SteamAchievementsGenerator.Resources.RestSharp.dll";
            string resource3 = "SteamAchievementsGenerator.Resources.Newtonsoft.Json.dll";
            EmbeddedAssembly.Load(resource1, "HtmlAgilityPack.dll");
            EmbeddedAssembly.Load(resource2, "RestSharp.dll");
            EmbeddedAssembly.Load(resource3, "Newtonsoft.Json.dll");

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);

            lang_names.Add("simplified chinese", "schinese");
            lang_names.Add("traditional chinese", "tchinese");
            lang_names.Add("korean", "koreana");
            lang_names.Add("portuguese - brazil", "brazilian");

            maindir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (!string.IsNullOrEmpty(maindir))
                maindir = maindir.Replace("\\", "/");

            if (args.Length < 1)
            {
                PrintUsage();
                return;
            }

            var filename = args[0].Replace('\\', '/');
            if (!File.Exists(filename))
            {
                PrintUsage();
                return;
            }

            string filedir = Path.GetDirectoryName(filename);
            string filenameWithoutExt = Path.GetFileNameWithoutExtension(filename);
            filesdir = string.Join("/", new string[] { filedir, filenameWithoutExt + "_files" });
            if (!Directory.Exists(filesdir)) Directory.CreateDirectory(filesdir);

            achgen = new AchGen(filename);
            if (achgen.AppId == "0")
            {
                PrintUsage();
                return;
            }

            folder = Path.Combine(maindir, achgen.Name == "" ? achgen.AppId : achgen.AppId + " - " + achgen.Name);
            if (!MakeDir(folder)) return;

            var achievements = achgen.GetAchievements();
            var stats = achgen.GetStats();
            var dlc = achgen.GetDLC();

            SaveFile("steam_appid.txt", achgen.AppId, "text");

            if (achievements != null && achievements.Length != 0)
                SaveFile("achievements.json", achievements, "achievements");

            if (stats != null && stats.Count != 0)
                SaveFile("stats.txt", stats, "stats");

            if (dlc != null && dlc.Count != 0)
                SaveFile("DLC.txt", dlc, "dlc");

            Console.WriteLine("\nDone! Output folder:");
            Console.WriteLine(folder);
            Console.ReadKey();
        }

        static void PrintUsage()
        {
            Console.WriteLine(@"USAGE:
    1. Go to https://steamdb.info/
    2. Search your game, click it.
    3. Open the “Stats” tab (that’s now the Achievements page).
    4. Save the HTML file and its “_files” folder.
    5. Drag the HTML file onto this exe.");
            Console.ReadKey();
        }

        static bool MakeDir(string path, bool forced = true)
        {
            try
            {
                if (Directory.Exists(path)) return true;
                if (File.Exists(path))
                {
                    if (!forced) return false;
                    File.Delete(path);
                }
                Directory.CreateDirectory(path);
                return true;
            }
            catch { return false; }
        }

        public static bool CopyFile(string src, string dst)
        {
            try
            {
                if (!File.Exists(src)) return false;
                MakeDir(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
                return true;
            }
            catch { return false; }
        }

        static void SaveFile(string name, object content, string mode = "stats")
        {
            if (content == null) return;
            var filename = Path.Combine(folder, name);

            using (var file = new StreamWriter(filename, false, new System.Text.UTF8Encoding(false)))
            {
                switch (mode)
                {
                    case "achievements":
                        var achievements = (Achievement[])content;
                        if (achievements.Length == 0) return;
                        string json = JsonConvert.SerializeObject(achievements, Formatting.Indented);
                        file.Write(json);
                        break;

                    case "stats":
                        var list = (List<Dictionary<string, string>>)content;
                        foreach (var dict in list)
                        {
                            string nameKey = dict["name"];
                            string value = dict["defaultValue"];
                            string type = value.Contains('.') ? "float" : "int";
                            file.WriteLine($"{nameKey}={type}={value}");
                        }
                        break;

                    case "dlc":
                        var dlcvalue = (List<string>)content;
                        if (dlcvalue.Count == 0) return;
                        file.WriteLine(string.Join("\n", dlcvalue));
                        break;

                    default:
                        string valueText = content.ToString();
                        if (valueText.Length == 0 || valueText == "0") return;
                        file.Write(valueText);
                        break;
                }
            }
        }

        public class Achievement
        {
            public string name = "";
            public Dictionary<string, string> displayName = new Dictionary<string, string>();
            public Dictionary<string, string> description = new Dictionary<string, string>();
            public string hidden = "";
            public string icon = "";
            public string icon_gray = "";
        }

        class AchGen
        {
            private readonly HtmlDocument soup;
            public readonly string AppId;
            public readonly string Name;

            public AchGen(string filename)
            {
                var content = File.ReadAllText(filename);
                soup = new HtmlDocument();
                soup.LoadHtml(content);
                AppId = soup.DocumentNode.SelectSingleNode("//div[@class='scope-app']")?.GetAttributeValue("data-appid", "0");
                Name = ValidateFileName(soup?.DocumentNode.SelectSingleNode("//h1[contains(@itemprop,'name')]")?.InnerText ?? "");
            }

            public Achievement[] GetAchievements()
            {
                string fullName = achgen.AppId + (string.IsNullOrEmpty(achgen.Name) ? "" : $": {achgen.Name}");
                Console.WriteLine($"Generating achievements for {fullName}...");
                Console.WriteLine("Please wait...");

                var achievements = new List<Achievement>();

                
                var achievementDivs = soup.DocumentNode.SelectNodes("//div[contains(@class,'achievement') and @id]");
                if (achievementDivs == null || achievementDivs.Count == 0)
                {
                    Console.WriteLine("No achievements found — check if you saved the /stats/ page fully.");
                    return achievements.ToArray();
                }

                var imgdir = Path.Combine(folder, "images");
                MakeDir(imgdir);

                foreach (var achNode in achievementDivs)
                {
                    try
                    {
                        var data = new Achievement();

                        // API name
                        var apiNode = achNode.SelectSingleNode(".//div[contains(@class,'achievement_api')]");
                        data.name = apiNode?.InnerText.Trim() ?? "";

                        // Display name
                        var nameNode = achNode.SelectSingleNode(".//div[contains(@class,'achievement_name')]");
                        var descNode = achNode.SelectSingleNode(".//div[contains(@class,'achievement_desc')]");
                        var spoilerNode = descNode?.SelectSingleNode(".//span[contains(@class,'achievement_spoiler')]");

                        string displayName = nameNode?.InnerText.Trim() ?? "";
                        string description = spoilerNode != null ? spoilerNode.InnerText.Trim() :
                                             descNode?.InnerText.Trim() ?? "";

                        // Hidden achievements
                        data.hidden = descNode != null && descNode.InnerHtml.Contains("Hidden achievement") ? "1" : "0";

                        data.displayName["english"] = displayName;
                        data.description["english"] = description;

                        // Icons
                        var mainIcon = achNode.SelectSingleNode(".//img[contains(@class,'achievement_image')]");
                        var grayIcon = achNode.SelectSingleNode(".//img[contains(@class,'achievement_image_small')]");
                        string iconName = mainIcon?.GetAttributeValue("data-name", "") ?? "";
                        string grayName = grayIcon?.GetAttributeValue("data-name", "") ?? "";

                        if (!string.IsNullOrEmpty(iconName))
                        {
                            data.icon = $"images/{iconName}";
                            CopyFile($"{filesdir}/{iconName}", $"{imgdir}/{iconName}");
                        }
                        if (!string.IsNullOrEmpty(grayName))
                        {
                            data.icon_gray = $"images/{grayName}";
                            CopyFile($"{filesdir}/{grayName}", $"{imgdir}/{grayName}");
                        }

                        achievements.Add(data);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing one achievement: {ex.Message}");
                    }
                }

                Console.WriteLine($"Parsed {achievements.Count} achievements successfully!");
                return achievements.ToArray();
            }


            public List<Dictionary<string, string>> GetStats()
            {
                var stats = new List<Dictionary<string, string>>();
                var statsContainer = soup.DocumentNode.SelectSingleNode("//div[contains(@id,'js-stats')]");
                if (statsContainer == null) return stats;

                var rows = statsContainer.SelectNodes(".//tr");
                if (rows == null || rows.Count == 0) return stats;

                foreach (var row in rows)
                {
                    var tds = row.SelectNodes(".//td");
                    if (tds == null || tds.Count < 3) continue;

                    stats.Add(new Dictionary<string, string>
                    {
                        ["name"] = tds[0].InnerText.Trim(),
                        ["displayName"] = tds[1].InnerText.Trim(),
                        ["defaultValue"] = tds[2].InnerText.Trim()
                    });
                }

                return stats;
            }

            public List<string> GetDLC()
            {
                var dlc = new List<string>();
                var dlcTable = soup.DocumentNode.SelectSingleNode("//div[@id='dlc']");
                if (dlcTable == null) return dlc;

                foreach (var row in dlcTable.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var tds = row.SelectNodes(".//td");
                    if (tds == null || tds.Count < 2) continue;
                    dlc.Add($"{tds[0].InnerText.Trim()}={tds[1].InnerText.Trim()}");
                }

                return dlc;
            }

            public string ValidateFileName(string fileName = "")
            {
                foreach (char c in invalidChars)
                    fileName = fileName.Replace(c.ToString(), "_");
                return fileName;
            }
        }
    }
}

