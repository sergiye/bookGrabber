using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using sergiye.Common;

namespace bookGrabber {

  public class AudioBookMp3Parser : PageParser {

    public const string BaseUrl = "https://audiobook-mp3.com/";

    public override async Task Init(string url) {

      if (string.IsNullOrWhiteSpace(url))
        throw new ArgumentException("Url can not be empty.");

      var content = (await Utils.GetContent(url)).TrimEnd();

      SequenceName = string.Empty;
      SequenceNumber = string.Empty;
      NextBookUrl = string.Empty;
      Author = string.Empty;
      BookTitle = string.Empty;
      Title = string.Empty;
      var matches = Regex.Matches(content, @"<h1[^>]*class=""b-maintitle""[^>]*>\s*(?<author>.*?)\s*-\s*(?<title>.*?)\s*</h1>");
      if (matches.Count != 0 && matches[0].Groups.Count > 1) {
        Author = Utils.GetValidFileName(matches[0].Groups[1].Value, true);
        BookTitle = Utils.GetValidFileName(matches[0].Groups[2].Value, true);
        Title = GetTitle(Author, SequenceName, SequenceNumber, BookTitle);
      }

      BookImgUrl = ExtractBookImageUrl(content);

      var playlistUrl = ExtractPlaylistUrl(content);
      if (string.IsNullOrEmpty(playlistUrl)) {
        throw new Exception("No tracks found");
      }
      var playlistContent = await Utils.GetContent(playlistUrl, BaseUrl);
      var jsonData = Regex.Unescape(playlistContent)
        .Replace("\\", "\\\\"); //convert from JavaScript literal
      var playlistItems = jsonData.FromJson<PlayListItem[]>();
      Tracks = playlistItems.Select(i => new TrackInfo {Url = i.File, Title = i.Title}).ToArray();
      if (Tracks == null || Tracks.Length == 0)
        throw new Exception("Error getting list of tracks");
    }

    private static string ExtractBookImageUrl(string content) {
      var regex = new Regex(@"<img[^>]*class=""abook_image""[^>]*src=""(?<url>https?:\/\/[^""]+\.(?:jpg|jpeg|png|webp))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
      var match = regex.Match(content);
      return match.Success
        ? match.Groups["url"].Value
        : null;
    }

    private static string ExtractPlaylistUrl(string content) {
      var regex = new Regex(@"file\s*:\s*""(?<url>https?:\/\/[^""]+\.pl\.txt)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
      var match = regex.Match(content);
      return match.Success
        ? match.Groups["url"].Value
        : null;
    }

    private class PlayListItem {
      public string Title { get; set; }
      public string File { get; set; }
    }
  }
}
