import PAsearchSites
import PAutils


def search(results, lang, siteNum, searchData):
    sceneId = None
    parts = searchData.title.split()
    if unicode(parts[0], 'UTF-8').isdigit():
        sceneId = parts[0]
        searchData.title = searchData.title.replace(sceneId, '', 1).strip()

    searchURL = '%s/api/search/%s' % (PAsearchSites.getSearchBaseURL(siteNum), searchData.encoded.lower())
    req = PAutils.HTTPRequest(searchURL)
    searchResults = req.json()
    data = searchResults['playlists'] if siteNum == 1677 else searchResults['scenes']

    if searchResults:
        for searchResult in data:
            for type in ['scene', 'bts']:
                if type == 'bts' and siteNum == 1835:
                    titleNoFormatting = 'BTS: %s' % PAutils.parseTitle(searchResult['title'], siteNum)

                    releaseDate = parse(searchResult['publish_date']).strftime('%Y-%m-%d')
                    sceneURL = '%s/bts/%s-bts' % (PAsearchSites.getSearchBaseURL(siteNum), searchResult['slug'])

                    curID = PAutils.Encode(sceneURL)

                    if searchData.date:
                        score = 80 - Util.LevenshteinDistance(searchData.date, releaseDate)
                    else:
                        score = 80 - Util.LevenshteinDistance(searchData.title.lower(), titleNoFormatting.lower())

                    results.Append(MetadataSearchResult(id='%s|%d' % (curID, siteNum), name='%s [%s] %s' % (titleNoFormatting, PAsearchSites.getSearchSiteName(siteNum), releaseDate), score=score, lang=lang))
                elif type == 'scene':
                    titleNoFormatting = PAutils.parseTitle(searchResult['title'], siteNum)
                    videoId = searchResult['id']

                    if siteNum == 1677:
                        releaseDate = parse(searchResult['created_at']).strftime('%Y-%m-%d')
                        sceneURL = '%s/%s/%s' % (PAsearchSites.getSearchSearchURL(siteNum), videoId, searchResult['slug'])
                    else:
                        releaseDate = parse(searchResult['publish_date']).strftime('%Y-%m-%d')
                        sceneURL = '%s/%s' % (PAsearchSites.getSearchSearchURL(siteNum), searchResult['slug'])

                    curID = PAutils.Encode(sceneURL)

                    if sceneId and int(sceneId) == int(videoId):
                        score == 100
                    elif searchData.date:
                        score = 80 - Util.LevenshteinDistance(searchData.date, releaseDate)
                    else:
                        score = 80 - Util.LevenshteinDistance(searchData.title.lower(), titleNoFormatting.lower())

                    results.Append(MetadataSearchResult(id='%s|%d' % (curID, siteNum), name='%s [%s] %s' % (titleNoFormatting, PAsearchSites.getSearchSiteName(siteNum), releaseDate), score=score, lang=lang))

    return results


def update(metadata, lang, siteNum, movieGenres, movieActors, art):
    metadata_id = str(metadata.id).split('|')
    sceneURL = PAutils.Decode(metadata_id[0])

    req = PAutils.HTTPRequest(sceneURL)
    detailsPageElements = HTML.ElementFromString(req.text)
    videoPageElements = json.loads(detailsPageElements.xpath('//script[@type="application/json"]')[0].text_content())

    if siteNum == 1677:
        video = videoPageElements['props']['pageProps']['playlist']
        content = videoPageElements['props']['pageProps']['content']
    else:
        video = videoPageElements['props']['pageProps']['content']
        content = video

    # Title
    metadata.title = PAutils.parseTitle(video['title'], siteNum)

    # Summary
    summary = video['description']
    if not re.search(r'.$(?<=(!|\.|\?))', summary):
        summary = summary + '.'
    metadata.summary = summary

    # Studio
    if (1229 <= siteNum <= 1236):
        metadata.studio = 'Top Web Models'
    elif (837 <= siteNum <= 839):
        metadata.studio = 'TwoWebMedia'
    else:
        metadata.studio = 'Radical Cash'

    # Tagline and Collection(s)
    tagline = content['site'] if siteNum == 1677 else video['site']
    if metadata.studio != tagline:
        metadata.tagline = tagline
    metadata.collections.add(tagline)

    # Release Date
    date_object = parse(video['publish_date'])
    metadata.originally_available_at = date_object
    metadata.year = metadata.originally_available_at.year

    # Genres
    for genreLink in content['tags']:
        genreName = genreLink.strip()

        movieGenres.addGenre(genreName)

    # Actor(s)
    for actor in video['models_thumbs']:
        actorName = actor['name']
        actorPhotoURL = actor['thumb']

        movieActors.addActor(actorName, actorPhotoURL)

    # Posters
    if content['trailer_screencap']:
        art.append(content['trailer_screencap'])

    if 'previews' in content:
        for image in content['previews']['full']:
            art.append(image)

    for imageType in ['extra_thumbnails']:
        if imageType in content:
            for image in list(content[imageType]):
                art.append(image)

    if not art:
        if 'thumbs' in content:
            for image in content['thumbs']:
                art.append(image)

    images = []
    posterExists = False
    Log('Artwork found: %d' % len(art))
    for idx, posterUrl in enumerate(art, 1):
        if not PAsearchSites.posterAlreadyExists(posterUrl, metadata):
            # Download image file for analysis
            try:
                image = PAutils.HTTPRequest(posterUrl)
                im = StringIO(image.content)
                resized_image = Image.open(im)
                width, height = resized_image.size
                # Add the image proxy items to the collection
                if height > width:
                    # Item is a poster
                    metadata.posters[posterUrl] = Proxy.Media(image.content, sort_order=idx)
                    posterExists = True
                if width > height:
                    # Item is an art item
                    images.append((image, posterUrl))
                    metadata.art[posterUrl] = Proxy.Media(image.content, sort_order=idx)
            except:
                pass
        elif PAsearchSites.posterOnlyAlreadyExists(posterUrl, metadata):
            posterExists = True

    if not posterExists:
        for idx, (image, posterUrl) in enumerate(images, 1):
            try:
                im = StringIO(image.content)
                resized_image = Image.open(im)
                width, height = resized_image.size
                # Add the image proxy items to the collection
                if width > 1:
                    # Item is a poster
                    metadata.posters[posterUrl] = Proxy.Media(image.content, sort_order=idx)
            except:
                pass

    return metadata
