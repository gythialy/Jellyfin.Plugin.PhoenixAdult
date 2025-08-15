import os
import re
import subprocess
import json
import sys

def extract_python_code(plex_path):
    # Extract the python file from the zip
    # The -o option is to overwrite existing files without prompting
    subprocess.run(['unzip', '-o', 'plex_plugin.zip', plex_path], capture_output=True, text=True)
    with open(plex_path, 'r', encoding='utf-8') as f:
        return f.read()

def parse_genres_db(code):
    match = re.search(r'genresDB\s*=\s*({[^}]+})', code, re.DOTALL)
    if not match:
        return None
    try:
        # This is a bit hacky, converting python dict to json
        py_dict_str = match.group(1).replace("'", '"')
        # Add quotes around keys
        py_dict_str = re.sub(r'([{,]\s*)(\w+)(\s*:)', r'\1"\2"\3', py_dict_str)
        return json.loads(py_dict_str)
    except json.JSONDecodeError:
        return None

def generate_cs_genres_db(genres_data):
    if not genres_data:
        return ""

    cs_dict = "private static readonly Dictionary<string, List<string>> genresDB = new Dictionary<string, List<string>>\n        {\n"
    for key, values in genres_data.items():
        cs_values = ", ".join([f'"{v}"' for v in values])
        cs_dict += f'            {{ "{key}", new List<string> {{ {cs_values} }} }},\n'
    cs_dict += "        };"
    return cs_dict

def extract_xpath(code, variable_name):
    match = re.search(fr"{variable_name}\s*=\s*detailsPageElements.xpath\((['\"])(.*?)\1\)", code)
    if match:
        return match.group(2)
    return None

def generate_cs_code(class_name, genres_db_cs, python_code):
    studio_match = re.search(r"metadata.studio\s*=\s*'([^']+)'", python_code)
    studio = studio_match.group(1) if studio_match else "Unknown"

    title_xpath = extract_xpath(python_code, "metadata.title") or "//h1"
    summary_xpath = extract_xpath(python_code, "metadata.summary") or "//p"
    date_xpath = extract_xpath(python_code, "date") or "//*[@class='date']"

    # Escape quotes for C# verbatim strings
    title_xpath_cs = title_xpath.replace('"', '""')
    summary_xpath_cs = summary_xpath.replace('"', '""')
    date_xpath_cs = date_xpath.replace('"', '""')

    search_logic = f"""
        // Simplified search logic, may need adjustments
        var result = new List<RemoteSearchResult>();
        var googleResults = await GoogleSearch.PerformSearch(searchTitle, Helper.GetSearchSiteName(siteNum));
        foreach (var sceneURL in googleResults)
        {{
            var http = await new HTTP().Get(sceneURL, cancellationToken);
            if (http.IsOK)
            {{
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var titleNode = doc.DocumentNode.SelectSingleNode(@"{title_xpath_cs}");
                var titleNoFormatting = titleNode?.InnerText.Trim();
                var curID = Helper.Encode(sceneURL);
                var item = new RemoteSearchResult
                {{
                    ProviderIds = {{ {{ Plugin.Instance.Name, $"{{curID}}|{{siteNum[0]}}" }} }},
                    Name = titleNoFormatting,
                    SearchProviderName = Plugin.Instance.Name,
                }};
                result.Add(item);
            }}
        }}
        return result;
    """

    update_logic = f"""
        var result = new MetadataResult<BaseItem>()
        {{
            Item = new Movie(),
            People = new List<PersonInfo>(),
        }};
        var movie = (Movie)result.Item;
        var providerIds = sceneID[0].Split('|');
        var sceneURL = Helper.Decode(providerIds[0]);
        var http = await new HTTP().Get(sceneURL, cancellationToken);
        if (!http.IsOK) return result;

        var doc = new HtmlDocument();
        doc.LoadHtml(http.Content);

        movie.Name = doc.DocumentNode.SelectSingleNode(@"{title_xpath_cs}")?.InnerText.Trim();
        movie.Overview = doc.DocumentNode.SelectSingleNode(@"{summary_xpath_cs}")?.InnerText.Trim();
        movie.AddStudio("{studio}");

        var dateNode = doc.DocumentNode.SelectSingleNode(@"{date_xpath_cs}");
        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
        {{
            movie.PremiereDate = parsedDate;
            movie.ProductionYear = parsedDate.Year;
        }}

        // Actor and Genre logic needs to be manually added for each site

        return result;
    """

    get_images_logic = """
        // Simplified image logic, may need adjustments
        var images = new List<RemoteImageInfo>();
        var providerIds = sceneID[0].Split('|');
        var sceneURL = Helper.Decode(providerIds[0]);
        var http = await new HTTP().Get(sceneURL, cancellationToken);
        if (http.IsOK)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            var imageNodes = doc.DocumentNode.SelectNodes("//img/@src");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    var imgUrl = img.GetAttributeValue("src", string.Empty);
                    if (!imgUrl.StartsWith("http"))
                        imgUrl = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), imgUrl).ToString();
                    images.Add(new RemoteImageInfo { Url = imgUrl });
                }
            }
        }
        return images;

"""


    cs_template = f"""using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{{
    public class {class_name} : IProviderBase
    {{
        {genres_db_cs}

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {{
            {search_logic}
        }}

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {{
            {update_logic}
        }}

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {{
            {get_images_logic}
        }}
    }}
}}
"""
    return cs_template


def main():
    if len(sys.argv) != 3:
        print("Usage: python migrate.py <plex_path> <jellyfin_filename>")
        sys.exit(1)

    plex_path = sys.argv[1]
    jellyfin_filename = sys.argv[2]

    class_name = os.path.splitext(jellyfin_filename)[0]

    print(f"Migrating {plex_path} to {jellyfin_filename}...")

    python_code = extract_python_code(plex_path)

    genres_data = parse_genres_db(python_code)
    genres_db_cs = generate_cs_genres_db(genres_data)

    cs_code = generate_cs_code(class_name, genres_db_cs, python_code)

    output_path = os.path.join('Jellyfin.Plugin.PhoenixAdult', 'Sites', jellyfin_filename)
    with open(output_path, 'w', encoding='utf-8') as out_f:
        out_f.write(cs_code)

    os.remove(plex_path)

    print(f"Finished migrating {plex_path}")

if __name__ == "__main__":
    main()
