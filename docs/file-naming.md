# File naming

The plugin matches an item by its file name, so the name has to carry enough information: a site name and either a
release date, a performer or a scene id. The site may be a full network name or a site abbreviation.

## Naming by title

- `SiteName` - `YYYY-MM-DD` - `Scene Name` `.[ext]`
- `SiteName` - `Scene Name` `.[ext]`
- `SiteName` - `YYYY-MM-DD` - `Actor(s)` `.[ext]`
- `SiteName` - `Actor(s)` `.[ext]`

Examples:

- `Blacked - 2018-12-11 - The Real Thing.mp4`
- `Blacked - Hot Vacation Adventures.mp4`
- `Blacked - 2018-09-07 - Alecia Fox.mp4`

## Naming by scene id

Use these when the site has no search, or when you prefer a deterministic match over a title search:

- `SiteName` - `YYYY-MM-DD` - `SceneID` `.[ext]`
- `SiteName` - `SceneID` `.[ext]`
- `SiteName` - `SceneID` - `Scene Name` `.[ext]`

Examples:

- `EvilAngel - 2016-10-02 - 119883` from `https://www.evilangel.com/en/video/Allie--Lilys-Slobbery-Anal-Threesome/119883`
- `MomsTeachSex - 314082` from `https://momsteachsex.com/tube/watch/314082`
- `Babes - 3075191 - Give In to Desire` from `https://www.babes.com/scene/3075191/1`

For sites without any search endpoint, see [manual search](./manualsearch.md) for `SceneID`, `StudioID`, `ActressID` and
direct scene URLs.
