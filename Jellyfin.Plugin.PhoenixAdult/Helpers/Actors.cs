using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
#endif

namespace PhoenixAdult.Helpers
{
    internal static class Actors
    {
        public static List<PersonInfo> Cleanup(MetadataResult<Movie> scene)
        {
            var newPeoples = new List<PersonInfo>();

            if (scene == null || Plugin.Instance.Configuration.DisableActors)
            {
                return newPeoples;
            }

            foreach (var people in scene.People)
            {
                var newPeople = new PersonInfo
                {
                    Name = people.Name,
                    Type = people.Type,
                    ImageUrl = people.ImageUrl,
                };

                newPeople.Name = WebUtility.HtmlDecode(newPeople.Name);
                newPeople.Name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(newPeople.Name);
                newPeople.Name = newPeople.Name.Split('(').First();
                newPeople.Name = newPeople.Name.Replace("™", string.Empty, StringComparison.OrdinalIgnoreCase);
                newPeople.Name = newPeople.Name.Trim();

#if __EMBY__
#else
                if (newPeople.Type == PersonKind.Unknown)
                {
                    newPeople.Type = PersonKind.Actor;
                }
#endif
                if (!newPeoples.Any(o => o.Name == newPeople.Name))
                {
                    newPeoples.Add(newPeople);
                }
            }

            switch (Plugin.Instance.Configuration.PreferedActorNameSource)
            {
                case PreferedActorNameSource.LocalDatabase:
                    newPeoples = CleanupFromDatabase(newPeoples, scene.Item);
                    break;
            }

            return newPeoples;
        }

        public static List<PersonInfo> Cleanup(List<PersonInfo> peoples, BaseItem item)
        {
            return Cleanup(new MetadataResult<Movie>
            {
                People = peoples,
                Item = new Movie
                {
                    Studios = item.Studios,
                },
            });
        }

        /// <summary>
        /// Resolve an actress name to its canonical form,
        /// shared by movie cleaning, actor search and portraits.
        /// </summary>
        public static string Resolve(string actorName, string[] studios = null)
        {
            if (string.IsNullOrWhiteSpace(actorName))
            {
                return actorName;
            }

            return ReplaceFromDatabase(actorName, studios);
        }

        /// <summary>
        /// Parse a name; when direct parsing fails, swap the word order and retry.
        /// </summary>
        public static string ResolveWithJapaneseStyle(string actorName, string[] studios = null)
        {
            var resolved = Resolve(actorName, studios);
            if (!string.Equals(resolved, actorName, StringComparison.Ordinal))
            {
                return resolved;
            }

            var splitedName = actorName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (splitedName.Length < 2)
            {
                return resolved;
            }

            var reversedName = string.Join(" ", splitedName.Reverse());
            var reversedResolved = Resolve(reversedName, studios);
            if (string.Equals(reversedResolved, reversedName, StringComparison.Ordinal))
            {
                return resolved;
            }

            return string.Join(" ", reversedResolved.Split(' ').Reverse());
        }

        public static List<PersonInfo> CleanupFromDatabase(List<PersonInfo> peoples, BaseItem item)
        {
            var newPeoples = new List<PersonInfo>();

            foreach (var people in peoples)
            {
                var newPeople = new PersonInfo
                {
                    Name = people.Name,
                    Type = people.Type,
                    ImageUrl = people.ImageUrl,
                };

                var newName = ReplaceFromDatabase(newPeople.Name, item.Studios);
                if (newName == newPeople.Name)
                {
                    switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
                    {
                        case JAVActorNamingStyle.JapaneseStyle:
                            string japaneseName = string.Join(" ", newName.Split().Reverse()),
                                newJapaneseName = ReplaceFromDatabase(japaneseName, item.Studios);

                            newJapaneseName = string.Join(" ", newJapaneseName.Split().Reverse());

                            if (newJapaneseName != japaneseName)
                            {
                                newName = newJapaneseName;
                            }

                            break;
                    }
                }

                newPeople.Name = newName;

                if (!newPeoples.Any(o => o.Name == newPeople.Name))
                {
                    newPeoples.Add(newPeople);
                }
            }

            return newPeoples;
        }

        private static string ReplaceFromDatabase(string actorName, string[] studios)
        {
            if (string.IsNullOrWhiteSpace(actorName))
            {
                return actorName;
            }

            var siteIndex = -1;
            foreach (var studio in studios ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(studio))
                {
                    continue;
                }

                var studioName = studio.Split('!').First().Trim();

                if (Database.Actors.ActorsStudioIndexes == null)
                {
                    break;
                }

                foreach (var studioIndex in Database.Actors.ActorsStudioIndexes)
                {
                    if (studioIndex.Value != null && studioIndex.Value.Contains(studioName))
                    {
                        siteIndex = studioIndex.Key;
                        break;
                    }
                }

                if (siteIndex > -1)
                {
                    break;
                }
            }

            var newActorName = string.Empty;
            if (siteIndex > -1
                && Database.Actors.ActorsReplaceStudios != null
                && Database.Actors.ActorsReplaceStudios.TryGetValue(siteIndex, out var studioReplace)
                && studioReplace != null)
            {
                newActorName = studioReplace.FirstOrDefault(o => o.Value != null && o.Value.Contains(actorName, StringComparer.OrdinalIgnoreCase)).Key;
                if (!string.IsNullOrEmpty(newActorName))
                {
                    actorName = newActorName;
                }
            }

            if (Database.Actors.ActorsReplace == null)
            {
                return actorName;
            }

            newActorName = Database.Actors.ActorsReplace.FirstOrDefault(o => o.Key.Equals(actorName, StringComparison.OrdinalIgnoreCase) || (o.Value != null && o.Value.Contains(actorName, StringComparer.OrdinalIgnoreCase))).Key;
            if (!string.IsNullOrEmpty(newActorName))
            {
                actorName = newActorName;
            }

            return actorName;
        }
    }
}
