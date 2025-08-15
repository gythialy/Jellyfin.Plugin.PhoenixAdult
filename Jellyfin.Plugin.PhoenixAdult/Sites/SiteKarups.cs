using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.PhoenixAdult.Models;

namespace Jellyfin.Plugin.PhoenixAdult.Sites
{
    public class SiteKarups : SiteBase
    {
        public override string Name => "Karups";
        public override string DefaultUrl => "https://www.karups.com/";
        public override List<string> AlternativeUrls => new List<string>() { "https://www.karupsow.com/", "https://www.karupsha.com/", "https://www.karupspc.com/" };

        public override Regex SceneUrlRegex => new Regex(@"(?<baseUrl>https?://www\.(?:karups|karupsow|karupsha|karupspc)\.com/video/)(?<id>.*?)/(?<slug>.*?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public override Regex SceneIdRegex => new Regex(@"(?<baseUrl>https?://www\.(?:karups|karupsow|karupsha|karupspc)\.com/video/)(?<id>.*?)/(?<slug>.*?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Regex _sceneDateRegex = new Regex(@".*?(\d{1,2}\s(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Regex _subsiteRegex = new Regex(@"karupsow|karupsha|karupspc", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override List<SearchResult> Search(string query)
        {
            return Search(query, _sceneDateRegex, _subsiteRegex);
        }

        public override Scene Scrape(string url)
        {
            return Scrape(url, _sceneDateRegex, _subsiteRegex);
        }
    }
}
