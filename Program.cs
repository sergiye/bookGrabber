using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace bookGrabber {

    internal class Program {

        private static object gate = new object();

        static async Task Main(string[] args) {

            var errors = new Dictionary<string, Exception>();
            try {
                Console.Clear();

                var url = args.Length < 1 ? null : args[0];

                if (string.IsNullOrEmpty(url)) {
                    Write("Enter book url: ");
                    url = Console.ReadLine();
                }
                
                if (string.IsNullOrWhiteSpace(url))
                    throw new Exception("Book url can not be empty");

                Write("Retrieving book content... ");
                var content = (await GetContent(url)).TrimEnd();
                WriteLine($"\tDone!", ConsoleColor.Green);

                var title = string.Empty;
                var pTitle = Regex.Matches(content, @"<meta property=""og:title"" content=""([^>]+)>");
                if (pTitle.Count != 0 && pTitle[0].Groups.Count > 1) {
                    title = GetValidFileName(pTitle[0].Groups[1].Value, true);
                    Console.Title = $"Downloading: '{title}'";
                }

                var subDir = args.Length < 2 ? null : args[1];
                if (string.IsNullOrEmpty(subDir)) {
                    Write("Press 'Enter' to use '");
                    Write($"{title}", ConsoleColor.Yellow);
                    Write("' as subdir, or enter new one: ");
                    var value = Console.ReadLine();
                    subDir = string.IsNullOrWhiteSpace(value) ? title : GetValidFileName(value, true);
                }

                var coll = Regex.Matches(content, @"new BookPlayer\([\d]+,\s(\[[^\[]+\]),\s\[");
                if (coll.Count == 0 || coll[0].Groups.Count < 2)
                    throw new Exception("No tracks found");
                
                var jsonData = Regex.Unescape(coll[0].Groups[1].Value);

                var tracks = jsonData.FromJson<TrackInfo[]>();
                if (tracks == null || tracks.Length == 0)
                    throw new Exception("Error getting list of tracks");

                Console.CursorVisible = false;
                var asm = Assembly.GetExecutingAssembly();
                var outPath = Path.GetDirectoryName(asm.Location);
                if (!string.IsNullOrEmpty(subDir)) {
                    outPath = Path.Combine(outPath, subDir);
                    Directory.CreateDirectory(outPath);
                }
                Write("Target folder: ");
                WriteLine(outPath, ConsoleColor.DarkCyan);

                var done = 0;
                var failed = 0;
                ShowProgress(tracks.Length, done, failed);

                var tasks = Enumerable.Range(0, tracks.Length)
                    .Select(async (i) => {
                        var track = tracks[i];
                        var fileName = GetValidFileName(track.title, false);
                        if (Path.GetExtension(fileName) != ".mp3")
                            fileName += ".mp3";
                        try {
                            var outputPath = Path.Combine(outPath, fileName);
                            using (var wc = new WebClient())
                                await wc.DownloadFileTaskAsync(track.url, outputPath);
                            Interlocked.Increment(ref done);
                        }
                        catch (Exception ex) {
                            Interlocked.Increment(ref failed);
                            errors[track.url] = ex;
                        }
                        finally {
                            ShowProgress(tracks.Length, done, failed);
                        }
                    }).ToArray();
                await Task.WhenAll(tasks);

                Console.SetCursorPosition(0, 7);
                WriteLine("Finished", ConsoleColor.DarkCyan);
            }
            catch (Exception ex) {
                Console.SetCursorPosition(0, 7);
                WriteLine(ex.Message, ConsoleColor.Red);
            }

            Console.CursorVisible = true;
            Console.SetCursorPosition(0, 9);

            if (errors.Count > 0) {
                WriteLine($"Errors summary:");
                foreach (var p in errors) {
                    Write($" - Error loading {p.Key}: ", ConsoleColor.Red);
                    WriteLine($"{p.Value.Message}", ConsoleColor.Yellow);
                }
            }

            WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static void ShowProgress(int count, int done, int failed) {
            lock (gate) {
                Console.SetCursorPosition(0, 5);
                Write("Downloading ", ConsoleColor.White);
                Write($"{count}", ConsoleColor.Cyan);
                Write(" tracks, done: ", ConsoleColor.White);
                Write($"{done}", ConsoleColor.Green);
                if (failed > 0) {
                    Write($", failed: ", ConsoleColor.White);
                    Write($"{failed}", ConsoleColor.Red);
                }
                Write(", completed: ", ConsoleColor.White);
                Write($"{(done + failed) * 100 / count}%", ConsoleColor.Yellow);
            }
        }

        private static async Task<string> GetContent(string uri, int timeout = 10, string method = "GET") {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.Timeout = timeout * 1000;
            request.UserAgent =
              "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.500.27 Safari/537.36";
            //request.Accept = "text/xml,text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.ContentType = "application/json; charset=utf-8";
            using (var webResp = await request.GetResponseAsync()) {
                using (var stream = webResp.GetResponseStream()) {
                    if (stream == null) return null;
                    var answer = new StreamReader(stream, Encoding.UTF8);
                    var result = answer.ReadToEnd();
                    return result;
                }
            }
        }

        private static string GetValidFileName(string fileName, bool allowEmpty) {
            if (string.IsNullOrWhiteSpace(fileName) && !allowEmpty)
                throw new ArgumentException("File name can not be empty.");
            foreach (char c in Path.GetInvalidFileNameChars()) {
                fileName = fileName.Replace(c, ' ').Trim();
            }
            return fileName;
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
