using System;
using sergiye.Common;

namespace bookGrabber {

  internal sealed class ProgressView {

    private readonly object gate = new object();
    private readonly int count;
    private readonly string title;

    public int Top { get; }

    public ProgressView(int count, string title) {
      this.count = count;
      this.title = title;
      Top = Utils.GetCursorTop();
    }

    public void Start() {
      lock (gate) {
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Normal);
        Update(0, 0);
      }
    }

    public void Finish() {
      lock (gate) {
        TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.NoProgress);
      }
    }

    public void Update(int done, int failed) {
      lock (gate) {
        var percents = count == 0 ? 100 : (done + failed) * 100 / count;
        if (Console.IsOutputRedirected) {
          Console.WriteLine($"Downloading {count} tracks, done: {done}, failed: {failed}, completed: {percents}%");
        }
        else {
          Utils.SafeSetCursorPosition(0, Top + 1);
          Utils.Write("Downloading ", ConsoleColor.White);
          Utils.Write($"{count}", ConsoleColor.Cyan);
          Utils.Write(" tracks, done: ", ConsoleColor.White);
          Utils.Write($"{done}", ConsoleColor.Green);
          if (failed > 0) {
            Utils.Write(", failed: ", ConsoleColor.White);
            Utils.Write($"{failed}", ConsoleColor.Red);
          }
          Utils.Write(", completed: ", ConsoleColor.White);
          Utils.Write($"{percents}%", ConsoleColor.Yellow);
        }
        Utils.SetTitle($"{percents}% {title}");
        if (failed > 0)
          TaskbarProgressHelper.SetState(TaskbarProgressHelper.TaskbarStates.Error);
        TaskbarProgressHelper.SetValue(done, count);
      }
    }
  }
}
