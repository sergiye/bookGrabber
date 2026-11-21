using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using sergiye.Common;

namespace bookGrabber {

  public abstract class PlaylistPageParser : PageParser {

    private static readonly Regex playlistUrlRegex = new Regex(@"file\s*:\s*""(?<url>https?:\/\/[^""]+\.pl\.txt)""",
      RegexOptions.IgnoreCase | RegexOptions.Singleline);

    protected async Task LoadTracks(string pageContent, string referer) {
      var match = playlistUrlRegex.Match(pageContent);
      if (!match.Success)
        throw new Exception("No tracks found");

      var playlistContent = await Utils.GetContent(match.Groups["url"].Value, referer);
      var playlistItems = Utils.NormalizeJsLiteral(playlistContent).FromJson<PlayListItem[]>();
      if (playlistItems == null || playlistItems.Length == 0)
        throw new Exception("Error getting list of tracks");
      Tracks = playlistItems.Select(i => new TrackInfo {Url = i.File, Title = i.Title}).ToArray();
    }

    private class PlayListItem {
      public string Title { get; set; }
      public string File { get; set; }
    }
  }
}
