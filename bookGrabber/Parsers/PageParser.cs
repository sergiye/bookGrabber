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
        .NormalizeSpaces();
      return title;
    }

    public abstract Task Init(string url);
  }
}
