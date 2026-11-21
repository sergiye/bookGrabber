using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace bookGrabber {

  internal class Program {

    static async Task Main(string[] args) {

      ServicePointManager.Expect100Continue = false;
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
      ServicePointManager.ServerCertificateValidationCallback += (_, _, _, _) => true;

      var url = args.Length < 1 ? null : args[0];

      if (string.IsNullOrEmpty(url)) {
        Utils.Write("Enter book or book sequence url: ");
        url = Console.ReadLine();
      }

      var context = new DownloadContext {
        SubDir = args.Length < 2 ? null : args[1],
        MaxThreads = args.Length >= 3 && int.TryParse(args[2], out var threads) && threads > 0 ? threads : Environment.ProcessorCount,
        IsFirstBook = true,
      };

      while (true) {
        var result = await DownloadBook(url, context);
        ShowSummary(result);

        if (result.Errors.Count > 0) {
          Utils.WriteLine("Press any key to retry download or 'Esc' to exit...");
          var key = Utils.ReadKey();
          if (key.HasValue && key.Value != ConsoleKey.Escape)
            continue;
          return;
        }

        if (string.IsNullOrEmpty(result.NextBookUrl))
          return;

        url = result.NextBookUrl;
        context.IsFirstBook = false;
        context.SubDir = null;
      }
    }

    private static async Task<BookResult> DownloadBook(string url, DownloadContext context) {

      var result = new BookResult();
      ProgressView view = null;

      try {
        if (string.IsNullOrWhiteSpace(url))
          throw new Exception("Book url can not be empty");

        Utils.Write("Retrieving book content... ");
        var parser = await PageParserFactory.Create(url);
        Utils.WriteLine("\tDone!", ConsoleColor.Green);

        result.NextBookUrl = parser.NextBookUrl;
        if (context.IsFirstBook && !string.IsNullOrWhiteSpace(result.NextBookUrl)) {
          if (context.DownloadSeries == null) {
            Utils.WriteLine("Download other books in series? Press 'Esc' to cancel or any other key to agree...");
            var key = Utils.ReadKey();
            context.DownloadSeries = key.HasValue && key.Value != ConsoleKey.Escape;
          }
          if (context.DownloadSeries != true)
            result.NextBookUrl = null;
        }

        Utils.SetTitle(parser.Title);
        if (!context.IsFirstBook)
          context.SubDir = Utils.GetValidFileName(parser.Title, true);
        if (string.IsNullOrEmpty(context.SubDir))
          context.SubDir = AskSubDir(parser);

        var outPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new Exception("Invalid assembly location.");
        if (!string.IsNullOrEmpty(context.SubDir)) {
          outPath = Path.Combine(outPath, context.SubDir);
          Directory.CreateDirectory(outPath);
        }

        Utils.Write("Target folder: ");
        Utils.WriteLine(outPath, ConsoleColor.DarkCyan);

        Utils.SetCursorVisible(false);
        view = new ProgressView(parser.Tracks.Length, parser.Title);

        var downloader = new BookDownloader(parser, outPath, url, context.MaxThreads, view);
        try {
          await downloader.Run();
        }
        finally {
          result.Errors.AddRange(downloader.Errors.OrderBy(p => p.Key));
          result.Warnings.AddRange(downloader.Warnings.OrderBy(p => p.Key));
        }

        Utils.SafeSetCursorPosition(0, view.Top + 3);
        Utils.WriteLine("Finished", ConsoleColor.DarkCyan);
      }
      catch (Exception ex) {
        result.Errors.Add(new KeyValuePair<string, Exception>("internal", ex));
      }

      Utils.SetCursorVisible(true);
      if (view != null)
        Utils.SafeSetCursorPosition(0, view.Top + 5);
      return result;
    }

    private static string AskSubDir(PageParser parser) {
      Utils.Write("Press 'Enter' to use '");
      Utils.Write($"{parser.Title}", ConsoleColor.Yellow);
      Utils.Write("' as subdir, or enter new one: ");
      var value = Console.ReadLine();
      if (string.IsNullOrWhiteSpace(value))
        return parser.Title;

      if (value.Contains("%f") || value.Contains("%s") || value.Contains("%n") || value.Contains("%t")) {
        PageParser.SubDirTemplate = value;
        value = parser.Title = PageParser.GetTitle(parser.Author, parser.SequenceName, parser.SequenceNumber, parser.BookTitle);
      }
      return Utils.GetValidFileName(value, true);
    }

    private static void ShowSummary(BookResult result) {
      if (result.Warnings.Count > 0) {
        Utils.WriteLine("Warnings summary:");
        foreach (var p in result.Warnings) {
          Utils.Write($" - Error writing tags to {p.Key}: ", ConsoleColor.Yellow);
          Utils.WriteLine(p.Value.Message);
        }
      }
      if (result.Errors.Count > 0) {
        Utils.WriteLine("Errors summary:");
        foreach (var p in result.Errors) {
          Utils.Write($" - Error loading {p.Key}: ", ConsoleColor.Red);
          Utils.WriteLine($"{p.Value.Message}", ConsoleColor.Yellow);
        }
      }
    }

    private class DownloadContext {
      public string SubDir;
      public int MaxThreads;
      public bool IsFirstBook;
      public bool? DownloadSeries;
    }

    private class BookResult {
      public string NextBookUrl;
      public readonly List<KeyValuePair<string, Exception>> Errors = new List<KeyValuePair<string, Exception>>();
      public readonly List<KeyValuePair<string, Exception>> Warnings = new List<KeyValuePair<string, Exception>>();
    }
  }
}
