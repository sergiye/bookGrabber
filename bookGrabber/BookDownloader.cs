using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;

namespace bookGrabber {

  internal sealed class BookDownloader {

    private static readonly HashSet<string> audioExtensions = new HashSet<string> { ".mp3", ".m4a", ".m4b", ".ogg", ".flac", ".wma" };

    private readonly PageParser parser;
    private readonly string outPath;
    private readonly string sourceUrl;
    private readonly int maxThreads;
    private readonly ProgressView progress;
    private int done;
    private int failed;

    public ConcurrentQueue<KeyValuePair<string, Exception>> Errors { get; } = new ConcurrentQueue<KeyValuePair<string, Exception>>();
    public ConcurrentQueue<KeyValuePair<string, Exception>> Warnings { get; } = new ConcurrentQueue<KeyValuePair<string, Exception>>();

    public BookDownloader(PageParser parser, string outPath, string sourceUrl, int maxThreads, ProgressView progress) {
      this.parser = parser;
      this.outPath = outPath;
      this.sourceUrl = sourceUrl;
      this.maxThreads = maxThreads;
      this.progress = progress;
    }

    public async Task Run() {
      var cover = await LoadCover();

      progress.Start();
      try {
        using (var semaphore = new SemaphoreSlim(maxThreads, maxThreads)) {
          await Task.WhenAll(Enumerable.Range(0, parser.Tracks.Length).Select(i => ProcessTrack(i, cover, semaphore)).ToArray());
        }
      }
      finally {
        progress.Finish();
      }
    }

    private async Task<Picture> LoadCover() {
      if (string.IsNullOrWhiteSpace(parser.BookImgUrl))
        return null;

      Utils.Write("Retrieving book image... ");
      try {
        var bytes = await Utils.GetBytes(parser.BookImgUrl, parser.Referer);
        var format = Utils.DetectFormat(bytes);
        var embed = true;
        if (format == Utils.ImageFormatType.Webp) {
          try {
            bytes = Utils.ConvertWebpToJpeg(bytes);
            format = Utils.ImageFormatType.Jpeg;
          }
          catch (Exception) {
            embed = false;
          }
        }
        System.IO.File.WriteAllBytes(Path.Combine(outPath, "folder" + Utils.GetImageExtension(format)), bytes);
        if (!embed) {
          Utils.WriteLine("\tSaved, but WebP can not be converted to embed", ConsoleColor.Yellow);
          return null;
        }
        Utils.WriteLine("\tDone!", ConsoleColor.Green);
        return new Picture(bytes);
      }
      catch (Exception ex) {
        Utils.WriteLine($"\tSkipped: {ex.Message}", ConsoleColor.Yellow);
        return null;
      }
    }

    private async Task ProcessTrack(int index, Picture cover, SemaphoreSlim semaphore) {
      await semaphore.WaitAsync();

      var track = parser.Tracks[index];
      var trackNum = index + 1;
      var fileName = trackNum.ToString().PadLeft(parser.Tracks.Length.ToString().Length, '0') + GetAudioExtension(track.Url);

      try {
        var outputPath = Path.Combine(outPath, fileName);
        var fileInfo = new FileInfo(outputPath);
        if (fileInfo.Exists && fileInfo.Length > 0) {
          Interlocked.Increment(ref done);
          return;
        }

        await Utils.DownloadFile(track.Url, outputPath, parser.Referer);

        try {
          WriteTags(outputPath, trackNum, track, cover);
        }
        catch (Exception ex) {
          Warnings.Enqueue(new KeyValuePair<string, Exception>(fileName, ex));
        }
        Interlocked.Increment(ref done);
      }
      catch (Exception ex) {
        Interlocked.Increment(ref failed);
        Errors.Enqueue(new KeyValuePair<string, Exception>($"{fileName} ({track.Url})", ex));
      }
      finally {
        semaphore.Release();
        progress.Update(Volatile.Read(ref done), Volatile.Read(ref failed));
      }
    }

    private void WriteTags(string path, int trackNum, TrackInfo track, Picture cover) {
      TagLib.File f;
      try {
        f = TagLib.File.Create(path);
      }
      catch (CorruptFileException) {
        StripId3v2Tags(path);
        f = TagLib.File.Create(path);
      }

      using (f) {
        f.Tag.Track = (uint) trackNum;
        f.Tag.TrackCount = (uint) parser.Tracks.Length;
        if (string.IsNullOrEmpty(f.Tag.Title))
          f.Tag.Title = track.Title;
        f.Tag.Album = parser.BookTitle;
        if (!string.IsNullOrEmpty(parser.Author)) {
          f.Tag.Performers = MergeFirst(parser.Author, f.Tag.Performers);
          f.Tag.AlbumArtists = MergeFirst(parser.Author, f.Tag.AlbumArtists);
        }

        var comment = $"saved by bookGrabber from {sourceUrl}";
        if (string.IsNullOrWhiteSpace(f.Tag.Comment))
          f.Tag.Comment = comment;
        else
          f.Tag.Comment += "\n" + comment;

        if (cover != null && cover.Type != PictureType.NotAPicture)
          f.Tag.Pictures = [cover];

        f.Save();
      }
    }

    private static string[] MergeFirst(string first, string[] existing) {
      if (existing == null || existing.Length == 0)
        return [first];
      return existing[0] == first ? existing : new[] { first }.Union(existing).ToArray();
    }

    private static string GetAudioExtension(string url) {
      if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
        var path = uri.AbsolutePath;
        var dot = path.LastIndexOf('.');
        if (dot > path.LastIndexOf('/')) {
          var ext = path.Substring(dot).ToLowerInvariant();
          if (audioExtensions.Contains(ext))
            return ext;
        }
      }
      return ".mp3";
    }

    private static void StripId3v2Tags(string path) {
      var data = System.IO.File.ReadAllBytes(path);
      var offset = 0;
      while (data.Length - offset >= 10 && data[offset] == 'I' && data[offset + 1] == 'D' && data[offset + 2] == '3') {
        var size = (data[offset + 6] & 0x7F) << 21 | (data[offset + 7] & 0x7F) << 14 | (data[offset + 8] & 0x7F) << 7 | (data[offset + 9] & 0x7F);
        var hasFooter = (data[offset + 5] & 0x10) != 0;
        offset = Math.Min(data.Length, offset + 10 + size + (hasFooter ? 10 : 0));
      }
      if (offset == 0)
        return;
      using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
      fs.Write(data, offset, data.Length - offset);
    }
  }
}
