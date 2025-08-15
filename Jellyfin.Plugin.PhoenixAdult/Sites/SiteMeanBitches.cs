using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.PhoenixAdult.Models;

namespace Jellyfin.Plugin.PhoenixAdult.Sites
{
    public class SiteMeanBitches : IProviderBase
    {
        public string Name => "Mean Bitches";
        public string DefaultUrl => "https://www.meanbitches.com/";
        public List<string> AlternativeUrls => new List<string>();

        public Regex SceneUrlRegex => new Regex(@"(?<baseUrl>https?://www\.meanbitches\.com/scene/)(?<id>.*?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public Regex SceneIdRegex => new Regex(@"(?<baseUrl>https?://www\.meanbitches\.com/scene/)(?<id>.*?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Regex _sceneDateRegex = new Regex(@".*?(\d{1,2}\s(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public List<SearchResult> Search(string query)
        {
            return new List<SearchResult>();
        }

        public Scene Scrape(string url)
        {
            return new Scene();
        }
    }
}
