using sergiye.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TagLib;

namespace bookGrabber {

  internal class Program {

    private static readonly object gate = new object();
    private static int consoleTop;
    private static HttpClient httpClient;
    private const int BufferSize = 80 * 1024; //80KB

    static async Task Main(string[] args) {

      ServicePointManager.Expect100Continue = false;
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
      ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

      httpClient = new HttpClient(new HttpClientHandler {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        AllowAutoRedirect = true,
      }) {
        Timeout = TimeSpan.FromMinutes(5), 
      };
      httpClient.DefaultRequestHeaders.Referrer = new Uri("https://knigavuhe.org/");
      httpClient.DefaultRequestHeaders.Accept.Clear();
      httpClient.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json")
      );
      
      var url = args.Length < 1 ? null : args[0];

      if (string.IsNullOrEmpty(url)) {
        Write("Enter book or book sequence url: ");
        url = Console.ReadLine();
      }
      
      var subDir = args.Length < 2 ? null : args[1];
      
      if (args.Length < 3 || !int.TryParse(args[2], out var maxDownloadThreads))
        maxDownloadThreads = Environment.ProcessorCount;

      await DownloadBook(url, maxDownloadThreads, subDir, true);
    }

    private static async Task DownloadBook(string url, int maxDownloadThreads, string subDir = null, bool isFirstBook = false) {

      var errors = new Dictionary<string, Exception>();
      var nextBookUrl = string.Empty;

      try {
        if (string.IsNullOrWhiteSpace(url))
          throw new Exception("Book url can not be empty");

        Write("Retrieving book content... ");
        var content = (await GetContent(url)).TrimEnd();
        WriteLine("\tDone!", ConsoleColor.Green);

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
                  WriteLine("Download other books in series? Press 'Esc' to cancel or any other key to agree...");
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
          author = GetValidFileName(matches[0].Groups[1].Value, true);
          bookTitle = GetValidFileName(matches[0].Groups[2].Value, true);
          if (!string.IsNullOrEmpty(sequenceName))
            title = sequenceNumber > 0 ? $"{author} - {sequenceName} - {sequenceNumber}. {bookTitle}" : $"{author} - {bookTitle}";
          else if (sequenceNumber > 0)
            title = $"{author} - {sequenceNumber}. {bookTitle}";
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
          await DownloadFile(bookImgUrl, bookImageFilePath);
          bookImage = new Picture(bookImageFilePath);
          System.IO.File.Delete(bookImageFilePath);
          WriteLine("\tDone!", ConsoleColor.Green);
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

        SafeSetCursorPosition(0, consoleTop + 3);
        WriteLine("Finished", ConsoleColor.DarkCyan);
      }
      catch (Exception ex) {
        errors["internal"] = ex;
      }

      Console.CursorVisible = true;
      SafeSetCursorPosition(0, consoleTop + 5);

      if (errors.Count > 0) {
        WriteLine("Errors summary:");
        foreach (var p in errors) {
          Write($" - Error loading {p.Key}: ", ConsoleColor.Red);
          WriteLine($"{p.Value.Message}", ConsoleColor.Yellow);
        }
        WriteLine("Press any key to retry download or 'Esc' to exit...");
        if (Console.ReadKey().Key != ConsoleKey.Escape)
          await DownloadBook(url, maxDownloadThreads, subDir, isFirstBook);
      }
      else if (!string.IsNullOrEmpty(nextBookUrl)) {
        await DownloadBook(nextBookUrl, maxDownloadThreads);
      }
    }

    private static void ShowProgress(int count, int done, int failed, string title) {
      lock (gate) {
        SafeSetCursorPosition(0, consoleTop + 1);
        Write("Downloading ", ConsoleColor.White);
        Write($"{count}", ConsoleColor.Cyan);
        Write(" tracks, done: ", ConsoleColor.White);
        Write($"{done}", ConsoleColor.Green);
        if (failed > 0) {
          Write(", failed: ", ConsoleColor.White);
          Write($"{failed}", ConsoleColor.Red);
        }
        Write(", completed: ", ConsoleColor.White);
        var percents = (done + failed) * 100 / count;
        Write($"{percents}%", ConsoleColor.Yellow);
        Console.Title = $"{percents}% {title}";
      }
    }

    private static void SafeSetCursorPosition(int x, int y) {
      try {
        var maxX = Math.Max(0, Console.BufferWidth - 1);
        var maxY = Math.Max(0, Console.BufferHeight - 1);
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x > maxX) x = maxX;
        if (y > maxY) y = maxY;
        Console.SetCursorPosition(x, y);
      }
      catch (ArgumentOutOfRangeException) {
        Console.SetCursorPosition(0, Math.Max(0, Console.BufferHeight - 1));
      }
    }

    private static async Task<string> GetContent(string uri, CancellationToken token = default) {
      using (var response = await httpClient.GetAsync(uri, token)) {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
      }
    }

    private static async Task DownloadFile(string url, string outputPath, int maxRetries = 1, CancellationToken cancellationToken = default) {
      for (var attempt = 1; attempt <= maxRetries; attempt++) {
        try {
          using (var response = await httpClient.GetAsync(url,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken)) {
            if (!response.IsSuccessStatusCode) {
              if (IsRetryableStatusCode(response.StatusCode) && attempt < maxRetries) {
                await DelayBeforeRetry(attempt, cancellationToken);
                continue;
              }
              response.EnsureSuccessStatusCode();
            }
            using (var input = await response.Content.ReadAsStreamAsync())
            using (var output = new FileStream(outputPath,
                     FileMode.Create, FileAccess.Write, FileShare.None,
                     BufferSize, useAsync: true)) {
              await input.CopyToAsync(output, BufferSize, cancellationToken);
            }
          }
          return;
        }
        catch (TaskCanceledException ex) {
          if (cancellationToken.IsCancellationRequested)
            throw;
          if (attempt >= maxRetries) throw new TimeoutException("HTTP request timed out.", ex);
          await DelayBeforeRetry(attempt, cancellationToken);
        }
        catch (HttpRequestException) {
          if (attempt >= maxRetries) throw;
          await DelayBeforeRetry(attempt, cancellationToken);
        }
      }
    }

    private static Task DelayBeforeRetry(int attempt, CancellationToken token) {
      var delay = TimeSpan.FromSeconds(Math.Min(2 * attempt, 10));
      return Task.Delay(delay, token);
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode) {
      var code = (int) statusCode;
      return
        statusCode == HttpStatusCode.RequestTimeout
        || code == 429 //too many requests 
        || code >= 500 //all server errors
        ;
    }

    private static string GetValidFileName(string fileName, bool allowEmpty) {
      if (string.IsNullOrWhiteSpace(fileName))
        return allowEmpty ? fileName : throw new ArgumentException("File name can not be empty.");
      fileName = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, ' ').Trim());
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
