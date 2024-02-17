using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace bookGrabber {

    internal class Program {

        static void Main(string[] args) {

            try {

                if (args.Length < 1)
                    throw new Exception("Please provide book url");

                var url = args[0];
                var subDir = args.Length > 1 ? args[1] : string.Empty;

                Console.WriteLine("Retrieving book content... ");
                var content = GetContent(url).TrimEnd();

                Console.WriteLine("Parsing book content... ");
                var coll = Regex.Matches(content, @"new BookPlayer\([\d]+,\s(\[[^\[]+\]),\s\[");
                if (coll.Count == 0 || coll[0].Groups.Count < 2)
                    throw new Exception("No tracks found");
                
                var jsonData = Regex.Unescape(coll[0].Groups[1].Value);

                var tracks = jsonData.FromJson<TrackInfo[]>();
                if (tracks == null || tracks.Length == 0)
                    throw new Exception("Error getting list of tracks");
                
                Console.WriteLine($"Found {tracks.Length} tracks, downloading...");
                var asm = Assembly.GetExecutingAssembly();
                var outPath = Path.GetDirectoryName(asm.Location);
                if (!string.IsNullOrEmpty(subDir))
                    outPath = Path.Combine(outPath, subDir);

                foreach (var track in tracks) {
                    var fileName = GetValidFileName(track.title);
                    try {
                        Console.Write($"Downloading: {fileName}");
                        var outputPath = Path.Combine(outPath, fileName);

                        using (var wc = new WebClient())
                            wc.DownloadFile(track.url, outputPath);
                        Console.WriteLine("\t Done!");
                    }
                    catch (Exception ex) {
                        Console.WriteLine($"\nDownload error: {fileName} from {track.url}");
                    }
                }

                Console.WriteLine("Finished");
            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static string GetContent(string uri, int timeout = 10, string method = "GET") {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.Timeout = timeout * 1000;
            request.UserAgent =
              "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.500.27 Safari/537.36";
            //request.Accept = "text/xml,text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.ContentType = "application/json; charset=utf-8";
            using (var webResp = request.GetResponse()) {
                using (var stream = webResp.GetResponseStream()) {
                    if (stream == null) return null;
                    var answer = new StreamReader(stream, Encoding.UTF8);
                    var result = answer.ReadToEnd();
                    return result;
                }
            }
        }

        private static string GetValidFileName(string fileName) {
            foreach (char c in Path.GetInvalidFileNameChars()) {
                fileName = fileName.Replace(c, '_');
            }
            if (Path.GetExtension(fileName) != ".mp3")
                fileName += ".mp3";
            return fileName;
        }
    }
}
