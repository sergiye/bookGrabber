using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace bookGrabber {

  internal static class Utils {

    private static readonly HttpClient httpClient;
    private const int BufferSize = 80 * 1024; //80KB
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private static readonly Regex reservedNames = new Regex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase);

    static Utils() {
      httpClient = new HttpClient(new HttpClientHandler {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        AllowAutoRedirect = true,
      }) {
        Timeout = TimeSpan.FromMinutes(5),
      };
    }

    private static HttpRequestMessage CreateRequest(string uri, string referer) {
      var request = new HttpRequestMessage(HttpMethod.Get, uri);
      if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        request.Headers.Referrer = refererUri;
      else if (Uri.TryCreate(uri, UriKind.Absolute, out var target))
        request.Headers.Referrer = new Uri(target.GetLeftPart(UriPartial.Authority) + "/");
      return request;
    }

    public static async Task<string> GetContent(string uri, string referer = null, CancellationToken token = default) {
      using (var request = CreateRequest(uri, referer))
      using (var response = await httpClient.SendAsync(request, token)) {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
      }
    }

    public static async Task<byte[]> GetBytes(string uri, string referer = null, CancellationToken token = default) {
      using (var request = CreateRequest(uri, referer))
      using (var response = await httpClient.SendAsync(request, token)) {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
      }
    }

    public static async Task DownloadFile(string url, string outputPath, string referer = null, int maxRetries = 3, CancellationToken cancellationToken = default) {
      var tempPath = outputPath + ".tmp";
      for (var attempt = 1; ; attempt++) {
        var fatal = false;
        try {
          using (var request = CreateRequest(url, referer))
          using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)) {
            if (!response.IsSuccessStatusCode) {
              if (IsRetryableStatusCode(response.StatusCode) && attempt < maxRetries) {
                await DelayBeforeRetry(attempt, cancellationToken);
                continue;
              }
              fatal = true;
              response.EnsureSuccessStatusCode();
            }
            using (var input = await response.Content.ReadAsStreamAsync())
            using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true)) {
              await CopyWithStallTimeout(input, output, cancellationToken);
            }
          }
          if (System.IO.File.Exists(outputPath))
            System.IO.File.Delete(outputPath);
          System.IO.File.Move(tempPath, outputPath);
          return;
        }
        catch (Exception ex) when (!fatal && IsTransient(ex, cancellationToken)) {
          TryDelete(tempPath);
          if (attempt >= maxRetries) {
            if (ex is OperationCanceledException)
              throw new TimeoutException("HTTP request timed out.", ex);
            throw;
          }
          await DelayBeforeRetry(attempt, cancellationToken);
        }
        catch {
          TryDelete(tempPath);
          throw;
        }
      }
    }

    private static async Task CopyWithStallTimeout(Stream input, Stream output, CancellationToken token) {
      var buffer = new byte[BufferSize];
      using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
      //the response stream on .NET Framework may ignore the token, so a stalled read is interrupted by disposing the stream
      using (cts.Token.Register(input.Dispose)) {
        try {
          while (true) {
            cts.CancelAfter(StallTimeout);
            var read = await input.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (read == 0)
              break;
            await output.WriteAsync(buffer, 0, read, token);
          }
        }
        catch (Exception) when (cts.IsCancellationRequested && !token.IsCancellationRequested) {
          throw new TimeoutException("Download stalled.");
        }
      }
    }

    private static bool IsTransient(Exception ex, CancellationToken token) {
      if (ex is OperationCanceledException)
        return !token.IsCancellationRequested;
      return ex is HttpRequestException || ex is IOException || ex is TimeoutException;
    }

    private static void TryDelete(string path) {
      try {
        System.IO.File.Delete(path);
      }
      catch (IOException) {
      }
      catch (UnauthorizedAccessException) {
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

    public static string GetValidFileName(string fileName, bool allowEmpty) {
      if (string.IsNullOrWhiteSpace(fileName))
        return allowEmpty ? fileName : throw new ArgumentException("File name can not be empty.");
      var invalid = Path.GetInvalidFileNameChars();
      var result = new string(fileName.Select(c => Array.IndexOf(invalid, c) >= 0 ? ' ' : c).ToArray())
        .Trim()
        .TrimEnd('.', ' ');
      return reservedNames.IsMatch(result) ? "_" + result : result;
    }

    public static string NormalizeSpaces(this string input) {
      if (string.IsNullOrEmpty(input))
        return input;
      var sb = new StringBuilder(input.Length);
      var previousIsSpace = false;
      foreach (var c in input) {
        if (c == ' ') {
          if (previousIsSpace) continue;
          sb.Append(c);
          previousIsSpace = true;
        }
        else {
          sb.Append(c);
          previousIsSpace = false;
        }
      }
      return sb.ToString();
    }

    /// <summary>
    /// Converts escape sequences that are valid in a JavaScript string literal but not in JSON.
    /// </summary>
    public static string NormalizeJsLiteral(string text) {
      if (string.IsNullOrEmpty(text))
        return text;
      var sb = new StringBuilder(text.Length);
      for (var i = 0; i < text.Length; i++) {
        var c = text[i];
        if (c != '\\' || i + 1 >= text.Length) {
          sb.Append(c);
          continue;
        }
        var next = text[++i];
        switch (next) {
          case '"':
          case '\\':
          case '/':
          case 'b':
          case 'f':
          case 'n':
          case 'r':
          case 't':
          case 'u':
            sb.Append('\\').Append(next);
            break;
          case 'x':
            if (i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2])) {
              sb.Append("\\u00").Append(text, i + 1, 2);
              i += 2;
            }
            else
              sb.Append(next);
            break;
          case 'v':
            sb.Append("\\u000b");
            break;
          case '0':
            sb.Append("\\u0000");
            break;
          default:
            sb.Append(next);
            break;
        }
      }
      return sb.ToString();
    }

    public static void SafeSetCursorPosition(int x, int y) {
      if (Console.IsOutputRedirected)
        return;
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
      catch (IOException) {
      }
    }

    public static int GetCursorTop() {
      try {
        return Console.CursorTop;
      }
      catch (IOException) {
        return 0;
      }
    }

    public static void SetCursorVisible(bool visible) {
      try {
        Console.CursorVisible = visible;
      }
      catch (IOException) {
      }
    }

    public static void SetTitle(string title) {
      try {
        Console.Title = title;
      }
      catch (IOException) {
      }
    }

    public static ConsoleKey? ReadKey() {
      return Console.IsInputRedirected ? (ConsoleKey?) null : Console.ReadKey(true).Key;
    }

    public static void WriteLine(string message = null, ConsoleColor? color = null, ConsoleColor? backColor = null) {
      Write(message, color, backColor, true);
    }

    public static void Write(string message = null, ConsoleColor? color = null, ConsoleColor? backColor = null, bool newLine = false) {
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

    public enum ImageFormatType {
      Unknown,
      Jpeg,
      Png,
      Gif,
      Webp,
      Bmp
    }

    public static ImageFormatType DetectFormat(byte[] data) {
      if (data.Length < 12)
        return ImageFormatType.Unknown;

      // JPEG: FF D8
      if (data[0] == 0xFF && data[1] == 0xD8)
        return ImageFormatType.Jpeg;

      // PNG: 89 50 4E 47
      if (data[0] == 0x89 && data[1] == 0x50 &&
          data[2] == 0x4E && data[3] == 0x47)
        return ImageFormatType.Png;

      // GIF: GIF87a / GIF89a
      if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
        return ImageFormatType.Gif;

      // BMP: 42 4D
      if (data[0] == 0x42 && data[1] == 0x4D)
        return ImageFormatType.Bmp;

      // WEBP: "RIFF....WEBP"
      if (data[0] == 'R' && data[1] == 'I' &&
          data[2] == 'F' && data[3] == 'F' &&
          data[8] == 'W' && data[9] == 'E' &&
          data[10] == 'B' && data[11] == 'P')
        return ImageFormatType.Webp;

      return ImageFormatType.Unknown;
    }

    public static string GetImageExtension(ImageFormatType format) {
      switch (format) {
        case ImageFormatType.Png: return ".png";
        case ImageFormatType.Gif: return ".gif";
        case ImageFormatType.Webp: return ".webp";
        case ImageFormatType.Bmp: return ".bmp";
        default: return ".jpg";
      }
    }

    public static byte[] ConvertWebpToJpeg(byte[] byteArray) {
      using (var stream = new MemoryStream(byteArray)) {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
        encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
        using (var outputStream = new MemoryStream()) {
          encoder.Save(outputStream);
          return outputStream.ToArray();
        }
      }
    }
  }
}
