using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bookGrabber {

  public class AudioBookMp3Parser : PlaylistPageParser {

    public const string BaseUrl = "https://audiobook-mp3.com/";

    public override async Task Init(string url) {

      if (string.IsNullOrWhiteSpace(url))
        throw new ArgumentException("Url can not be empty.");

      var content = (await Utils.GetContent(url)).TrimEnd();

      ResetInfo();
      var h1 = Regex.Match(content, @"<h1[^>]*class=""b-maintitle""[^>]*>\s*(?<text>.*?)\s*</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
      if (h1.Success) {
        var text = WebUtility.HtmlDecode(Regex.Replace(h1.Groups["text"].Value, "<[^>]+>", string.Empty)).Trim();
        if (SplitTitle(text, false, out var author, out var title))
          SetTitles(author, title);
        else
          SetTitles(string.Empty, text);
      }

      BookImgUrl = ResolveUrl(url, ExtractBookImageUrl(content));

      await LoadTracks(content, BaseUrl);
    }

    private static string ExtractBookImageUrl(string content) {
      var regex = new Regex(@"<img[^>]*class=""abook_image""[^>]*src=""(?<url>https?:\/\/[^""]+\.(?:jpg|jpeg|png|webp))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
      var match = regex.Match(content);
      return match.Success
        ? match.Groups["url"].Value
        : null;
    }
  }
}
