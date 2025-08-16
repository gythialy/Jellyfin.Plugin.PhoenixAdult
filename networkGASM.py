import PAsearchSites
import PAutils


def search(results, lang, siteNum, searchData):
    cookies = {'WarningModal': 'true'}
    sceneID = None
    parts = searchData.title.split()
    if unicode(parts[0], 'UTF-8').isdigit():
        sceneID = parts[0]
        searchData.title = searchData.title.replace(sceneID, '', 1).strip()

    if sceneID:
        sceneURL = '%s/post/details/%s' % (PAsearchSites.getSearchBaseURL(siteNum), sceneID)
        req = PAutils.HTTPRequest(sceneURL, cookies=cookies)
        searchResult = HTML.ElementFromString(req.text)

        titleNoFormatting = searchResult.xpath('//h1[@class="post_title"]/span')[0].text_content()
        curID = PAutils.Encode(sceneURL)
        subSite = PAutils.parseTitle(searchResult.xpath('//a[contains(@href, "/studio/profile/")]')[0].text_content().split(':')[-1].strip(), siteNum)

        date = searchResult.xpath('//h3[@class="post_date"]')
        if date:
            releaseDate = datetime.strptime(date[0].text_content().strip(), '%b %d, %Y').strftime('%Y-%m-%d')
        else:
            releaseDate = searchData.dateFormat() if searchData.date else ''
        displayDate = releaseDate if date else ''

        score = 100

        results.Append(MetadataSearchResult(id='%s|%d|%s' % (curID, siteNum, releaseDate), name='%s [%s] %s' % (PAutils.parseTitle(titleNoFormatting, siteNum), subSite, displayDate), score=score, lang=lang))
    else:
        searchData.encoded = slugify(searchData.title, lowercase=True, separator='+')
        searchURL = PAsearchSites.getSearchSearchURL(siteNum) + searchData.encoded
        if 1867 <= siteNum <= 1882:
            searchURL = '%s&channel=%s' % (searchURL, PAutils.getDictKeyFromValues(channelIdDB, PAsearchSites.getSearchSiteName(siteNum).lower())[0])
        req = PAutils.HTTPRequest(searchURL, cookies=cookies)
        searchResults = HTML.ElementFromString(req.text)

        for searchResult in searchResults.xpath('//div[contains(@class, "results_item")]'):
            titleNoFormatting = searchResult.xpath('.//a[@class="post_title"]')[0].text_content()
            curID = PAutils.Encode(searchResult.xpath('.//a[@class="post_title"]/@href')[0])
            subSite = PAutils.parseTitle(searchResult.xpath('.//a[@class="post_channel"]')[0].text_content().split(':')[-1].strip(), siteNum)

            releaseDate = searchData.dateFormat() if searchData.date else ''

            score = 100 - Util.LevenshteinDistance(searchData.title.lower(), titleNoFormatting.lower())

            results.Append(MetadataSearchResult(id='%s|%d|%s' % (curID, siteNum, releaseDate), name='%s [%s]' % (PAutils.parseTitle(titleNoFormatting, siteNum), subSite), score=score, lang=lang))

    return results


def update(metadata, lang, siteNum, movieGenres, movieActors, art):
    cookies = {'WarningModal': 'true'}
    metadata_id = str(metadata.id).split('|')
    sceneURL = PAutils.Decode(metadata_id[0])
    if not sceneURL.startswith('http'):
        sceneURL = PAsearchSites.getSearchBaseURL(siteNum) + sceneURL
    sceneDate = metadata_id[2]
    req = PAutils.HTTPRequest(sceneURL, cookies=cookies)
    detailsPageElements = HTML.ElementFromString(req.text)

    # Title
    metadata.title = PAutils.parseTitle(detailsPageElements.xpath('//h1[@class="post_title"]/span')[0].text_content().strip(), siteNum)

    # Summary
    metadata.summary = detailsPageElements.xpath('//h2[@class="post_description"]')[0].text_content().replace('´', '\'').replace('’', '\'')

    # Studio
    metadata.studio = 'GASM'

    # Tagline and Collection(s)
    tagline = PAutils.parseTitle(detailsPageElements.xpath('//a[contains(@href, "/studio/profile/")]')[0].text_content().strip(), siteNum)
    metadata.tagline = tagline
    metadata.collections.add(tagline)

    dvd = detailsPageElements.xpath('//div[@class="post_item dvd"]/h1')
    if dvd:
        metadata.collections.add(PAutils.parseTitle(dvd[0].text_content().lower(), siteNum))

    # Release Date
    date = detailsPageElements.xpath('//h3[@class="post_date"]')
    if date:
        date_object = datetime.strptime(date[0].text_content().strip(), '%b %d, %Y')
        metadata.originally_available_at = date_object
        metadata.year = metadata.originally_available_at.year
    elif sceneDate:
        date_object = parse(sceneDate)
        metadata.originally_available_at = date_object
        metadata.year = metadata.originally_available_at.year

    # Genres
    for genreLink in detailsPageElements.xpath('//a[contains(@href, "/search?s=")]'):
        genreName = genreLink.text_content().strip()

        movieGenres.addGenre(genreName)

    # Actor(s)
    for actorLink in detailsPageElements.xpath('//a[contains(@href, "models/")]'):
        actorName = actorLink.text_content().strip()
        actorPhotoURL = ''

        movieActors.addActor(actorName, actorPhotoURL)

    # Posters/Background
    xpaths = [
        '//img[@class="item_cover"]/@src',
        '//meta[@name="twitter:image"]/@content'
    ]

    for xpath in xpaths:
        for poster in detailsPageElements.xpath(xpath):
            art.append(poster)

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
                if width > 1:
                    # Item is a poster
                    metadata.posters[posterUrl] = Proxy.Media(image.content, sort_order=idx)
                if width > height:
                    # Item is an art item
                    metadata.art[posterUrl] = Proxy.Media(image.content, sort_order=idx)
            except:
                pass

    return metadata


channelIdDB = {
    '23': ['harmony vision'],
    '102': ['butt formation'],
    '105': ['pure xxx films'],
    '8110': ['cosplay babes'],
    '8111': ['filthy and fisting'],
    '8112': ['fun movies'],
    '8113': ['herzog'],
    '8114': ['hot gold'],
    '8115': ['inflagranti'],
    '8116': ['japanhd'],
    '8117': ['Leche69'],
    '8118': ['magma film'],
    '8119': ['mmv films'],
    '8120': ['paradise films'],
    '8121': ['pornxn'],
    '8122': ['the undercover lover'],
}
