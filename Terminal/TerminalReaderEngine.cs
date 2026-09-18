using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MUTHUR6000.Terminal
{
    public class TerminalReaderEngine : IDisposable
    {
        private readonly List<string> _allFiles = new List<string>();
        private readonly List<string> _unplayedFiles = new List<string>();
        private string? _lastPlayedFile = null;
        private bool _isBootPhase = true;

        private CancellationTokenSource? _cts;
        private Task? _playbackTask;
        private readonly object _lock = new object();

        private int _baudRate = 1200;
        private double _holdDelaySeconds = 3.0;
        private bool _isPaused = false;
        private bool _isDisposed = false;

        // Current active state
        public string? CurrentFileName { get; private set; }
        public int BaudRate => _baudRate;
        public bool IsPaused => _isPaused;
        public int TotalFilesCount => _allFiles.Count;

        // Events
        public event Action<string>? FileStarted;
        public event Action<string>? CharactersEmitted;
        public event Action<string>? FileCompleted;
        public event Action? ScreenClearRequested;
        public event Action<string>? StatusChanged;
        public event Action<bool>? PauseStateChanged;

        public TerminalReaderEngine(int initialBaudRate = 1200, double holdDelaySeconds = 3.0)
        {
            _baudRate = initialBaudRate > 0 ? initialBaudRate : 1200;
            _holdDelaySeconds = Math.Max(0.5, holdDelaySeconds);

            RefreshFileList();
        }

        public void RefreshFileList()
        {
            lock (_lock)
            {
                _allFiles.Clear();
                _unplayedFiles.Clear();

                string? targetDir = FindTextDirectory();
                if (targetDir != null && Directory.Exists(targetDir))
                {
                    var files = Directory.GetFiles(targetDir, "*.txt", SearchOption.TopDirectoryOnly)
                                         .OrderBy(f => Path.GetFileName(f))
                                         .ToList();
                    _allFiles.AddRange(files);
                }

                _isBootPhase = true;
                InitializeUnplayedQueue();
            }
        }

        private static string? FindTextDirectory()
        {
            // 1. Check directory containing the running executable on disk
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                string? exeDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string candidate1 = Path.Combine(exeDir, "text");
                    if (Directory.Exists(candidate1)) return candidate1;
                }
            }

            // 2. Check AppDomain BaseDirectory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                string candidate2 = Path.Combine(baseDir, "text");
                if (Directory.Exists(candidate2)) return candidate2;
            }

            // 3. Check current working directory
            string cwd = Directory.GetCurrentDirectory();
            string candidate3 = Path.Combine(cwd, "text");
            if (Directory.Exists(candidate3)) return candidate3;

            return null;
        }

        private void InitializeUnplayedQueue()
        {
            _unplayedFiles.Clear();

            if (_allFiles.Count == 0)
            {
                return;
            }

            // If boot phase and a BOOT_ file exists, ensure it is queued first
            if (_isBootPhase)
            {
                var bootFile = _allFiles.FirstOrDefault(f =>
                    Path.GetFileName(f).StartsWith("BOOT_", StringComparison.OrdinalIgnoreCase));

                if (bootFile != null)
                {
                    _unplayedFiles.Add(bootFile);
                    var remaining = _allFiles.Where(f => !string.Equals(f, bootFile, StringComparison.OrdinalIgnoreCase)).ToList();
                    ShuffleList(remaining);
                    _unplayedFiles.AddRange(remaining);
                    return;
                }
            }

            // Standard shuffle cycle
            var allShuffled = new List<string>(_allFiles);
            ShuffleList(allShuffled);

            // Avoid repeating the exact last played file as the first item if more than 1 file exists
            if (_lastPlayedFile != null && allShuffled.Count > 1 && allShuffled[0] == _lastPlayedFile)
            {
                string temp = allShuffled[0];
                allShuffled[0] = allShuffled[^1];
                allShuffled[^1] = temp;
            }

            _unplayedFiles.AddRange(allShuffled);
        }

        private static void ShuffleList<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Random.Shared.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                StopInternal();

                _cts = new CancellationTokenSource();
                _playbackTask = Task.Run(() => PlaybackLoopAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
            _cts = null;
        }

        public void SetBaudRate(int baudRate)
        {
            if (baudRate > 0)
            {
                _baudRate = baudRate;
                StatusChanged?.Invoke($"BAUD RATE SET TO {_baudRate}");
            }
        }

        public void SetHoldDelay(double seconds)
        {
            _holdDelaySeconds = Math.Max(0.5, seconds);
        }

        public void TogglePause()
        {
            SetPaused(!_isPaused);
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            PauseStateChanged?.Invoke(_isPaused);
            StatusChanged?.Invoke(_isPaused ? "TRANSMISSION PAUSED" : "TRANSMISSION ACTIVE");
        }

        public void SkipNext()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                Start();
            }
        }

        public void RestartBoot()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isBootPhase = true;
                _lastPlayedFile = null;
                InitializeUnplayedQueue();
                Start();
            }
        }

        private async Task PlaybackLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_isDisposed)
            {
                string? nextFile = null;

                lock (_lock)
                {
                    if (_unplayedFiles.Count == 0)
                    {
                        _isBootPhase = false;
                        InitializeUnplayedQueue();
                    }

                    if (_unplayedFiles.Count > 0)
                    {
                        nextFile = _unplayedFiles[0];
                        _unplayedFiles.RemoveAt(0);
                    }
                }

                if (nextFile == null || !File.Exists(nextFile))
                {
                    StatusChanged?.Invoke("NO TELEMETRY FILES FOUND IN ./TEXT");
                    try
                    {
                        await Task.Delay(2000, ct);
                    }
                    catch { break; }
                    RefreshFileList();
                    continue;
                }

                CurrentFileName = Path.GetFileName(nextFile);
                _lastPlayedFile = nextFile;

                // Notify file started
                FileStarted?.Invoke(CurrentFileName);

                string content;
                try
                {
                    content = await File.ReadAllTextAsync(nextFile, ct);
                }
                catch (Exception ex)
                {
                    CharactersEmitted?.Invoke($"\n[ERROR READING FILE: {ex.Message}]\n");
                    content = string.Empty;
                }

                // Type out the file character-by-character based on baud rate
                await StreamTextContentAsync(content, ct);

                if (ct.IsCancellationRequested) break;

                // Notify file complete
                FileCompleted?.Invoke(CurrentFileName);

                // Hold for the specified end-of-file hold duration
                double holdSeconds = _holdDelaySeconds;
                var holdStopwatch = Stopwatch.StartNew();
                while (holdStopwatch.Elapsed.TotalSeconds < holdSeconds && !ct.IsCancellationRequested)
                {
                    await Task.Delay(50, ct);
                }

                if (ct.IsCancellationRequested) break;

                // Request screen clear before next file
                ScreenClearRequested?.Invoke();

                // Small pause after clear before next file starts typing
                await Task.Delay(250, ct);
            }
        }

        private async Task StreamTextContentAsync(string content, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(content)) return;

            int totalChars = content.Length;
            int currentIndex = 0;
            var sw = Stopwatch.StartNew();
            double lastTimestamp = sw.Elapsed.TotalSeconds;
            double charFractionAccumulator = 0.0;

            // Frame slice duration (aiming for smooth ~15ms - 25ms batch updates)
            const int TickIntervalMs = 16;

            while (currentIndex < totalChars && !ct.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    await Task.Delay(50, ct);
                    lastTimestamp = sw.Elapsed.TotalSeconds;
                    continue;
                }

                double currentTimestamp = sw.Elapsed.TotalSeconds;
                double dt = currentTimestamp - lastTimestamp;
                lastTimestamp = currentTimestamp;

                // Clamp dt to avoid huge bursts on UI thread freezes / system resume
                if (dt > 1.0) dt = 0.05;

                double charsPerSecond = Math.Max(10.0, _baudRate / 10.0);
                charFractionAccumulator += dt * charsPerSecond;

                int countToEmit = (int)charFractionAccumulator;
                if (countToEmit > 0)
                {
                    charFractionAccumulator -= countToEmit;
                    int actualCount = Math.Min(countToEmit, totalChars - currentIndex);
                    if (actualCount > 0)
                    {
                        string chunk = content.Substring(currentIndex, actualCount);
                        currentIndex += actualCount;
                        CharactersEmitted?.Invoke(chunk);
                    }
                }

                try
                {
                    await Task.Delay(TickIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                StopInternal();
            }
        }
    }
}

