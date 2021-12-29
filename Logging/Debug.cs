using System.Collections.Concurrent;
using System.Text;

internal static class Debug
{
    #region general

    private static readonly string SessionStartTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

    /// <summary>
    /// Initialize Debug functionality
    /// </summary>
    public static void Init()
    {
        InitLogging();
        InitInput();
        LogNoFormat(string.Empty);
        LogNoFormat("    -------------------------    New Session at [" + SessionStartTime + "]    -------------------------");
        LogNoFormat(string.Empty);

        Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
        {
            Log("Closing Debug library...");
            Close();
            e.Cancel = false;
        };
    }

    /// <summary>
    /// Close Debug functionality
    /// </summary>
    public static void Close()
    {
        CloseLogging();
        CloseInput();
    }

    #endregion

    #region logging

    private static readonly CancellationTokenSource LoggingCancellationTokenSource = new();
    private static CancellationToken _loggingCancellationToken;
    private static WaitHandle? _loggingWaitHandle;
    private static Task? _loggingTask;

    private static readonly List<Tuple<string, StreamWriter>> LogWriters = new();
    private static readonly HashSet<string> LogFilePaths = new();

    private static readonly ConsoleColor InfoConsoleColor = Console.ForegroundColor;
    private const ConsoleColor WarningConsoleColor = ConsoleColor.DarkYellow;
    private const ConsoleColor ErrorConsoleColor = ConsoleColor.DarkRed;
    private enum LogSeverity { Ignore, Info, Warning, Error }

    private static readonly ConcurrentQueue<Tuple<LogSeverity, string>> LoggingQueue = new();

    private const string DateFormat = "yyyy/MM/dd HH:mm:ss";

    private static void InitLogging()
    {
        _loggingCancellationToken = LoggingCancellationTokenSource.Token;
        _loggingWaitHandle = _loggingCancellationToken.WaitHandle;
        _loggingTask = new Task(LoggingLoop, _loggingCancellationToken);
        _loggingTask.Start();
    }

    private static void CloseLogging()
    {
        if (!LoggingCancellationTokenSource.IsCancellationRequested) LoggingCancellationTokenSource.Cancel();
        _loggingWaitHandle?.WaitOne();
        LoggingCancellationTokenSource.Dispose();
        DumpOutput();
        UnregisterAllOutputFiles();
    }

    /// <summary>
    /// Registers a new file to output logs into [DOES MOSTLY NOT NEED DEBUG INITIALIZATION]
    /// </summary>
    /// <param name="path">the path to the file - the file will be created if it doesn't already exist</param>
    /// <param name="overwriteContent">if the file contents should be overwritten on each new session</param>
    /// /// <param name="logRegister">If the process of adding this file sould be logged [DEBUG INITIALIZATION NECESSARY]</param>
    public static void RegisterOutputFile(string path, bool overwriteContent = false, bool logRegister = false)
    {
        lock (LogFilePaths)
        {
            if (!path.EndsWith(".log")) path += ".log";
            if (LogFilePaths.Contains(path)) return;
            if (!File.Exists(path)) using (var fw = File.Create(path)) fw.Close();

            StreamWriter sw = new(path, !overwriteContent);
            sw.AutoFlush = true;
            LogWriters.Add(new Tuple<string, StreamWriter>(path, sw));

            LogFilePaths.Add(path);

            sw.Write(Environment.NewLine);
            sw.Write("    ----------    Joined Session from [" + SessionStartTime + "] at [" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "]    ----------");
            sw.Write(Environment.NewLine);

            if (logRegister) Log("Registered new log output file at: \"" + path + "\"");
        }
    }

    /// <summary>
    /// Unregisters a registered output file [DOES MOSTLY NOT NEED DEBUG INITIALIZATION]
    /// </summary>
    /// <param name="path">the path to the file</param>
    /// <param name="logUnregister">If the process of removing this file sould be logged [DEBUG INITIALIZATION NECESSARY]</param>
    public static void UnregisterOutputFile(string path, bool logUnregister = false)
    {
        lock (LogFilePaths)
        {
            if (!LogFilePaths.Contains(path)) return;
            LogFilePaths.Remove(path);
            foreach (var item in LogWriters.Where(tuple => tuple.Item1.Equals(path)))
            {
                item.Item2.Close();
                item.Item2.Dispose();
                LogWriters.Remove(item);
            }
            if (logUnregister) Log("Unregistered log output file at: \"" + path + "\"");
        }
    }

    /// <summary>
    /// Unregisters all output files [DOES NOT NEED DEBUG INITIALIZATION]
    /// </summary>
    public static void UnregisterAllOutputFiles()
    {
        lock (LogFilePaths)
        {
            foreach (var (_, sw) in LogWriters) { sw.Close(); sw.Dispose(); }
            LogWriters.Clear();
            LogFilePaths.Clear();
        }
    }

    private static void LoggingLoop()
    {
        while (!_loggingCancellationToken.IsCancellationRequested)
        {
            DumpOutput();
        }
    }

    private static readonly object DumpLock = new();
    private static void DumpOutput()
    {
        lock (DumpLock)
        {
            while (!LoggingQueue.IsEmpty)
            {
                if (LoggingQueue.TryDequeue(out var logMessage))
                    Log(logMessage.Item1, logMessage.Item2);
            }
        }
    }

    /// <summary>
    /// Log with no formatting (without date and severity level)
    /// </summary>
    /// <param name="message">the string to log</param>
    public static void LogNoFormat(string message)
    {
        LoggingQueue.Enqueue(
            new Tuple<LogSeverity, string>(
                LogSeverity.Ignore,
                message));
    }

    /// <summary>
    /// Log a message
    /// </summary>
    /// <param name="message">The message to log</param>
    public static void Log(string message)
    {
        LoggingQueue.Enqueue(
            new Tuple<LogSeverity, string>(
                LogSeverity.Info,
                message));
    }

    /// <summary>
    /// Log a message and mark it as a warning
    /// </summary>
    /// <param name="message">The message to log</param>
    public static void LogWarning(string message)
    {
        LoggingQueue.Enqueue(
            new Tuple<LogSeverity, string>(
                LogSeverity.Warning,
                message));
    }

    /// <summary>
    /// Log a message and mark it as an error
    /// </summary>
    /// <param name="message">The message to log</param>
    public static void LogError(string message)
    {
        LoggingQueue.Enqueue(
            new Tuple<LogSeverity, string>(
                LogSeverity.Error,
                message));
    }

    private static void Log(LogSeverity severity, string inputMessage)
    {
        StringBuilder messageBuilder = new(inputMessage.Length
            + (severity == LogSeverity.Ignore ? 0 : DateFormat.Length + 2 + 12));

        if (severity != LogSeverity.Ignore)
        {
            var date = new StringBuilder("[", DateFormat.Length + 2).Append(DateTime.Now.ToString(DateFormat)).Append(']').ToString();
            Console.Write(date);
            lock (LogFilePaths) Parallel.ForEach(LogWriters, tuple => tuple.Item2.Write(date));
        }

        var cc = Console.ForegroundColor;

        switch (severity)
        {
            case LogSeverity.Info:
                messageBuilder.Insert(0, " [INFO]     ");
                Console.ForegroundColor = InfoConsoleColor;
                break;
            case LogSeverity.Warning:
                messageBuilder.Insert(0, " [WARNING]  ");
                Console.ForegroundColor = WarningConsoleColor;
                break;
            case LogSeverity.Error:
                messageBuilder.Insert(0, " [ERROR]    ");
                Console.ForegroundColor = ErrorConsoleColor;
                break;
        }

        var message = messageBuilder.Append(inputMessage).Append(Environment.NewLine).ToString();
        Console.Write(message);
        Console.ForegroundColor = cc;
        lock (LogFilePaths) Parallel.ForEach(LogWriters, tuple => tuple.Item2.Write(message));
    }

    #endregion

    #region input

    private static readonly CancellationTokenSource InputCancellationTokenSource = new();
    private static CancellationToken _inputCancellationToken;
    private static WaitHandle? _inputWaitHandle;
    private static Task? _inputTask;

    private static readonly ConcurrentQueue<string> InputQueue = new();

    private static void InitInput()
    {
        _inputCancellationToken = InputCancellationTokenSource.Token;
        _inputWaitHandle = _inputCancellationToken.WaitHandle;
        _inputTask = new Task(InputLoop, _inputCancellationToken);
        _inputTask.Start();
    }

    private static void CloseInput()
    {
        if (!InputCancellationTokenSource.IsCancellationRequested) InputCancellationTokenSource.Cancel();
        _inputWaitHandle?.WaitOne();
        InputCancellationTokenSource.Dispose();
    }

    private static void InputLoop()
    {
        while (!_inputCancellationToken.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line != null) InputQueue.Enqueue(line);
        }
    }

    /// <summary>
    /// If there is a input line available to read
    /// </summary>
    /// <returns>true, if there is input available</returns>
    public static bool InputAvailable() => !InputQueue.IsEmpty;

    /// <summary>
    /// Get a input line (from FIFO Buffer)
    /// </summary>
    /// <returns>The next line from the FIFO Buffer if it is available. If not null will be returned</returns>
    public static string? GetInput()
    {
        if(!InputQueue.IsEmpty && InputQueue.TryDequeue(out var line)) return line;
        return null;
    }

    /// <summary>
    /// Prompts the User to press enter and waits until they have
    /// </summary>
    public static void Prompt_PressEnterToContinue()
    {
        LogNoFormat("Press enter to continue...");
        WaitForInput();
    }

    /// <summary>
    /// Waits for the user to press enter (if anythin was typed it will be available to read via Debug.GetInput())
    /// </summary>
    public static void WaitForInput()
    {
        var queueSize = InputQueue.Count;
        while (queueSize >= InputQueue.Count) if (queueSize > InputQueue.Count) queueSize = InputQueue.Count;
    }

    #endregion
}