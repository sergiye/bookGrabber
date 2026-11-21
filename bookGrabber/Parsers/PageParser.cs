using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bookGrabber {

  public abstract class PageParser {

    public static string SubDirTemplate = "%f %s %n - %t";
    public string SequenceName { get; protected set; }
    public string SequenceNumber { get; protected set; }
    public string NextBookUrl { get; protected set; }
    public string Author { get; protected set; }
    public string BookTitle { get; protected set; }
    public string Title { get; set; }
    public string BookImgUrl { get; protected set; }
    public string Referer { get; internal set; }
    public TrackInfo[] Tracks { get; protected set; }

    /// <summary>
    /// %f - full author name <br/>
    /// %s - sequence name <br/>
    /// %n - sequence number <br/>
    /// %t - title <br/>
    /// </summary>
    /// <param name="author"></param>
    /// <param name="sequenceName"></param>
    /// <param name="sequenceNumber"></param>
    /// <param name="bookTitle"></param>
    /// <returns></returns>
    public static string GetTitle(string author, string sequenceName, string sequenceNumber, string bookTitle) {
      var title = SubDirTemplate
        .Replace("%f", author)
        .Replace("%t", bookTitle)
        .Replace("%s", string.IsNullOrEmpty(sequenceName) ? "" : sequenceName)
        .Replace("%n", string.IsNullOrEmpty(sequenceNumber) ? "" : sequenceNumber)
        .NormalizeSpaces()
        .Trim();
      return title;
    }

    public abstract Task Init(string url);

    protected void ResetInfo() {
      SequenceName = string.Empty;
      SequenceNumber = string.Empty;
      NextBookUrl = string.Empty;
      Author = string.Empty;
      BookTitle = string.Empty;
      Title = string.Empty;
    }

    protected void SetTitles(string author, string bookTitle) {
      Author = Utils.GetValidFileName(author, true) ?? string.Empty;
      BookTitle = Utils.GetValidFileName(bookTitle, true) ?? string.Empty;
      Title = GetTitle(Author, SequenceName, SequenceNumber, BookTitle);
    }

    protected static string GetMetaContent(string html, string property) {
      foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase)) {
        var name = Regex.Match(tag.Value, @"\b(?:property|name)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        if (!name.Success)
          continue;
        var nameValue = name.Groups[1].Success ? name.Groups[1].Value : name.Groups[2].Value;
        if (!string.Equals(nameValue, property, StringComparison.OrdinalIgnoreCase))
          continue;
        var content = Regex.Match(tag.Value, @"\bcontent\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        if (content.Success)
          return WebUtility.HtmlDecode(content.Groups[1].Success ? content.Groups[1].Value : content.Groups[2].Value);
      }
      return null;
    }

    //splits "left - right" on a hyphen surrounded by spaces, so names like "Jean-Paul" stay intact
    protected static bool SplitTitle(string text, bool splitAtLast, out string left, out string right) {
      var matches = Regex.Matches(text, @"\s+[-–—]\s+");
      if (matches.Count == 0) {
        left = right = string.Empty;
        return false;
      }
      var separator = splitAtLast ? matches[matches.Count - 1] : matches[0];
      left = text.Substring(0, separator.Index).Trim();
      right = text.Substring(separator.Index + separator.Length).Trim();
      return true;
    }

    protected static string ResolveUrl(string pageUrl, string url) {
      if (string.IsNullOrWhiteSpace(url))
        return url;
      url = WebUtility.HtmlDecode(url.Trim());
      if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var result))
        return result.AbsoluteUri;
      return url;
    }
  }
}
