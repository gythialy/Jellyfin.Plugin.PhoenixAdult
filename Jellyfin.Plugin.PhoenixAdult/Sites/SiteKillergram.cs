using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.PhoenixAdult.Models;

namespace Jellyfin.Plugin.PhoenixAdult.Sites
{
    public class SiteKillergram : SiteBase
    {
        public override string Name => "Killergram";
        public override string DefaultUrl => "https://www.killergram.com/";
        public override List<string> AlternativeUrls => new List<string>();

        public override Regex SceneUrlRegex => new Regex(@"(?<baseUrl>https?://www\.killergram\.com/video/)(?<id>.*?)/(?<slug>.*?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public override Regex SceneIdRegex => new Regex(@"(?<baseUrl>https?://www\.killergram\.com/video/)(?<id>.*?)/(?<slug>.*?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Regex _sceneDateRegex = new Regex(@".*?(\d{1,2}\s(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override List<SearchResult> Search(string query)
        {
            return Search(query, _sceneDateRegex);
        }

        public override Scene Scrape(string url)
        {
            return Scrape(url, _sceneDateRegex);
        }
    }
}
