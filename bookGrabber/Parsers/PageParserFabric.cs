using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace bookGrabber {

  public static class PageParserFabric {

    private static readonly Dictionary<string, Type> parsers = new() {
      {KnigavuheParser.BaseUrl, typeof(KnigavuheParser)},
      {AudioBookMp3Parser.BaseUrl, typeof(AudioBookMp3Parser)},
      {BooksAudioOnlineParser.BaseUrl, typeof(BooksAudioOnlineParser)},
    };

    public static async Task<PageParser> Create(string url) {

      var parserType = parsers.FirstOrDefault(p => url.StartsWith(p.Key)).Value;
      if (parserType == null)
        throw new Exception($"No parser found for url: {url}");

      var parser = (PageParser) Activator.CreateInstance(parserType);
      await parser.Init(url);
      return parser;
    }
  }
}
