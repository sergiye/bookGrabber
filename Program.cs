using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TagLib;

namespace bookGrabber {

  internal class Program {

    private static readonly object gate = new object();
    private static int consoleTop;

    static async Task Main(string[] args) {

      var url = args.Length < 1 ? null : args[0];

      if (string.IsNullOrEmpty(url)) {
        Write("Enter book or book sequence url: ");
        url = Console.ReadLine();
      }
      
      var subDir = args.Length < 2 ? null : args[1];

      await DownloadBook(url, subDir, true);
    }

    static async Task DownloadBook(string url, string subDir = null, bool isFirstBook = false) {

      var errors = new Dictionary<string, Exception>();
      var nextBookUrl = string.Empty;

      try {
        if (string.IsNullOrWhiteSpace(url))
          throw new Exception("Book url can not be empty");

        Write("Retrieving book content... ");
        var content = (await GetContent(url)).TrimEnd();
        WriteLine($"\tDone!", ConsoleColor.Green);

        var author = string.Empty;
        var bookTitle = string.Empty;
        var title = string.Empty;
        var sequenceNumber = 0;
        var sequenceName = string.Empty;

        var sequenceNameMatch = Regex.Match(content, @"<div class=""book_serie_block_title"">\s+.+>([^>]+)<\/a>");
        if (sequenceNameMatch.Success) {
          sequenceName = sequenceNameMatch.Groups[1].Value;
        }
        var sequences = Regex.Matches(content, @"<div class=""book_serie_block_item"">\s+<span class=""book_serie_block_item_index"">(\d+)\.<\/span>(\s+<a href=""([^""]+)"">)?");
        if (sequences.Count > 0) {
          for(var i = 0; i < sequences.Count; i++) {
            if (sequences[i].Groups[3].Success == false) {
              if (int.TryParse(sequences[i].Groups[1].Value, out var number))
                sequenceNumber = number;

              if (sequences.Count > i + 1) {
                nextBookUrl = sequences[i+1].Groups[3].Value;
                if (!nextBookUrl.StartsWith("http")) {
                  nextBookUrl = "https://knigavuhe.org" + nextBookUrl;
                }
              }
              break;
            }
          }
        }

        var matches = Regex.Matches(content, @"<meta property=""og:title"" content=""([^""]+) - ([^>]+)"">");
        if (matches.Count != 0 && matches[0].Groups.Count > 1) {
          author = GetValidFileName(matches[0].Groups[1].Value, true);
          bookTitle = GetValidFileName(matches[0].Groups[2].Value, true);
          if (!string.IsNullOrEmpty(sequenceName))
            title = sequenceNumber > 0 ? $"{sequenceName} - {sequenceNumber}. {author} - {bookTitle}" : $"{author} - {bookTitle}";
          else if (sequenceNumber > 0)
            title = $"{author} - {sequenceNumber} - {bookTitle}";
          else
            title = $"{author} - {bookTitle}";
          Console.Title = title;
        }

        Picture bookImage = null;
        matches = Regex.Matches(content, @"<meta property=""og:image"" content=""([^>]+)"">");
        if (matches.Count != 0 && matches[0].Groups.Count > 1) {
          var bookImgUrl = matches[0].Groups[1].Value;
          Write("Retrieving book image... ");
          var bookImageFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
          using (var wc = new WebClient())
            await wc.DownloadFileTaskAsync(bookImgUrl, bookImageFilePath);
          bookImage = new Picture(bookImageFilePath);
          System.IO.File.Delete(bookImageFilePath);
          WriteLine($"\tDone!", ConsoleColor.Green);
        }

        if (!isFirstBook) {
          subDir = GetValidFileName(title, true);
        }
        if (string.IsNullOrEmpty(subDir)) {
          Write("Press 'Enter' to use '");
          Write($"{title}", ConsoleColor.Yellow);
          Write("' as subdir, or enter new one: ");
          var value = Console.ReadLine();
          subDir = string.IsNullOrWhiteSpace(value) ? title : GetValidFileName(value, true);
        }

        // var useTitle = args.Length > 2 && args[2] == "/t";
        // var useUri = args.Length > 2 && args[2] == "/u";
        // if (args.Length == 0) {
        //     Write("Get file names from: 't' (title), 'u' (url), or 'n' (number, by default): ");
        //     var value = Console.ReadLine();
        //     useUri = value == "u";
        //     useTitle = value == "t";
        // }

        var coll = Regex.Matches(content, @"new BookPlayer\([\d]+,\s(\[[^\[]+\]),\s\[");
        if (coll.Count == 0 || coll[0].Groups.Count < 2)
          throw new Exception("No tracks found");

        var jsonData = Regex.Unescape(coll[0].Groups[1].Value);

        var tracks = jsonData.FromJson<TrackInfo[]>();
        if (tracks == null || tracks.Length == 0)
          throw new Exception("Error getting list of tracks");

        var asm = Assembly.GetExecutingAssembly();
        var outPath = Path.GetDirectoryName(asm.Location);
        if (!string.IsNullOrEmpty(subDir)) {
          outPath = Path.Combine(outPath, subDir);
          Directory.CreateDirectory(outPath);
        }
        Write("Target folder: ");
        WriteLine(outPath, ConsoleColor.DarkCyan);

        Console.CursorVisible = false;
        consoleTop = Console.CursorTop;

        var done = 0;
        var failed = 0;
        ShowProgress(tracks.Length, done, failed, title);
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Normal);
        TaskbarProgressHelper.SetValue(done, tracks.Length);

#if !DEBUG
        var tasks = Enumerable.Range(0, tracks.Length)
            .Select(async (i) => {
#else
        for (var i = 0; i < tracks.Length; i++) {
#endif
              var track = tracks[i];
              var trackNum = i + 1;

              var fileName = $"{trackNum.ToString().PadLeft(tracks.Length.ToString().Length, '0')}.mp3";
              // if (useTitle) {
              //     fileName =  GetValidFileName(track.title, false);
              // }
              // else if (useUri) {
              //     Uri uri = new Uri(track.url);
              //     if (uri.IsFile)
              //         fileName = Path.GetFileName(uri.LocalPath);
              //     else if (uri.Segments != null && uri.Segments.Length > 0 && uri.Segments[uri.Segments.Length - 1].EndsWith(".mp3"))
              //         fileName = uri.Segments[uri.Segments.Length - 1];
              // }
              if (Path.GetExtension(fileName) != ".mp3")
                fileName += ".mp3";

              var outputPath = Path.Combine(outPath, fileName);
              try {
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Exists && fileInfo.Length > 0) {
                  Interlocked.Increment(ref done);
#if !DEBUG
                  return;
#else
                  continue;
#endif                  
                }

                await DownloadFile(track.url, outputPath);

                var f = TagLib.File.Create(outputPath);
                //Console.WriteLine("Title: {0}, duration: {1}", tfile.Tag.Title, tfile.Properties.Duration);
                f.Tag.Track = (uint)trackNum;
                f.Tag.TrackCount = (uint)tracks.Length;
                if (string.IsNullOrEmpty(f.Tag.Title))
                  f.Tag.Title = track.title;
                f.Tag.Album = bookTitle;
                if (f.Tag.Performers == null || f.Tag.Performers.Length == 0)
                  f.Tag.Performers = new[] { author };
                else if (f.Tag.Performers[0] != author)
                  f.Tag.Performers = new[] { author }.Union(f.Tag.Performers).ToArray();
                if (f.Tag.AlbumArtists == null || f.Tag.AlbumArtists.Length == 0)
                  f.Tag.AlbumArtists = new[] { author };
                else if (f.Tag.AlbumArtists[0] != author)
                  f.Tag.AlbumArtists = new[] { author }.Union(f.Tag.AlbumArtists).ToArray();

                var comment = $"saved by bookGrabber from {url}";
                if (string.IsNullOrWhiteSpace(f.Tag.Comment))
                  f.Tag.Comment = comment;
                else 
                  f.Tag.Comment += "\n" + comment;

                if (bookImage != null && bookImage.Type != PictureType.NotAPicture)
                  f.Tag.Pictures = new IPicture[1] { bookImage };

                f.Save();

                Interlocked.Increment(ref done);
              }
              catch (Exception ex) {
                Interlocked.Increment(ref failed);
                errors[track.url] = ex;
                System.IO.File.Delete(outputPath);
              }
              finally {
                ShowProgress(tracks.Length, done, failed, title);
                if (failed > 0)
                  TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Error);
                TaskbarProgressHelper.SetValue(done, tracks.Length);
              }
#if !DEBUG              
            }).ToArray();
        await Task.WhenAll(tasks);
#else
        }
#endif        
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.NoProgress);

        Console.SetCursorPosition(0, consoleTop + 3);
        WriteLine("Finished", ConsoleColor.DarkCyan);
      }
      catch (Exception ex) {
        errors["internal"] = ex;
      }

      Console.CursorVisible = true;
      Console.SetCursorPosition(0, consoleTop + 5);

      if (errors.Count > 0) {
        WriteLine($"Errors summary:");
        foreach (var p in errors) {
          Write($" - Error loading {p.Key}: ", ConsoleColor.Red);
          WriteLine($"{p.Value.Message}", ConsoleColor.Yellow);
        }
        WriteLine("Press 'Enter' to exit...");
        Console.ReadLine();
      }
      else if (!string.IsNullOrEmpty(nextBookUrl)) {
        if (isFirstBook) {
          WriteLine("Press 'Esc' to exit or any other key to download other sequence books...");
          if (Console.ReadKey().Key == ConsoleKey.Escape)
            return;
        }
        await DownloadBook(nextBookUrl);
      }
    }

    private static void ShowProgress(int count, int done, int failed, string title) {
      lock (gate) {
        Console.SetCursorPosition(0, consoleTop + 1);
        Write("Downloading ", ConsoleColor.White);
        Write($"{count}", ConsoleColor.Cyan);
        Write(" tracks, done: ", ConsoleColor.White);
        Write($"{done}", ConsoleColor.Green);
        if (failed > 0) {
          Write($", failed: ", ConsoleColor.White);
          Write($"{failed}", ConsoleColor.Red);
        }
        Write(", completed: ", ConsoleColor.White);
        var percents = (done + failed) * 100 / count;
        Write($"{percents}%", ConsoleColor.Yellow);
        Console.Title = $"{percents}% {title}";
      }
    }

    private static async Task<string> GetContent(string uri) {
      using (var wc = new WebClient()) {
        //wc.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.500.27 Safari/537.36");
        wc.Headers.Add("content-type", "application/json; charset=utf-8");
        return await wc.DownloadStringTaskAsync(uri).ConfigureAwait(false);
      }
    }

    private static async Task DownloadFile(string url, string outputPath, int retries = 3) {

      // var tryNum = 1;
      // while (tryNum < retries) {
      //     try {
      using (var wc = new WebClient())
        await wc.DownloadFileTaskAsync(url, outputPath);
      //     }
      //     catch (Exception ex) {
      //         if (ex is WebException webEx && webEx.Response is HttpWebResponse response) {
      //             switch ((WebExceptionStatus) response.StatusCode) {
      //                 case WebExceptionStatus.ConnectFailure:
      //                 case WebExceptionStatus.ReceiveFailure:
      //                 case WebExceptionStatus.RequestCanceled:
      //                 case WebExceptionStatus.ProtocolError:
      //                 case WebExceptionStatus.ConnectionClosed:
      //                 case WebExceptionStatus.KeepAliveFailure:
      //                 case WebExceptionStatus.Pending:
      //                 case WebExceptionStatus.Timeout:
      //                 case WebExceptionStatus.ProxyNameResolutionFailure:
      //                     tryNum++;
      //                     continue;
      //             }
      //         }
      //         throw;
      //     }
      // }
    }

    private static string GetValidFileName(string fileName, bool allowEmpty) {
      if (string.IsNullOrWhiteSpace(fileName) && !allowEmpty)
        throw new ArgumentException("File name can not be empty.");
      foreach (char c in Path.GetInvalidFileNameChars()) {
        fileName = fileName.Replace(c, ' ').Trim();
      }
      return fileName.TrimEnd('.');
    }

    private static void WriteLine(string message = null, ConsoleColor? color = null, ConsoleColor? backColor = null) {
      Write(message, color, backColor, true);
    }

    private static void Write(string message = null, ConsoleColor? color = null, ConsoleColor? backColor = null, bool newLine = false) {
      if (backColor.HasValue)
        Console.BackgroundColor = backColor.Value;
      if (color.HasValue)
        Console.ForegroundColor = color.Value;
      if (newLine)
        Console.WriteLine(message);
      else
        Console.Write(message);
      Console.ResetColor();
    }
  }
}
