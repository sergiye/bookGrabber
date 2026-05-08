using System;
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

      var sequenceNameMatch = Regex.Match(content, @"<div class=""book_serie_block_title"">\s+.+>([^>]+)<\/a>");
      SequenceName = sequenceNameMatch.Success
        ? sequenceNameMatch.Groups[1].Value
        : string.Empty;

      SequenceNumber = string.Empty;
      NextBookUrl = string.Empty;
      var sequences = Regex.Matches(content, @"<div class=""book_serie_block_item"">\s*(<span.+)?(\s*<a href=""([^""]+)"">)?");
      if (sequences.Count > 0) {
        for (var i = 0; i < sequences.Count; i++) {
          if (sequences[i].Groups[3].Success) continue;
          var sequenceNumberMatch = Regex.Match(sequences[i].Groups[1].Value, @"<span class=""book_serie_block_item_index"">(\d+\.?\d*)\.<\/span>?");
          if (sequenceNumberMatch.Groups[1].Success) {
            SequenceNumber = sequenceNumberMatch.Groups[1].Value;
          }
          if (sequences.Count > i + 1) {
            NextBookUrl = sequences[i + 1].Groups[3].Value;
            if (!string.IsNullOrEmpty(NextBookUrl) && !NextBookUrl.StartsWith("http")) {
              NextBookUrl = BaseUrl + NextBookUrl;
            }
          }
          break;
        }
      }

      Author = string.Empty;
      BookTitle = string.Empty;
      Title = string.Empty;
      var matches = Regex.Matches(content, @"<meta property=""og:title"" content=""([^""]+) - ([^>]+)"">");
      if (matches.Count != 0 && matches[0].Groups.Count > 1) {
        Author = Utils.GetValidFileName(matches[0].Groups[1].Value, true);
        BookTitle = Utils.GetValidFileName(matches[0].Groups[2].Value, true);
        Title = GetTitle(Author, SequenceName, SequenceNumber, BookTitle);
      }

      matches = Regex.Matches(content, @"<meta property=""og:image"" content=""([^>]+)"">");
      if (matches.Count != 0 && matches[0].Groups.Count > 1) {
        BookImgUrl = matches[0].Groups[1].Value;
      }

      var coll = Regex.Matches(content, @"new BookPlayer\([\d]+,\s(\[[^\[]+\]),\s\[");
      if (coll.Count == 0 || coll[0].Groups.Count < 2)
        throw new Exception("No tracks found");

      var jsonData = Regex.Unescape(coll[0].Groups[1].Value);
      Tracks = jsonData.FromJson<TrackInfo[]>();
      if (Tracks == null || Tracks.Length == 0)
        throw new Exception("Error getting list of tracks");
    }
  }
}
