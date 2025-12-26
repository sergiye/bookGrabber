using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace bookGrabber {
  
  internal static class Utils {
    
    private static HttpClient httpClient;
    private const int BufferSize = 80 * 1024; //80KB

    static Utils() {
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
    }

    public static async Task<string> GetContent(string uri, CancellationToken token = default) {
      using (var response = await httpClient.GetAsync(uri, token)) {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
      }
    }

    public static async Task DownloadFile(string url, string outputPath, int maxRetries = 1, CancellationToken cancellationToken = default) {
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

    public static string GetValidFileName(string fileName, bool allowEmpty) {
      if (string.IsNullOrWhiteSpace(fileName))
        return allowEmpty ? fileName : throw new ArgumentException("File name can not be empty.");
      fileName = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, ' ').Trim());
      return fileName.TrimEnd('.');
    }

    public static void SafeSetCursorPosition(int x, int y) {
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
  }
}