using System;
using System.Threading.Tasks;

namespace bookGrabber {

  public class BooksAudioOnlineParser : PlaylistPageParser {

    public const string BaseUrl = "https://booksaudio-online.com";

    public override async Task Init(string url) {

      if (string.IsNullOrWhiteSpace(url))
        throw new ArgumentException("Url can not be empty.");

      var content = (await Utils.GetContent(url)).TrimEnd();

      ResetInfo();
      var ogTitle = GetMetaContent(content, "og:title");
      if (!string.IsNullOrWhiteSpace(ogTitle)) {
        //format is "title - author"
        if (SplitTitle(ogTitle, true, out var title, out var author))
          SetTitles(author, title);
        else
          SetTitles(string.Empty, ogTitle);
      }

      BookImgUrl = ResolveUrl(url, GetMetaContent(content, "og:image"));

      await LoadTracks(content, BaseUrl);
    }
  }
}
