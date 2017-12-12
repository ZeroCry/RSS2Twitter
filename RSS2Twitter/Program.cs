using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace RSS2Twitter
{
    public class Program
    {
        public static void Main(string[] args)
        {
            new Program().Start().GetAwaiter().GetResult();
        }

        public async Task Start()
        {
            Config.CheckExistence();
            //var response = await twitter.Tweet("This is my first automated tweet!");
            //Console.WriteLine(response);


            await DoRss();
        }

        public class FeedItem
        {
            public string Link { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public DateTime PublishDate { get; set; }
            public string RssRootUrl { get; set; }
            public string PostRootUrl { get; set; }
        }

        public async Task<List<FeedItem>> Index(string url)
        {
            var articles = new List<FeedItem>();
            var feedUrl = url;
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(feedUrl);
                var responseMessage = await client.GetAsync(feedUrl);
                var responseString = await responseMessage.Content.ReadAsByteArrayAsync();
                var rs2 = Encoding.UTF8.GetString(responseString);

                //extract feed items
                var doc = XDocument.Parse(rs2);
                if (doc.Root == null) return articles;

                var feedItems = from item in doc.Root.Descendants().First(i => i.Name.LocalName == "channel")
                        .Elements().Where(i => i.Name.LocalName == "item")
                                select new FeedItem
                                {
                                    Content = item.Elements().First(i => i.Name.LocalName == "description").Value,
                                    Link = item.Elements().First(i => i.Name.LocalName == "link").Value,
                                    PublishDate = ParseDate(item.Elements().First(i => i.Name.LocalName == "pubDate").Value),
                                    Title = item.Elements().First(i => i.Name.LocalName == "title").Value,
                                    RssRootUrl = $"{new Uri(feedUrl).Scheme}://{new Uri(feedUrl).Host}",
                                    PostRootUrl =
                                        $"{new Uri(item.Elements().First(i => i.Name.LocalName == "link").Value).Scheme}://{new Uri(item.Elements().First(i => i.Name.LocalName == "link").Value).Host}"
                                };
                articles = feedItems.ToList();
            }

            return articles;
        }

        private static DateTime ParseDate(string date)
        {
            return DateTime.TryParse(date, out var result) ? result : DateTime.MinValue;
        }

        //public static List<RssObject> RssFeeds = new List<RssObject>();

        //public class RssObject
        //{
        //    public string Formatting { get; set; } = "$posttitle\n" +
        //                                             "$postlink";
        //    public string RssUrl { get; set; }
        //}

        private async Task DoRss()
        {
            var consumerKey = Config.Load().ConsumerKey;
            var consumerKeySecret = Config.Load().ConsumerKeySecret;
            var accessToken = Config.Load().AccessToken;
            var accessTokenSecret = Config.Load().AccessTokenSecret;
            var twitter = new TwitterApi(consumerKey, consumerKeySecret, accessToken, accessTokenSecret);

            while (true)
            {
                var feedItems = (await Index(Config.Load().RssUrl)).Where(x =>
                    x.PublishDate.ToUniversalTime() + TimeSpan.FromMinutes(1000000) > DateTime.UtcNow);

                foreach (var item in feedItems)
                {
                    try
                    {
                        /*var desc = Regex.Replace(item.Content, "<.*?>", "");
                        var d2 = feed.Formatting
                            .Replace("$posttitle", item.Title)
                            .Replace("$postlink", item.Link)
                            //.Replace("$postcontent", desc)
                            .Replace("$postrssroot", item.RssRootUrl)
                            .Replace("$postroot", item.PostRootUrl)
                            .Replace("$rssurl", feed.RssUrl);*/

                        if (item.Title.ToLower().Contains("sock"))
                        {
                            throw new Exception("Proxy");
                        }

                        await twitter.Tweet($"{item.Title}\n" +
                                            $"{item.Link}");
                        Console.WriteLine($"{item.Title}                                                                    ".Substring(0, 30));

                        await Task.Delay(1000);

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }




                await Task.Delay(1000 * 60 * 10);
            }
            // ReSharper disable once FunctionNeverReturns
        }

        public class TwitterApi
        {
            public const string TwitterApiBaseUrl = "https://api.twitter.com/1.1/";
            public readonly string ConsumerKey, ConsumerKeySecret, AccessToken, AccessTokenSecret;
            public readonly HMACSHA1 SigHasher;
            public readonly DateTime EpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public TwitterApi(string consumerKey, string consumerKeySecret, string accessToken,
                string accessTokenSecret)
            {
                ConsumerKey = consumerKey;
                ConsumerKeySecret = consumerKeySecret;
                AccessToken = accessToken;
                AccessTokenSecret = accessTokenSecret;

                SigHasher =
                    new HMACSHA1(
                        new ASCIIEncoding().GetBytes(string.Format("{0}&{1}", consumerKeySecret, accessTokenSecret)));
            }

            public Task<string> Tweet(string text)
            {
                var data = new Dictionary<string, string>
                {
                    {"status", text},
                    {"trim_user", "1"}
                };

                return SendRequest("statuses/update.json", data);
            }

            private Task<string> SendRequest(string url, Dictionary<string, string> data)
            {
                var fullUrl = TwitterApiBaseUrl + url;

                // Timestamps are in seconds since 1/1/1970.
                var timestamp = (int)((DateTime.UtcNow - EpochUtc).TotalSeconds);

                // Add all the OAuth headers we'll need to use when constructing the hash.
                data.Add("oauth_consumer_key", ConsumerKey);
                data.Add("oauth_signature_method", "HMAC-SHA1");
                data.Add("oauth_timestamp", timestamp.ToString());
                data.Add("oauth_nonce", "a"); // Required, but Twitter doesn't appear to use it, so "a" will do.
                data.Add("oauth_token", AccessToken);
                data.Add("oauth_version", "1.0");

                // Generate the OAuth signature and add it to our payload.
                data.Add("oauth_signature", GenerateSignature(fullUrl, data));

                // Build the OAuth HTTP Header from the data.
                var oAuthHeader = GenerateOAuthHeader(data);

                // Build the form data (exclude OAuth stuff that's already in the header).
                var formData = new FormUrlEncodedContent(data.Where(kvp => !kvp.Key.StartsWith("oauth_")));

                return SendRequest(fullUrl, oAuthHeader, formData);
            }

            private string GenerateSignature(string url, Dictionary<string, string> data)
            {
                var sigString = string.Join(
                    "&",
                    data
                        .Union(data)
                        .Select(kvp =>
                            string.Format("{0}={1}", Uri.EscapeDataString(kvp.Key), Uri.EscapeDataString(kvp.Value)))
                        .OrderBy(s => s)
                );

                var fullSigData = string.Format(
                    "{0}&{1}&{2}",
                    "POST",
                    Uri.EscapeDataString(url),
                    Uri.EscapeDataString(sigString)
                );

                return Convert.ToBase64String(
                    SigHasher.ComputeHash(new ASCIIEncoding().GetBytes(fullSigData)));
            }

            private static string GenerateOAuthHeader(Dictionary<string, string> data)
            {
                return "OAuth " + string.Join(
                           ", ",
                           data
                               .Where(kvp => kvp.Key.StartsWith("oauth_"))
                               .Select(kvp => string.Format("{0}=\"{1}\"", Uri.EscapeDataString(kvp.Key),
                                   Uri.EscapeDataString(kvp.Value)))
                               .OrderBy(s => s)
                       );
            }

            private static async Task<string> SendRequest(string fullUrl, string oAuthHeader,
                HttpContent formData)
            {
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Add("Authorization", oAuthHeader);

                    var httpResp = await http.PostAsync(fullUrl, formData);
                    var respBody = await httpResp.Content.ReadAsStringAsync();

                    return respBody;
                }
            }
        }

    }

    public class Config
    {
        [JsonIgnore] public static readonly string Appdir = AppContext.BaseDirectory;


        public static string ConfigPath = Path.Combine(AppContext.BaseDirectory, "setup/config.json");

        public string ConsumerKey { get; set; } = "";
        public string ConsumerKeySecret { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string AccessTokenSecret { get; set; } = "";
        public string RssUrl { get; set; } = "";


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
                {
                    throw new NullReferenceException();
                }
            }
            catch
            {
                {
                    Console.WriteLine("Run (Y for run, N for setup Config)");

                    Console.WriteLine("Y or N: ");
                    var res = Console.ReadKey();
                    if (res.KeyChar == 'N' || res.KeyChar == 'n')
                        File.Delete("setup/config.json");

                    if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "setup/")))
                        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "setup/"));
                }


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
                    Console.Write("Please enter your Access Token: ");
                    cfg.AccessToken = Console.ReadLine();
                    Console.Write("Please enter your Access Token Secret: ");
                    cfg.AccessTokenSecret = Console.ReadLine();

                    Console.Write("Please enter your Desired RSS Url: ");
                    cfg.RssUrl = Console.ReadLine();

                    cfg.Save();
                }
            }
        }
    }
}