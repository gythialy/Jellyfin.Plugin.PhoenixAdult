import PAsearchSites
import PAutils


def getSearchFromForm(query, siteNum):
    params = {
        'keywords': query,
        's_filters[type]': 'videos',
        's_filters[site]': 'current'
    }
    searchURL = '%s/search-es' % PAsearchSites.getSearchBaseURL(siteNum)
    req = PAutils.HTTPRequest(searchURL, params=params)
    data = HTML.ElementFromString(req.text)

    return data


def search(results, lang, siteNum, searchData):
    searchResults = []
    searchPageResults = []
    searchID = None

    parts = searchData.title.split()
    if unicode(parts[0], 'UTF-8').isdigit():
        sceneID = parts[0]
        searchData.title = searchData.title.replace(sceneID, '', 1).strip()
    else:
        sceneID = re.sub(r'\D', '', searchData.title)
        actorName = re.sub(r'\s\d.*', '', searchData.title).replace(' ', '-')
        directURL = '%s%s/%s/' % (PAsearchSites.getSearchSearchURL(siteNum), actorName, sceneID)
        searchData.title = searchData.title.replace(sceneID, '', 1).strip()
        searchResults.append(directURL)

    urlID = PAsearchSites.getSearchSearchURL(siteNum).replace(PAsearchSites.getSearchBaseURL(siteNum), '')

    searchPageElements = getSearchFromForm(searchData.title, siteNum)
    for searchResult in searchPageElements.xpath('//div[contains(@class, "compact video")]'):
        titleNoFormatting = PAutils.parseTitle(searchResult.xpath('.//a[contains(@class, "title")]')[0].text_content().strip(), siteNum)
        sceneURL = searchResult.xpath('.//a[contains(@class, "title")]/@href')[0].strip().split('?')[0]
        curID = PAutils.Encode(sceneURL)
        searchPageResults.append(sceneURL)
        match = re.search(r'(?<=\/)\d+(?=\/)', sceneURL)
        if match:
            searchID = match.group(0)
        actors = PAutils.Encode(searchResult.xpath('.//small[@class="i-model"]')[0].text_content())
        img = PAutils.Encode(searchResult.xpath('.//img/@src')[0])

        releaseDate = searchData.dateFormat() if searchData.date else ''

        if searchID and searchID == sceneID:
            score = 100
        else:
            score = 100 - Util.LevenshteinDistance(searchData.title.lower(), titleNoFormatting.lower())

        results.Append(MetadataSearchResult(id='%s|%d|%s|%s|%s|%s' % (curID, siteNum, releaseDate, PAutils.Encode(titleNoFormatting), actors, img), name='%s [%s]' % (titleNoFormatting, PAsearchSites.getSearchSiteName(siteNum)), score=score, lang=lang))

    googleResults = PAutils.getFromGoogleSearch(searchData.title, siteNum)
    for sceneURL in googleResults:
        if urlID in sceneURL and '?' not in sceneURL and sceneURL not in searchResults and sceneURL not in searchPageResults:
            searchResults.append(sceneURL)

    for sceneURL in searchResults:
        req = PAutils.HTTPRequest(sceneURL)
        scenePageElements = HTML.ElementFromString(req.text)
        titleNoFormatting = PAutils.parseTitle(scenePageElements.xpath('//h1')[0].text_content().strip(), siteNum)

        if '404' not in titleNoFormatting and not re.search('Latest.*Videos', titleNoFormatting):
            match = re.search(r'(?<=\/)\d+(?=\/)', sceneURL)
            if match:
                searchID = match.group(0)

            curID = PAutils.Encode(sceneURL)

            date = scenePageElements.xpath('//div[./span[contains(., "Date:")]]//span[@class="value"]')
            if date:
                releaseDate = parse(date[0].text_content().strip()).strftime('%Y-%m-%d')
            else:
                releaseDate = searchData.dateFormat() if searchData.date else ''
            displayDate = releaseDate if date else ''

            if searchID and searchID == sceneID:
                score = 100
            elif searchData.date and displayDate:
                score = 100 - Util.LevenshteinDistance(searchData.date, releaseDate)
            else:
                score = 100 - Util.LevenshteinDistance(searchData.title.lower(), titleNoFormatting.lower())

            results.Append(MetadataSearchResult(id='%s|%d|%s' % (curID, siteNum, releaseDate), name='%s [%s] %s' % (titleNoFormatting, PAsearchSites.getSearchSiteName(siteNum), displayDate), score=score, lang=lang))

    return results


def update(metadata, lang, siteNum, movieGenres, movieActors, art):
    metadata_id = str(metadata.id).split('|')
    sceneURL = PAutils.Decode(metadata_id[0])
    sceneDate = metadata_id[2]

    if not sceneURL.startswith('http'):
        sceneURL = PAsearchSites.getSearchSearchURL(siteNum) + sceneURL
    req = PAutils.HTTPRequest(sceneURL)
    detailsPageElements = HTML.ElementFromString(req.text)

    # Title
    title = detailsPageElements.xpath('//h1')[0].text_content().strip()
    if not title:
        actors = detailsPageElements.xpath('//div/span[@class="value"]/a/text()')
        if len(actors) > 1:
            title = ' and '.join(actors)
        elif actors:
            title = actors[0]

    if re.search('Latest.*Videos', title):
        title = PAutils.Decode(metadata_id[3])
        actors = PAutils.Decode(metadata_id[4]).split(',')
        art.append(PAutils.Decode(metadata_id[5]))
        for actorLink in actors:
            actorName = actorLink.strip()
            actorPhotoURL = ''

            movieActors.addActor(actorName, actorPhotoURL)
    metadata.title = PAutils.parseTitle(title, siteNum).replace('Coming Soon:', '').strip()

    # Summary
    summary_xpaths = [
        '//div[contains(@class, "p-desc")]/text()',
        '//div[contains(@class, "desc")]/text()'
    ]

    for xpath in summary_xpaths:
        summary = detailsPageElements.xpath(xpath)
        if summary:
            metadata.summary = '\n'.join([x for x in summary if x and x != ' ']).strip()
            break

    # Studio
    metadata.studio = 'Score Group'

    # Tagline and Collection(s)
    tagline = PAsearchSites.getSearchSiteName(siteNum)
    metadata.tagline = tagline
    metadata.collections.add(metadata.tagline)

    # Release Date
    try:
        date = detailsPageElements.xpath('//div/span[@class="value"]')[1].text_content().strip()
    except:
        date = None
    if date:
        date_object = parse(date)
        metadata.originally_available_at = date_object
        metadata.year = metadata.originally_available_at.year
    elif sceneDate:
        date_object = parse(sceneDate)
        metadata.originally_available_at = date_object
        metadata.year = metadata.originally_available_at.year

    # Genres
    genre_xpaths = [
        '//div[@class="mb-3"]/a',
        '//div[contains(@class, "desc")]//a[contains(@href, "tag") or contains(@href, "category")]'
    ]

    for xpath in genre_xpaths:
        for genreLink in detailsPageElements.xpath(xpath):
            genreName = genreLink.text_content().strip()

            movieGenres.addGenre(genreName)

    # Actor(s)
    for actorLink in detailsPageElements.xpath('//div/span[@class="value"]/a'):
        actorName = actorLink.text_content().strip()
        actorPhotoURL = ''
        gender = ''

        try:
            modelURL = actorLink.xpath('.//@href')[0].split('?')[0]
            gender = 'male' if '/male-' in modelURL else ''
            req = PAutils.HTTPRequest(modelURL)
            modelPageElements = HTML.ElementFromString(req.text)

            actorPhotoURL = modelPageElements.xpath('//div[@class="item-img pos-rel"]//img/@src')[0]
        except:
            pass

        if actorName.lower() != 'extra':
            movieActors.addActor(actorName, actorPhotoURL, gender=gender)

    if siteNum == 1344:
        movieActors.addActor('Christy Marks', '')

    # Posters/Background
    match = re.search(r'posterImage: \'(.*)\'', req.text)
    if match:
        art.append(match.group(1))

    xpaths = [
        '//script[@type]/text()',
        '//div[contains(@class, "thumb")]/img/@src',
        '//div[contains(@class, "p-image")]/a/img/@src',
        '//div[contains(@class, "dl-opts")]/a/img/@src',
        '//div[contains(@class, "p-photos")]/div/div/a/@href',
        '//div[contains(@class, "gallery")]/div/div/a/@href'
    ]

    for xpath in xpaths:
        for poster in detailsPageElements.xpath(xpath):
            match = re.search(r'(?<=(poster: \')).*(?=\')', poster)
            if match:
                poster = match.group(0)

            if not poster.startswith('http'):
                poster = 'http:' + poster

            if 'PosterThumbs' in poster:
                match = re.search(r'(?<=PosterThumbs)\/\d\d', poster)
                if match:
                    for idx in range(1, 7):
                        art.append(poster.replace(match.group(0), '/{0:02d}'.format(idx)))
            elif 'shared-bits' not in poster and '/join' not in poster:
                art.append(poster)

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
