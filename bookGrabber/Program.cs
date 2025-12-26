using sergiye.Common;
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
    private static string subDirTemplate = "%f %s %n - %t";

    static async Task Main(string[] args) {

      ServicePointManager.Expect100Continue = false;
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
      ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

      var url = args.Length < 1 ? null : args[0];

      if (string.IsNullOrEmpty(url)) {
        Utils.Write("Enter book or book sequence url: ");
        url = Console.ReadLine();
      }
      
      var subDir = args.Length < 2 ? null : args[1];
      
      if (args.Length < 3 || !int.TryParse(args[2], out var maxDownloadThreads))
        maxDownloadThreads = Environment.ProcessorCount;

      await DownloadBook(url, maxDownloadThreads, subDir, true);
    }

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
    private static string GetTitle(string author, string sequenceName, int sequenceNumber, string bookTitle) {
      var title = subDirTemplate
        .Replace("%f", author)
        .Replace("%t", bookTitle)
        .Replace("%s", string.IsNullOrEmpty(sequenceName) ? "" : sequenceName)
        .Replace("%n", sequenceNumber > 0 ? sequenceNumber.ToString() : "")
        .NormalizeSpaces();
      return title;
    }
    
    private static async Task DownloadBook(string url, int maxDownloadThreads, string subDir, bool isFirstBook) {

      var errors = new Dictionary<string, Exception>();
      var nextBookUrl = string.Empty;

      try {
        if (string.IsNullOrWhiteSpace(url))
          throw new Exception("Book url can not be empty");

        Utils.Write("Retrieving book content... ");
        var content = (await Utils.GetContent(url)).TrimEnd();
        Utils.WriteLine("\tDone!", ConsoleColor.Green);

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
            if (sequences[i].Groups[3].Success) continue;
            if (int.TryParse(sequences[i].Groups[1].Value, out var number))
              sequenceNumber = number;

            if (sequences.Count > i + 1) {
              nextBookUrl = sequences[i+1].Groups[3].Value;
              if (!string.IsNullOrEmpty(nextBookUrl)) {
                if (!nextBookUrl.StartsWith("http")) {
                  nextBookUrl = "https://knigavuhe.org" + nextBookUrl;
                }
                if (isFirstBook) {
                  Utils.WriteLine("Download other books in series? Press 'Esc' to cancel or any other key to agree...");
                  if (Console.ReadKey().Key == ConsoleKey.Escape)
                    nextBookUrl = null;
                }
              }
            }
            break;
          }
        }

        var matches = Regex.Matches(content, @"<meta property=""og:title"" content=""([^""]+) - ([^>]+)"">");
        if (matches.Count != 0 && matches[0].Groups.Count > 1) {
          author = Utils.GetValidFileName(matches[0].Groups[1].Value, true);
          bookTitle = Utils.GetValidFileName(matches[0].Groups[2].Value, true);
          title = GetTitle(author, sequenceName, sequenceNumber, bookTitle);
          Console.Title = title;
        }

        Picture bookImage = null;
        matches = Regex.Matches(content, @"<meta property=""og:image"" content=""([^>]+)"">");
        if (matches.Count != 0 && matches[0].Groups.Count > 1) {
          var bookImgUrl = matches[0].Groups[1].Value;
          Utils.Write("Retrieving book image... ");
          var bookImageFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
          await Utils.DownloadFile(bookImgUrl, bookImageFilePath);
          bookImage = new Picture(bookImageFilePath);
          System.IO.File.Delete(bookImageFilePath);
          Utils.WriteLine("\tDone!", ConsoleColor.Green);
        }

        if (!isFirstBook) {
          subDir = Utils.GetValidFileName(title, true);
        }
        if (string.IsNullOrEmpty(subDir)) {
          Utils.Write("Press 'Enter' to use '");
          Utils.Write($"{title}", ConsoleColor.Yellow);
          Utils.Write("' as subdir, or enter new one: ");
          var value = Console.ReadLine();
          if (string.IsNullOrWhiteSpace(value)) {
            subDir = title;
          }
          else {
            if (value.Contains("%f") || value.Contains("%s") || value.Contains("%n") || value.Contains("%t")) {
              subDirTemplate = value;
              value = title = GetTitle(author, sequenceName, sequenceNumber, bookTitle);
            }
            subDir = Utils.GetValidFileName(value, true);
          }
        }
        
        var coll = Regex.Matches(content, @"new BookPlayer\([\d]+,\s(\[[^\[]+\]),\s\[");
        if (coll.Count == 0 || coll[0].Groups.Count < 2)
          throw new Exception("No tracks found");

        var jsonData = Regex.Unescape(coll[0].Groups[1].Value);

        var tracks = jsonData.FromJson<TrackInfo[]>();
        if (tracks == null || tracks.Length == 0)
          throw new Exception("Error getting list of tracks");

        var asm = Assembly.GetExecutingAssembly();
        var outPath = Path.GetDirectoryName(asm.Location) ?? throw new Exception("Invalid assembly location.");
        if (!string.IsNullOrEmpty(subDir)) {
          outPath = Path.Combine(outPath, subDir);
          Directory.CreateDirectory(outPath);
        }

        Utils.Write("Target folder: ");
        Utils.WriteLine(outPath, ConsoleColor.DarkCyan);

        Console.CursorVisible = false;
        consoleTop = Console.CursorTop;

        var done = 0;
        var failed = 0;
        ShowProgress(tracks.Length, done, failed, title);
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Normal);
        TaskbarProgressHelper.SetValue(done, tracks.Length);

        var semaphore = new SemaphoreSlim(maxDownloadThreads, maxDownloadThreads);
        await Task.WhenAll(Enumerable.Range(0, tracks.Length).Select(async i => {
          await semaphore.WaitAsync();

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
              return;
            }

            await Utils.DownloadFile(track.url, outputPath);

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
              f.Tag.Pictures = new IPicture[] { bookImage };

            f.Save();

            Interlocked.Increment(ref done);
          }
          catch (Exception ex) {
            Interlocked.Increment(ref failed);
            errors[track.url] = ex;
            System.IO.File.Delete(outputPath);
          }
          finally {
            semaphore.Release();
            ShowProgress(tracks.Length, done, failed, title);
            if (failed > 0)
              TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Error);
            TaskbarProgressHelper.SetValue(done, tracks.Length);
          }
        }).ToArray());
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.NoProgress);

        Utils.SafeSetCursorPosition(0, consoleTop + 3);
        Utils.WriteLine("Finished", ConsoleColor.DarkCyan);
      }
      catch (Exception ex) {
        errors["internal"] = ex;
      }

      Console.CursorVisible = true;
      Utils.SafeSetCursorPosition(0, consoleTop + 5);

      if (errors.Count > 0) {
        Utils.WriteLine("Errors summary:");
        foreach (var p in errors) {
          Utils.Write($" - Error loading {p.Key}: ", ConsoleColor.Red);
          Utils.WriteLine($"{p.Value.Message}", ConsoleColor.Yellow);
        }

        Utils.WriteLine("Press any key to retry download or 'Esc' to exit...");
        if (Console.ReadKey().Key != ConsoleKey.Escape)
          await DownloadBook(url, maxDownloadThreads, subDir, isFirstBook);
      }
      else if (!string.IsNullOrEmpty(nextBookUrl)) {
        await DownloadBook(nextBookUrl, maxDownloadThreads, null, false);
      }
    }

    private static void ShowProgress(int count, int done, int failed, string title) {
      lock (gate) {
        Utils.SafeSetCursorPosition(0, consoleTop + 1);
        Utils.Write("Downloading ", ConsoleColor.White);
        Utils.Write($"{count}", ConsoleColor.Cyan);
        Utils.Write(" tracks, done: ", ConsoleColor.White);
        Utils.Write($"{done}", ConsoleColor.Green);
        if (failed > 0) {
          Utils.Write(", failed: ", ConsoleColor.White);
          Utils.Write($"{failed}", ConsoleColor.Red);
        }

        Utils.Write(", completed: ", ConsoleColor.White);
        var percents = (done + failed) * 100 / count;
        Utils.Write($"{percents}%", ConsoleColor.Yellow);
        Console.Title = $"{percents}% {title}";
      }
    }
  }
}
