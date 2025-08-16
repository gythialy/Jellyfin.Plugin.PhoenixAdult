import PAsearchSites
import PAutils


def xPathResultBuilder(titleXPath, dateXPath, dateFormat, redirectURL, sceneURL, lang, siteNum, searchData, searchResult, **kwargs):
    dateSplit = kwargs.pop('dateSplit', ':')
    searchResults = kwargs.pop('searchResults', [])

    result = None
    if redirectURL not in searchResults:
        titleNoFormatting = searchResult.xpath(titleXPath)[0].text_content().strip()
        curID = PAutils.Encode(sceneURL)

        date = searchResult.xpath(dateXPath)
        if date:
            cleanDate = re.sub(r'(\d)(st|nd|rd|th)', r'\1', date[0].text_content().split(dateSplit)[-1].strip())
            releaseDate = datetime.strptime(cleanDate, dateFormat).strftime('%Y-%m-%d')
        else:
            releaseDate = searchData.dateFormat() if searchData.date else ''

        displayDate = releaseDate if date else ''

        if searchData.date and displayDate:
            score = 100 - Util.LevenshteinDistance(searchData.date, releaseDate)
        else:
            score = 100 - Util.LevenshteinDistance(searchData.title.lower(), titleNoFormatting.lower())

        result = MetadataSearchResult(id='%s|%d' % (curID, siteNum), name='%s [%s] %s' % (displayDate, PAsearchSites.getSearchSiteName(siteNum), PAutils.parseTitle(titleNoFormatting, siteNum)), score=score, lang=lang)

    return result


def search(results, lang, siteNum, searchData):
    directSearchResults = []
    searchResults = []
    siteXPath = PAutils.getDictValuesFromKey(xPathMap, siteNum)

    searchURL = PAsearchSites.getSearchSearchURL(siteNum) + searchData.title.lower()
    if (1851 <= siteNum <= 1859):
        if lang in supported_lang:
            searchURL = '%s?_lang=%s' % (searchURL, lang)
        else:
            searchURL = '%s?_lang=%s' % (searchURL, 'en')

    req = PAutils.HTTPRequest(searchURL)
    searchPageElements = HTML.ElementFromString(req.text)
    for searchResult in searchPageElements.xpath(siteXPath['searchResults']):
        sceneURL = searchResult.xpath(siteXPath['searchURL'])[0].split('?')[0]
        directSearchResults.append(sceneURL)
        result = xPathResultBuilder(siteXPath['searchTitle'], siteXPath['searchDate'], siteXPath['searchDateFormat'], sceneURL, sceneURL, lang, siteNum, searchData, searchResult)
        if result:
            results.Append(result)

    googleResults = PAutils.getFromGoogleSearch(searchData.title, siteNum)
    for sceneURL in googleResults:
        sceneURL = sceneURL.split('?')[0].replace('dev.', '', 1)

        if ('/view/' in sceneURL or '/model/' in sceneURL) and 'photoset' not in sceneURL and sceneURL not in searchResults and sceneURL not in directSearchResults:
            searchResults.append(sceneURL)

    for sceneURL in searchResults:
        result = None
        if (1851 <= siteNum <= 1859):
            if lang in supported_lang:
                sceneURL = '%s?_lang=%s' % (sceneURL, lang)
            else:
                sceneURL = '%s?_lang=%s' % (sceneURL, 'en')

        if '/model/' in sceneURL:
            req = PAutils.HTTPRequest(sceneURL)
            actorPageElements = HTML.ElementFromString(req.text)

            for searchResult in actorPageElements.xpath(siteXPath['actorSearchResults']):
                sceneURL = searchResult.xpath(siteXPath['searchURL'])[0].split('?')[0].replace('dev.', '', 1)

                if '/join' not in sceneURL:
                    result = xPathResultBuilder(siteXPath['searchTitle'], siteXPath['searchDate'], siteXPath['searchDateFormat'], sceneURL, sceneURL, lang, siteNum, searchData, searchResult, searchResults=directSearchResults)
                if result:
                    results.Append(result)
        else:
            req = PAutils.HTTPRequest(sceneURL)
            detailsPageElements = HTML.ElementFromString(req.text)

            result = xPathResultBuilder(siteXPath['title'], siteXPath['date'], siteXPath['dateFormat'], req.url, sceneURL.split('?')[0], lang, siteNum, searchData, detailsPageElements, searchResults=directSearchResults)
            if result:
                results.Append(result)

    return results


def update(metadata, lang, siteNum, movieGenres, movieActors, art):
    metadata_id = str(metadata.id).split('|')
    sceneURL = PAutils.Decode(metadata_id[0])
    if (1851 <= siteNum <= 1859):
        if lang in supported_lang:
            sceneURL = '%s?_lang=%s' % (sceneURL, lang)
        else:
            sceneURL = '%s?_lang=%s' % (sceneURL, 'en')
    req = PAutils.HTTPRequest(sceneURL)
    detailsPageElements = HTML.ElementFromString(req.text)
    siteXPath = PAutils.getDictValuesFromKey(xPathMap, siteNum)

    # Title
    metadata.title = PAutils.parseTitle(detailsPageElements.xpath(siteXPath['title'])[0].text_content().strip(), siteNum)

    # Summary
    description = ''
    for desc in detailsPageElements.xpath(siteXPath['summary']):
        description += desc.text_content().strip() + '\n\n'
    metadata.summary = description

    # Studio
    if (1852 <= siteNum <= 1859):
        metadata.studio = 'Hitzefrei'
    if (1861 <= siteNum <= 1862):
        metadata.studio = 'Gonzo Living'
    else:
        metadata.studio = 'Radical Cash'

    # Tagline and Collection(s)
    if siteNum == 1066:
        tagline = '%s: %s' % (PAsearchSites.getSearchSiteName(siteNum), detailsPageElements.xpath('//p[@class="series"]')[0].text_content().strip())
    else:
        tagline = PAsearchSites.getSearchSiteName(siteNum)
    metadata.tagline = tagline
    metadata.collections.add(tagline)

    # Release Date
    date = detailsPageElements.xpath(siteXPath['date'])
    if date:
        cleanDate = re.sub(r'(\d)(st|nd|rd|th)', r'\1', date[0].text_content().strip())
        date_object = datetime.strptime(cleanDate, siteXPath['dateFormat'])
        metadata.originally_available_at = date_object
        metadata.year = metadata.originally_available_at.year

    # Genres
    genres = detailsPageElements.xpath(siteXPath['genres'])
    if genres:
        for genreLink in genres[0].split(','):
            genreName = genreLink.strip()

            movieGenres.addGenre(genreName)

    # Actor(s)
    actors = detailsPageElements.xpath(siteXPath['actors'])
    if actors:
        if len(actors) == 3:
            movieGenres.addGenre('Threesome')
        if len(actors) == 4:
            movieGenres.addGenre('Foursome')
        if len(actors) > 4:
            movieGenres.addGenre('Orgy')

        for actorLink in actors:
            actorName = actorLink.xpath(siteXPath['actor'])[0]
            actorPhotoURL = actorLink.xpath(siteXPath['actorPhoto'])[0]

            if 1860 <= siteNum <= 1862:
                req = PAutils.HTTPRequest(actorPhotoURL)
                modelPageElements = HTML.ElementFromString(req.text)

                actorPhotoURL = modelPageElements.xpath('//div[@class="model-photo"]//@src')[0]

            movieActors.addActor(actorName, actorPhotoURL)

    # Posters
    xpaths = [
        '//div[@class="photo-wrap"]//@href',
        '//div[@id="photo-carousel"]//@href',
        '//video/@poster',
    ]

    for xpath in xpaths:
        for img in sorted(detailsPageElements.xpath(xpath)):
            if 'http' not in img:
                img = PAsearchSites.getSearchBaseURL(siteNum) + img

            if img not in art:
                art.append(img)

    images = []
    posterExists = False
    Log('Artwork found: %d' % len(art))
    for idx, posterUrl in enumerate(art, 1):
        if not PAsearchSites.posterAlreadyExists(posterUrl, metadata):
            # Download image file for analysis
            try:
                image = PAutils.HTTPRequest(posterUrl)
                im = StringIO(image.content)
                images.append(image)
                resized_image = Image.open(im)
                width, height = resized_image.size
                # Add the image proxy items to the collection
                if height > width:
                    # Item is a poster
                    metadata.posters[posterUrl] = Proxy.Media(image.content, sort_order=idx)
                    posterExists = True
                if width > height:
                    # Item is an art item
                    metadata.art[posterUrl] = Proxy.Media(image.content, sort_order=idx)
            except:
                pass

    if not posterExists:
        for idx, image in enumerate(images, 1):
            try:
                im = StringIO(image.content)
                resized_image = Image.open(im)
                width, height = resized_image.size
                # Add the image proxy items to the collection
                if width > 1:
                    # Item is a poster
                    metadata.posters[art[idx - 1]] = Proxy.Media(image.content, sort_order=idx)
            except:
                pass

    return metadata


supported_lang = ['en', 'de']


xPathMap = {
    1066: {
        'searchResults': '//div[contains(@class,"content-item")]',
        'actorSearchResults': '//div[contains(@class, "content-item")]',
        'searchTitle': './/h3',
        'searchURL': './/h3//@href',
        'searchDate': './/span[@class="pub-date"]',
        'title': '//h1',
        'date': '//span[@class="date"]',
        'summary': '//div[@class="description"]//p',
        'genres': '//meta[@name="keywords"]/@content',
        'actors': '//div[@class="model-wrap"]//li',
        'actor': './/h5/text()',
        'actorPhoto': './/img/@src',
        'searchDateFormat': '%b %d, %Y',
        'dateFormat': '%A %B %d, %Y',
    },
    (1851, 1852, 1853, 1854, 1855, 1856, 1857, 1858, 1859): {
        'searchResults': '//div[@class="content-metadata"]',
        'actorSearchResults': '//div[contains(@class, "video-description")]',
        'searchTitle': './/h1',
        'searchURL': './/h1//@href',
        'searchDate': './/p[@class="content-date"]/strong[1]',
        'title': '//h1',
        'date': '//span[@class="meta-value"][2]',
        'summary': '//div[@class="content-description"]//p',
        'genres': '//meta[@name="keywords"]/@content',
        'actors': '//div[./div[@class="model-name"]]',
        'actor': './div[@class="model-name"]/text()',
        'actorPhoto': './/img/@src',
        'searchDateFormat': '%d/%m/%Y',
        'dateFormat': '%d/%m/%Y',
    },
    1860: {
        'searchResults': '//div[@class="col-sm-3"]',
        'actorSearchResults': '//div[contains(@class, "content-item")]',
        'searchTitle': './/h5',
        'searchURL': './/a//@href',
        'searchDate': './/div[@class="pull-right"][./i[contains(@class,"calendar")]]',
        'title': '//h2',
        'date': '//span[@class="post-date"]',
        'summary': '//div[@class="desc"]//p',
        'genres': '//meta[@name="keywords"]/@content',
        'actors': '//div[@class="content-meta"]//h4[@class="models"]//a',
        'actor': './text()',
        'actorPhoto': './/@href',
        'searchDateFormat': '%d %b %Y',
        'dateFormat': '%d %b %Y',
    },
    (1861, 1862): {
        'searchResults': '//div[contains(@class, "content-item-medium")]',
        'actorSearchResults': '//div[contains(@class, "content-item-large")]',
        'searchTitle': './/h3',
        'searchURL': './/a//@href',
        'searchDate': './/div[@class="date"]',
        'title': '//h2',
        'date': '//span[@class="post-date"]',
        'summary': '//div[@class="desc"]//p',
        'genres': '//meta[@name="keywords"]/@content',
        'actors': '//div[@class="content-meta"]//h4[@class="models"]//a',
        'actor': './text()',
        'actorPhoto': './/@href',
        'searchDateFormat': '%d %b %Y',
        'dateFormat': '%d %b %Y',
    },
}
