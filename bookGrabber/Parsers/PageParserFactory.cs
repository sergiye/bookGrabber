using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace bookGrabber {

  public static class PageParserFactory {

    private static readonly Dictionary<string, Type> parsers = new() {
      {KnigavuheParser.BaseUrl, typeof(KnigavuheParser)},
      {AudioBookMp3Parser.BaseUrl, typeof(AudioBookMp3Parser)},
      {BooksAudioOnlineParser.BaseUrl, typeof(BooksAudioOnlineParser)},
    };

    public static async Task<PageParser> Create(string url) {

      if (string.IsNullOrWhiteSpace(url))
        throw new ArgumentException("Url can not be empty.");
      url = url.Trim();
      if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        throw new Exception($"Invalid url: {url}");

      var entry = parsers.FirstOrDefault(p => IsSameSite(uri, new Uri(p.Key)));
      if (entry.Value == null)
        throw new Exception($"No parser found for url: {url}");

      var parser = (PageParser) Activator.CreateInstance(entry.Value);
      parser.Referer = entry.Key;
      await parser.Init(url);
      return parser;
    }

    private static bool IsSameSite(Uri uri, Uri baseUri) {
      return string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
             || string.Equals(uri.Host, "www." + baseUri.Host, StringComparison.OrdinalIgnoreCase);
    }
  }
}
