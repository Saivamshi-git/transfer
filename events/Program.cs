using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;   
using System;
using FlaUI.Core;

public class DesktopScrapedElementDto
{
    public string? Name { get; set; }
    public string ControlType { get; set; }
    public string? ClassName { get; set; }

    public DesktopScrapedElementDto(string? name, string controlType, string? className)
    {
        Name = name;
        ControlType = controlType;
        ClassName = className;
    }

    public override string ToString()
    {
        return $"[{ControlType}] \"{Name ?? ""}\" (Class={ClassName ?? ""})";
    }
}

// Represents a node in the scraped UI tree for visualization.
public class TreeNode
{
    public DesktopScrapedElementDto Data { get; set; }
    public List<TreeNode> Children { get; set; } = new();

    public TreeNode(DesktopScrapedElementDto dto)
    {
        Data = dto;
    }

    /// Recursive pretty-printer
    public void Print(string indent = "", bool isLast = true)
    {
        Console.Write(indent);
        Console.Write(isLast ? "└─" : "├─");
        Console.WriteLine(Data);

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Print(indent + (isLast ? "   " : "│  "), i == Children.Count - 1);
        }
    }

    /// Count nodes recursively
    public int CountNodes()
    {
        return 1 + Children.Sum(child => child.CountNodes());
    }
}


public class UiEventHandler : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly AutomationElement _rootElement;

    private StructureChangedEventHandlerBase? _structureChangedHandler;
    private PropertyChangedEventHandlerBase? _propertyChangedHandler;

    private readonly object _bufferLock = new();
    private readonly HashSet<string> _eventBuffer = new();

    private readonly Timer _flushTimer;
    private readonly Stopwatch _sinceLastEvent = new Stopwatch();

    private const int CheckIntervalMs = 30; // timer tick interval
    private const int MinIdleMs = 50;       // minimum idle period to flush

    public UiEventHandler(UIA3Automation automation, AutomationElement rootElement)
    {
        _automation = automation;
        _rootElement = rootElement;

        // Timer checks buffer every CheckIntervalMs
        _flushTimer = new Timer(OnFlushTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Console.WriteLine("--- Starting Event Listeners ---");

        _structureChangedHandler = _rootElement.RegisterStructureChangedEvent(
            TreeScope.Descendants,
            OnStructureChanged);

        _propertyChangedHandler = _rootElement.RegisterPropertyChangedEvent(
            TreeScope.Descendants,
            OnPropertyChanged,
            _automation.PropertyLibrary.Element.Name,
            _automation.PropertyLibrary.Element.IsEnabled,
            _automation.PropertyLibrary.Element.IsOffscreen
            
        );

        // start the flush timer
        _flushTimer.Change(CheckIntervalMs, CheckIntervalMs);
    }

    private void OnStructureChanged(AutomationElement sender, StructureChangeType changeType, int[] runtimeId)
    {
        string key = $"STRUCT:{changeType}:{sender?.Properties.Name.ValueOrDefault}";
        BufferEvent(key);
    }

    private void OnPropertyChanged(AutomationElement sender, PropertyId propertyId, object newValue)
    {
        string key = $"PROP:{propertyId.Name}:{sender?.Properties.Name.ValueOrDefault}:{newValue}";
        BufferEvent(key);
    }

    private void BufferEvent(string eventKey)
    {
        lock (_bufferLock)
        {
            _eventBuffer.Add(eventKey);
            _sinceLastEvent.Restart();
        }
    }

    private void OnFlushTimerElapsed(object? state)
    { 
        HashSet<string>? toFlush = null;
        long idleMs;

        lock (_bufferLock)
        {
            if (_eventBuffer.Count == 0) return;

            idleMs = _sinceLastEvent.ElapsedMilliseconds;
            if (idleMs < MinIdleMs) return; // still within active period

            toFlush = new HashSet<string>(_eventBuffer);
            _eventBuffer.Clear();
            _sinceLastEvent.Reset();
        }

        Console.WriteLine($"\n[Buffered Events Flushed after {idleMs} ms idle]:");
        foreach (var ev in toFlush)
            Console.WriteLine(ev);
    }

    public void Dispose()
    {
        Console.WriteLine("\n--- Stopping Event Listeners ---");
        _structureChangedHandler?.Dispose();
        _propertyChangedHandler?.Dispose();
        _flushTimer.Dispose();
    }
}



// --- 3. Optimized scraper (BlockingCollection) ---
public class OptimizedScraper
{
    private readonly UIA3Automation _automation;

    public OptimizedScraper()
    {
        _automation = new UIA3Automation();
    }

    public List<DesktopScrapedElementDto> Scrape(AutomationElement root)
    {
        if (root == null) return new List<DesktopScrapedElementDto>();

        var results = new ConcurrentBag<DesktopScrapedElementDto>();
        using var workQueue = new BlockingCollection<AutomationElement>(new ConcurrentQueue<AutomationElement>());

        int itemsToProcess = 1;
        workQueue.Add(root);

        var workerTasks = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            workerTasks.Add(Task.Run(() =>
            {
                var rawWalker = _automation.TreeWalkerFactory.GetRawViewWalker();

                foreach (var currentElement in workQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        results.Add(CreateDtoFromElement(currentElement));

                        var childrenToQueue = new List<AutomationElement>();
                        AutomationElement? child = rawWalker.GetFirstChild(currentElement);
                        while (child != null)
                        {
                            childrenToQueue.Add(child);
                            child = rawWalker.GetNextSibling(child);
                        }

                        if (childrenToQueue.Count > 0)
                        {
                            Interlocked.Add(ref itemsToProcess, childrenToQueue.Count);
                            foreach (var c in childrenToQueue)
                            {
                                workQueue.Add(c);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        if (Interlocked.Decrement(ref itemsToProcess) == 0)
                        {
                            workQueue.CompleteAdding();
                        }
                    }
                }
            }));
        }

        Task.WhenAll(workerTasks).Wait();

        return results.ToList();
    }

    private DesktopScrapedElementDto CreateDtoFromElement(AutomationElement element)
    {
        string? name = element.Properties.Name.ValueOrDefault;
        string controlType = element.Properties.ControlType.ValueOrDefault.ToString();
        string? className = element.Properties.ClassName.ValueOrDefault;
        return new DesktopScrapedElementDto(name, controlType, className);
    }
}


// --- Program entry point ---
public class Program
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }

   public static void Main()
{
    Console.WriteLine("Switch focus to the window you want to scrape in the next 5 seconds...");
    Thread.Sleep(5000);

    IntPtr windowHandle = NativeMethods.GetForegroundWindow();
    if (windowHandle == IntPtr.Zero)
    {
        Console.WriteLine("Could not find a foreground window.");
        return;
    }

    using var automation = new UIA3Automation();
    var windowElement = automation.FromHandle(windowHandle);

    Console.WriteLine($"--- Target window: {windowElement.Name} ---\n");

    // --- Run 3: Optimized Scraper ---
    Console.WriteLine("\n--- Running Optimized Scraper (Multi-Threaded) ---");
    var optimizedScraper = new OptimizedScraper();
    var swOptimized = Stopwatch.StartNew();
    var optimizedResults = optimizedScraper.Scrape(windowElement);
    swOptimized.Stop();
    Console.WriteLine(optimizedResults.Count > 0
        ? $"[RESULT] Completed in {swOptimized.ElapsedMilliseconds} ms | Found {optimizedResults.Count} elements"
        : "Optimized scraping failed.");

    // ✅ Start listening for UI changes *after scraping*
    using (var handler = new UiEventHandler(automation, windowElement))
    {
        handler.Start();

        Console.WriteLine("\nEvent listeners are now active.");
        Console.WriteLine("Interact with the target window to see real-time UI changes...");
        Console.WriteLine("Press any key to stop listening and exit.\n");

        Console.ReadKey();
    }

    Console.WriteLine("\nDone. Exiting program...");
}

}

