using sergiye.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TagLib;

namespace bookGrabber {

  internal class Program {

    private static readonly object gate = new object();
    private static int consoleTop;

    static async Task Main(string[] args) {

      ServicePointManager.Expect100Continue = false;
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
      ServicePointManager.ServerCertificateValidationCallback += (_, _, _, _) => true;

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

    private static async Task DownloadBook(string url, int maxDownloadThreads, string subDir, bool isFirstBook) {

      var errors = new Dictionary<string, Exception>();
      var nextBookUrl = string.Empty;

      try {
        if (string.IsNullOrWhiteSpace(url))
          throw new Exception("Book url can not be empty");

        Utils.Write("Retrieving book content... ");
        var parser = await PageParserFabric.Create(url);
        Utils.WriteLine("\tDone!", ConsoleColor.Green);

        Picture bookImage = null;
        if (!string.IsNullOrWhiteSpace(parser.BookImgUrl)) {
          Utils.Write("Retrieving book image... ");
          var bookImageFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
          await Utils.DownloadFile(parser.BookImgUrl, bookImageFilePath);
          bookImage = new Picture(bookImageFilePath);
          System.IO.File.Delete(bookImageFilePath);
          Utils.WriteLine("\tDone!", ConsoleColor.Green);
        }

        nextBookUrl = parser.NextBookUrl;
        if (isFirstBook && !string.IsNullOrWhiteSpace(nextBookUrl)) {
          Utils.WriteLine("Download other books in series? Press 'Esc' to cancel or any other key to agree...");
          if (Console.ReadKey().Key == ConsoleKey.Escape)
            nextBookUrl = null;
        }

        Console.Title = parser.Title;
        if (!isFirstBook) {
          subDir = Utils.GetValidFileName(parser.Title, true);
        }
        if (string.IsNullOrEmpty(subDir)) {
          Utils.Write("Press 'Enter' to use '");
          Utils.Write($"{parser.Title}", ConsoleColor.Yellow);
          Utils.Write("' as subdir, or enter new one: ");
          var value = Console.ReadLine();
          if (string.IsNullOrWhiteSpace(value)) {
            subDir = parser.Title;
          }
          else {
            if (value.Contains("%f") || value.Contains("%s") || value.Contains("%n") || value.Contains("%t")) {
              PageParser.SubDirTemplate = value;
              value = parser.Title = PageParser.GetTitle(parser.Author, parser.SequenceName, parser.SequenceNumber, parser.BookTitle);
            }
            subDir = Utils.GetValidFileName(value, true);
          }
        }

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
        ShowProgress(parser.Tracks.Length, done, failed, parser.Title);
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Normal);
        TaskbarProgressHelper.SetValue(done, parser.Tracks.Length);

        var semaphore = new SemaphoreSlim(maxDownloadThreads, maxDownloadThreads);
        await Task.WhenAll(Enumerable.Range(0, parser.Tracks.Length).Select(async i => {
          await semaphore.WaitAsync();

          var track = parser.Tracks[i];
          var trackNum = i + 1;
          var fileName = $"{trackNum.ToString().PadLeft(parser.Tracks.Length.ToString().Length, '0')}.mp3";
          // if (useTitle) {
          //     fileName =  GetValidFileName(track.Title, false);
          // }
          // else if (useUri) {
          //     Uri uri = new Uri(track.Url);
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

            await Utils.DownloadFile(track.Url, outputPath);

            var f = TagLib.File.Create(outputPath);
            //Console.WriteLine("Title: {0}, duration: {1}", tfile.Tag.Title, tfile.Properties.Duration);
            f.Tag.Track = (uint)trackNum;
            f.Tag.TrackCount = (uint)parser.Tracks.Length;
            if (string.IsNullOrEmpty(f.Tag.Title))
              f.Tag.Title = track.Title;
            f.Tag.Album = parser.BookTitle;
            if (f.Tag.Performers == null || f.Tag.Performers.Length == 0)
              f.Tag.Performers = [parser.Author];
            else if (f.Tag.Performers[0] != parser.Author)
              f.Tag.Performers = new[] { parser.Author }.Union(f.Tag.Performers).ToArray();
            if (f.Tag.AlbumArtists == null || f.Tag.AlbumArtists.Length == 0)
              f.Tag.AlbumArtists = [parser.Author];
            else if (f.Tag.AlbumArtists[0] != parser.Author)
              f.Tag.AlbumArtists = new[] { parser.Author }.Union(f.Tag.AlbumArtists).ToArray();

            var comment = $"saved by bookGrabber from {url}";
            if (string.IsNullOrWhiteSpace(f.Tag.Comment))
              f.Tag.Comment = comment;
            else
              f.Tag.Comment += "\n" + comment;

            if (bookImage != null && bookImage.Type != PictureType.NotAPicture)
              f.Tag.Pictures = [bookImage];

            f.Save();

            Interlocked.Increment(ref done);
          }
          catch (Exception ex) {
            Interlocked.Increment(ref failed);
            errors[track.Url] = ex;
            System.IO.File.Delete(outputPath);
          }
          finally {
            semaphore.Release();
            ShowProgress(parser.Tracks.Length, done, failed, parser.Title);
            if (failed > 0)
              TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Error);
            TaskbarProgressHelper.SetValue(done, parser.Tracks.Length);
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
