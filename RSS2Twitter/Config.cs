using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RSS2Twitter
{
    public class Config
    {
        [JsonIgnore] public static readonly string Appdir = AppContext.BaseDirectory;


        public static string ConfigPath = Path.Combine(AppContext.BaseDirectory, "setup/config.json");

        public string ConsumerKey { get; set; } = "";
        public string ConsumerKeySecret { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string AccessTokenSecret { get; set; } = "";
        public string RssUrl { get; set; } = "";
        public int Frequency { get; set; } = 10;
        public bool RemoveDuplicates { get; set; } = true;
        public List<string> BlacklistedWords { get; set; } = new List<string>();


        public void Save(string dir = "setup/config.json")
        {
            var file = Path.Combine(Appdir, dir);
            File.WriteAllText(file, ToJson());
        }

        public static Config Load(string dir = "setup/config.json")
        {
            var file = Path.Combine(Appdir, dir);
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(file));
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        public static void CheckExistence()
        {
            try
            {
                var accessToken = Load().AccessToken;
                if (string.IsNullOrEmpty(accessToken))
                    throw new NullReferenceException();
            }
            catch
            {
                {
                    Console.WriteLine("Run (Y for run, N for setup Config)");

                    Console.WriteLine("Y or N: ");
                    var res = Console.ReadKey();
                    if (res.KeyChar == 'N' || res.KeyChar == 'n')
                    {
                        try
                        {
                            File.Delete("setup/config.json");
                        }
                        catch
                        {
                            //
                        }
                    }
                        

                    if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "setup/")))
                        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "setup/"));
                }

                Console.Write("\n");
                if (!File.Exists(ConfigPath))
                {
                    var cfg = new Config();

                    Console.WriteLine(
                        "Please go to https://apps.twitter.com/ and create a new app\n" +
                        "Once Created, go to 'Keys and Access Tokens'\n" +
                        "Make sure you have generated your access token\n" +
                        "Press Any Key to Continue");
                    Console.ReadKey();
                    Console.Write("Please enter your Consumer Key (API Key): \n");
                    cfg.ConsumerKey = Console.ReadLine();
                    Console.Write("Please enter your Consumer Key Secret (API Secret): \n");
                    cfg.ConsumerKeySecret = Console.ReadLine();
                    Console.Write("Please enter your Access Token: \n");
                    cfg.AccessToken = Console.ReadLine();
                    Console.Write("Please enter your Access Token Secret: \n");
                    cfg.AccessTokenSecret = Console.ReadLine();

                    Console.Write("Please enter your Desired RSS Url: \n");
                    cfg.RssUrl = Console.ReadLine();

                    Console.Write("Please enter your desired frequency of RSS Update Checks \n" +
                                  "ie. 5 = RSS Updates are checked every 5 minutes: \n");
                    cfg.Frequency = Convert.ToInt32(Console.ReadLine());

                    Console.Write("Please write any words/phrases you would like to blacklist from the RSS Updates, these words will be filtered and removed before being posted.\n" +
                                  "Separate different words/phrases with a comma (,):\n");
                    cfg.BlacklistedWords = Console.ReadLine()?.Split(',').ToList();

                    Console.Write("Type Y if you would like to remove duplicate RSS Listings (based on title)\n" +
                                  "Type N if you owuld like to allow these.\n");
                    var res = Console.ReadKey();
                    if (res.KeyChar == 'N' || res.KeyChar == 'n')
                    {
                        cfg.RemoveDuplicates = false;
                    }
                    else
                    {
                        cfg.RemoveDuplicates = true;
                    }


                    cfg.Save();
                }
            }
        }
    }
}