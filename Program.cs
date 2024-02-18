using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bookGrabber {

    internal class Program {

        private static object gate = new object();

        static async Task Main(string[] args) {

            try {
                var url = args.Length < 1 ? null : args[0];
                var subDir = args.Length < 2 ? null : args[1];

                if (string.IsNullOrEmpty(url)) {
                    Write("Enter book url: ", ConsoleColor.White);
                    url = Console.ReadLine();
                }
                if (string.IsNullOrEmpty(subDir)) {
                    Write("Enter book subDir: ", ConsoleColor.White);
                    subDir = Console.ReadLine();
                }
                
                if (string.IsNullOrWhiteSpace(url))
                    throw new Exception("Book url can not be empty");
                subDir = subDir.Trim();

                WriteLine("Retrieving book content... ");
                var content = (await GetContent(url)).TrimEnd();

                WriteLine("Parsing book content... ");
                var coll = Regex.Matches(content, @"new BookPlayer\([\d]+,\s(\[[^\[]+\]),\s\[");
                if (coll.Count == 0 || coll[0].Groups.Count < 2)
                    throw new Exception("No tracks found");
                
                var jsonData = Regex.Unescape(coll[0].Groups[1].Value);

                var tracks = jsonData.FromJson<TrackInfo[]>();
                if (tracks == null || tracks.Length == 0)
                    throw new Exception("Error getting list of tracks");
                
                Write("Found ");
                Write($"{tracks.Length}", ConsoleColor.Green);
                WriteLine(" tracks, downloading...", ConsoleColor.White);
                var asm = Assembly.GetExecutingAssembly();
                var outPath = Path.GetDirectoryName(asm.Location);
                if (!string.IsNullOrEmpty(subDir)) {
                    outPath = Path.Combine(outPath, subDir);
                    Directory.CreateDirectory(outPath);
                }

                //foreach (var track in tracks) {
                //    var fileName = GetValidFileName(track.title);
                //    try {
                //        Write($"Downloading: {fileName}");
                //        var outputPath = Path.Combine(outPath, fileName);

                //        using (var wc = new WebClient())
                //            wc.DownloadFile(track.url, outputPath);
                //        WriteLine("\t Done!", ConsoleColor.Green);
                //    }
                //    catch (Exception ex) {
                //        WriteLine($"\nError getting file from {track.url}: {ex.Message}", ConsoleColor.Yellow);
                //    }
                //}

                var tasks = Enumerable.Range(0, tracks.Length)
                    .Select(async (i) => {
                        var track = tracks[i];
                        var fileName = GetValidFileName(track.title);
                        try {
                            var outputPath = Path.Combine(outPath, fileName);
                            using (var wc = new WebClient())
                                await wc.DownloadFileTaskAsync(track.url, outputPath);
                            lock (gate) {
                                Write($"{fileName}");
                                WriteLine("\tDone!", ConsoleColor.Green);
                            }
                        }
                        catch (Exception ex) {
                            lock (gate) {
                                Write($"\nError getting file from {track.url}: ", ConsoleColor.Red);
                                WriteLine(ex.Message, ConsoleColor.Yellow);
                            }
                        }
                    }).ToArray();
                await Task.WhenAll(tasks);

                WriteLine("Finished");
            }
            catch (Exception ex) {
                WriteLine(ex.Message, ConsoleColor.Red);
            }

            WriteLine("Press any key to exit...");
            Console.ReadKey();
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

        private static string GetValidFileName(string fileName) {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name can not be empty.");
            foreach (char c in Path.GetInvalidFileNameChars()) {
                fileName = fileName.Replace(c, '_');
            }
            if (Path.GetExtension(fileName) != ".mp3")
                fileName += ".mp3";
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
