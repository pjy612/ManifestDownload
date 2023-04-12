using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManifestDownload
{
    public static class DepotKeyStore
    {
        public static Dictionary<uint, string> LocalKeys = new Dictionary<uint, string>();
        public static Dictionary<uint, (string, ulong)> LocalApps = new Dictionary<uint, (string, ulong)>();
        private const string keyFileName = "keys.txt";
        private const string appFileName = "apps.txt";

        private static void LoadKeys()
        {
            if (File.Exists(keyFileName))
            {
                string[] readAllLines = File.ReadAllLines(keyFileName);
                foreach (string line in readAllLines)
                {
                    string[] key = line.Trim(' ', '"').Replace(":", ";")
                        .Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                    if (key.Length == 2)
                    {
                        if (!string.IsNullOrWhiteSpace(key[1]))
                        {
                            if (uint.TryParse(key[0].Trim(' ', '"'), out uint k))
                            {
                                LocalKeys[k] = key[1].ToLower().Trim(' ', '"');
                            }
                        }
                    }
                }
            }
            List<uint> errorIds = new List<uint>();
            foreach (var pair in LocalKeys)
            {
                try
                {
                    var hexByte = pair.Value.ToHexByte();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error Key By depotid:{pair.Key}！");
                    errorIds.Add(pair.Key);
                }
            }
            if (errorIds.Any())
            {
                errorIds.ForEach(id => LocalKeys.Remove(id));
            }
        }

        public static HashSet<uint> LoadAppsFromFile(string appFileName)
        {
            var hashSet = new HashSet<uint>();
            if (File.Exists(appFileName))
            {
                string[] readAllLines = File.ReadAllLines(appFileName);
                foreach (string line in readAllLines)
                {
                    string[] key = line
                        .Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                    if (key.Length > 0)
                    {
                        if (uint.TryParse(key[0], out uint k))
                        {
                            hashSet.Add(k);
                        }
                    }
                }
            }
            return hashSet;
        }
        private static void LoadApps()
        {
            if (File.Exists(appFileName))
            {
                string[] readAllLines = File.ReadAllLines(appFileName);
                foreach (string line in readAllLines)
                {
                    string[] key = line
                        .Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                    if (key.Length == 3)
                    {
                        if (!string.IsNullOrWhiteSpace(key[1]))
                        {
                            if (uint.TryParse(key[0], out var k))
                            {
                                if (ulong.TryParse(key[2], out var appToken))
                                {
                                    LocalApps[k] = (key[1], appToken);
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void Save()
        {
            File.WriteAllLines(keyFileName, LocalKeys.Select(r => $"{r.Key};{r.Value}"));
        }

        /// <summary>
        /// 字符串转16进制字节数组
        /// </summary>
        /// <param name="hexString"></param>
        /// <returns></returns>
        public static byte[] ToHexByte(this string hexString)
        {
            return Enumerable.Range(0, hexString.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hexString.Substring(x, 2), 16))
                .ToArray();
        }

        /// <summary>
        /// 字节数组转16进制字符串
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string ToHexStr(this byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        public static void LoadData()
        {
            LoadKeys();
        }
    }
}
