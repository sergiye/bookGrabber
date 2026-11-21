using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using sergiye.Common;

namespace bookGrabber {

  public class KnigavuheParser : PageParser {

    public const string BaseUrl = "https://knigavuhe.org";

    public override async Task Init(string url) {

      if (string.IsNullOrWhiteSpace(url))
        throw new ArgumentException("Url can not be empty.");

      var content = (await Utils.GetContent(url)).TrimEnd();

      ResetInfo();

      var sequenceNameMatch = Regex.Match(content, @"<div class=""book_serie_block_title"">\s+.+>([^>]+)<\/a>");
      if (sequenceNameMatch.Success)
        SequenceName = WebUtility.HtmlDecode(sequenceNameMatch.Groups[1].Value);

      var sequences = Regex.Matches(content, @"<div class=""book_serie_block_item"">\s*(<span.+)?(\s*<a href=""([^""]+)"">)?");
      if (sequences.Count > 0) {
        for (var i = 0; i < sequences.Count; i++) {
          if (sequences[i].Groups[3].Success) continue;
          var sequenceNumberMatch = Regex.Match(sequences[i].Groups[1].Value, @"<span class=""book_serie_block_item_index"">(\d+\.?\d*)\.<\/span>?");
          if (sequenceNumberMatch.Groups[1].Success) {
            SequenceNumber = sequenceNumberMatch.Groups[1].Value;
          }
          if (sequences.Count > i + 1) {
            NextBookUrl = ResolveUrl(url, sequences[i + 1].Groups[3].Value) ?? string.Empty;
          }
          break;
        }
      }

      var ogTitle = GetMetaContent(content, "og:title");
      if (!string.IsNullOrWhiteSpace(ogTitle)) {
        //format is "author - title"
        if (SplitTitle(ogTitle, false, out var author, out var title))
          SetTitles(author, title);
        else
          SetTitles(string.Empty, ogTitle);
      }

      BookImgUrl = ResolveUrl(url, GetMetaContent(content, "og:image"));

      var playerMatch = Regex.Match(content, @"new BookPlayer\(\d+,\s*\[");
      if (!playerMatch.Success)
        throw new Exception("No tracks found");

      var arrayStart = playerMatch.Index + playerMatch.Length - 1;
      var arrayEnd = FindArrayEnd(content, arrayStart);
      if (arrayEnd < 0)
        throw new Exception("No tracks found");

      var jsonData = Utils.NormalizeJsLiteral(content.Substring(arrayStart, arrayEnd - arrayStart + 1));
      Tracks = jsonData.FromJson<TrackInfo[]>();
      if (Tracks == null || Tracks.Length == 0)
        throw new Exception("Error getting list of tracks");
    }

    //track titles may contain brackets, so skip string literals while matching
    private static int FindArrayEnd(string content, int start) {
      var depth = 0;
      var inString = false;
      for (var i = start; i < content.Length; i++) {
        var c = content[i];
        if (inString) {
          if (c == '\\') i++;
          else if (c == '"') inString = false;
          continue;
        }
        switch (c) {
          case '"':
            inString = true;
            break;
          case '[':
            depth++;
            break;
          case ']':
            if (--depth == 0) return i;
            break;
        }
      }
      return -1;
    }
  }
}
