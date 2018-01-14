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
        public List<string> CompletedList = new List<string>();

        public static void Main(string[] args)
        {
            new Program().Start().GetAwaiter().GetResult();
        }

        public async Task Start()
        {
            Config.CheckExistence();

            var consumerKey = Config.Load().ConsumerKey;
            var consumerKeySecret = Config.Load().ConsumerKeySecret;
            var accessToken = Config.Load().AccessToken;
            var accessTokenSecret = Config.Load().AccessTokenSecret;
            var twitter = new TwitterApi(consumerKey, consumerKeySecret, accessToken, accessTokenSecret);

            Console.WriteLine($"RSS URL: {Config.Load().RssUrl}\n" +
                              $"Update Frequency: 1/{Config.Load().Frequency} minutes\n" +
                              $"Blacklist: {string.Join(", ", Config.Load().BlacklistedWords)}\n");

            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "setup/log.json")))
            {
                CompletedList = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "setup/log.json")));
            }

            while (true)
            {
                var feedItems = (await Index(Config.Load().RssUrl)).Where(x =>
                    x.PublishDate.ToUniversalTime() + TimeSpan.FromMinutes(Config.Load().Frequency) > DateTime.UtcNow);

                foreach (var item in feedItems)
                    try
                    {
                        if (CompletedList.Any(x =>
                            string.Equals(x, item.Title, StringComparison.CurrentCultureIgnoreCase)) && Config.Load().RemoveDuplicates)
                        {
                            throw new Exception($"REMOVED DUPLICATE: {item.Title}");
                        }
                        if (Config.Load().BlacklistedWords.Any(x => string.Equals(x, item.Title, StringComparison.CurrentCultureIgnoreCase)))
                        {
                            throw new Exception($"BLACKLISTED: {item.Title}");
                        }

                        await twitter.Tweet($"{item.Title}\n" +
                                            $"{item.Link}");
                        Console.WriteLine(
                            $"{item.Title}                                                                    "
                                .Substring(0, 30));

                        CompletedList.Add(item.Title.ToLower());

                        var file = Path.Combine(AppContext.BaseDirectory, "setup/log.json");
                        File.WriteAllText(file, JsonConvert.SerializeObject(CompletedList, Formatting.Indented));

                        await Task.Delay(1000);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }


                await Task.Delay(1000 * 60 * Config.Load().Frequency);
            }
            // ReSharper disable once FunctionNeverReturns
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

                //Convert each item into a FeedItem
                var feedItems = doc.Root.Descendants()
                    .First(i => i.Name.LocalName == "channel")
                    .Elements()
                    .Where(i => i.Name.LocalName == "item")
                    .Select(item => new FeedItem
                    {
                        Content = item.Elements().First(i => i.Name.LocalName == "description").Value,
                        Link = item.Elements().First(i => i.Name.LocalName == "link").Value,
                        PublishDate =
                            DateTime.TryParse(item.Elements().First(i => i.Name.LocalName == "pubDate").Value,
                                out var result)
                                ? result
                                : DateTime.MinValue,
                        Title = item.Elements().First(i => i.Name.LocalName == "title").Value,
                        RssRootUrl = $"{new Uri(feedUrl).Scheme}://{new Uri(feedUrl).Host}",
                        PostRootUrl =
                            $"{new Uri(item.Elements().First(i => i.Name.LocalName == "link").Value).Scheme}://{new Uri(item.Elements().First(i => i.Name.LocalName == "link").Value).Host}"
                    });
                articles = feedItems.ToList();
            }

            return articles;
        }

        /// <summary>
        /// This is the object for each RSS Post
        /// it contains all relevant info for Each Post
        /// </summary>
        public class FeedItem
        {
            public string Link { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public DateTime PublishDate { get; set; }
            public string RssRootUrl { get; set; }
            public string PostRootUrl { get; set; }
        }

        public class TwitterApi
        {
            public const string TwitterApiBaseUrl = "https://api.twitter.com/1.1/";
            public readonly string ConsumerKey, ConsumerKeySecret, AccessToken, AccessTokenSecret;
            public readonly DateTime EpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public readonly HMACSHA1 SigHasher;

            /// <summary>
            /// Creates an object for sending tweets to Twitter using Single-user OAuth.
            /// 
            /// Get your access keys by creating an app at apps.twitter.com then visiting the
            /// "Keys and Access Tokens" section for your app. They can be found under the
            /// "Your Access Token" heading.
            /// </summary>
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

            /// <summary>
            /// Sends a tweet with the supplied text and returns the response from the Twitter API.
            /// </summary>
            public Task<string> Tweet(string text)
            {
                var data = new Dictionary<string, string>
                {
                    {"status", text},
                    {"trim_user", "1"}
                };

                return SendRequest("statuses/update.json", data);
            }

            /// <summary>
            /// Send HTTP Request and return the response.
            /// </summary>
            private Task<string> SendRequest(string url, Dictionary<string, string> data)
            {
                var fullUrl = TwitterApiBaseUrl + url;

                // Timestamps are in seconds since 1/1/1970.
                var timestamp = (int) (DateTime.UtcNow - EpochUtc).TotalSeconds;

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

            /// <summary>
            /// Generate an OAuth signature from OAuth header values.
            /// </summary>
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


            /// <summary>
            /// Generate the raw OAuth HTML header from the values (including signature).
            /// </summary>
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
}