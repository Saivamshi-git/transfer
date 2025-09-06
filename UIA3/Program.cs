using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

// DTO to hold the scraped data. Lightweight and serializable.
public class DesktopScrapedElementDto
{
    public string? Name { get; set; }
    public string ControlType { get; set; }
    public string? ClassName { get; set; }

    public System.Drawing.Rectangle BoundingRectangle { get; set; }

    public DesktopScrapedElementDto(string? name, string controlType, string? className, System.Drawing.Rectangle BoundingRectangle)
    {
        Name = name;
        ControlType = controlType;
        ClassName = className;
        BoundingRectangle = BoundingRectangle;
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

// // --- 1. Original single-threaded scraper ---
// public class WindowScraper
// {
//     private readonly UIA3Automation _automation;

//     public WindowScraper()
//     {
//         _automation = new UIA3Automation();
//     }

//     public TreeNode? Scrape(AutomationElement elementToScrape)
//     {
//         if (elementToScrape == null) return null;

//         var walker = _automation.TreeWalkerFactory.GetRawViewWalker();
//         var rootDto = CreateDtoFromElement(elementToScrape);
//         var rootNode = new TreeNode(rootDto);

//         var queue = new Queue<(AutomationElement element, TreeNode parentNode)>();
//         queue.Enqueue((elementToScrape, rootNode));

//         while (queue.Count > 0)
//         {
//             var (currentElement, parentTreeNode) = queue.Dequeue();

//             try
//             {
//                 var childElement = walker.GetFirstChild(currentElement);
//                 while (childElement != null)
//                 {
//                     var dto = CreateDtoFromElement(childElement);
//                     var childNode = new TreeNode(dto);
//                     parentTreeNode.Children.Add(childNode);

//                     queue.Enqueue((childElement, childNode));
//                     childElement = walker.GetNextSibling(childElement);
//                 }
//             }
//             catch (Exception ex)
//             {
//                 LogScrapeException(ex, currentElement);
//             }
//         }
//         return rootNode;
//     }

//     private DesktopScrapedElementDto CreateDtoFromElement(AutomationElement element)
//     {
//         string? name = element.Properties.Name.ValueOrDefault;
//         string controlType = element.Properties.ControlType.ValueOrDefault.ToString();
//         string? className = element.Properties.ClassName.ValueOrDefault;
//         return new DesktopScrapedElementDto(name, controlType, className);
//     }

//     private void LogScrapeException(Exception ex, AutomationElement element)
//     {
//         Console.WriteLine($"[ERROR] Element={element?.Properties.Name?.ValueOrDefault ?? "Unknown"} : {ex.Message}");
//     }
// }

// // --- 2. Advanced multi-threaded scraper ---
// public class AdvancedScraper
// {
//     private ConcurrentBag<DesktopScrapedElementDto> _results;
//     private readonly UIA3Automation _automation;

//     public AdvancedScraper()
//     {
//         _results = new ConcurrentBag<DesktopScrapedElementDto>();
//         _automation = new UIA3Automation();
//     }

//     public List<DesktopScrapedElementDto> Scrape(AutomationElement root)
//     {
//         if (root == null) return new List<DesktopScrapedElementDto>();

//         var workQueue = new ConcurrentQueue<AutomationElement>();
//         workQueue.Enqueue(root);

//         int tasksOutstanding = 1;
//         var workerTasks = new List<Task>();

//         for (int i = 0; i < 4; i++)
//         {
//             workerTasks.Add(Task.Run(() => WorkerLoop(workQueue, ref tasksOutstanding)));
//         }

//         Task.WhenAll(workerTasks).Wait();

//         return _results.ToList();
//     }

//     private void WorkerLoop(ConcurrentQueue<AutomationElement> queue, ref int tasksOutstanding)
//     {
//         var walker = _automation.TreeWalkerFactory.GetRawViewWalker();

//         while (Volatile.Read(ref tasksOutstanding) > 0)
//         {
//             if (queue.TryDequeue(out var currentElement))
//             {
//                 try
//                 {
//                     var dto = CreateDtoFromElement(currentElement);
//                     _results.Add(dto);

//                     var childrenToQueue = new List<AutomationElement>();
//                     AutomationElement? child = walker.GetFirstChild(currentElement);
//                     while (child != null)
//                     {
//                         childrenToQueue.Add(child);
//                         child = walker.GetNextSibling(child);
//                     }

//                     if (childrenToQueue.Count > 0)
//                     {
//                         Interlocked.Add(ref tasksOutstanding, childrenToQueue.Count);
//                         foreach (var c in childrenToQueue)
//                         {
//                             queue.Enqueue(c);
//                         }
//                     }
//                 }
//                 catch { }
//                 finally
//                 {
//                     Interlocked.Decrement(ref tasksOutstanding);
//                 }
//             }
//             else
//             {
//                 Thread.Sleep(1);
//             }
//         }
//     }

//     private DesktopScrapedElementDto CreateDtoFromElement(AutomationElement element)
//     {
//         string? name = element.Properties.Name.ValueOrDefault;
//         string controlType = element.Properties.ControlType.ValueOrDefault.ToString();
//         string? className = element.Properties.ClassName.ValueOrDefault;
//         return new DesktopScrapedElementDto(name, controlType, className);
//     }
// }



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
        System.Drawing.Rectangle BoundingRectangle = element.Properties.BoundingRectangle.ValueOrDefault;

        return new DesktopScrapedElementDto(name, controlType, className,BoundingRectangle);
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

        // // --- Run 1: Standard Scraper ---
        // Console.WriteLine("--- Running Standard Scraper (Single-Threaded) ---");
        // var standardScraper = new WindowScraper();
        // var sw = Stopwatch.StartNew();
        // var root = standardScraper.Scrape(windowElement);
        // sw.Stop();
        // Console.WriteLine(root != null
        //     ? $"[RESULT] Completed in {sw.ElapsedMilliseconds} ms | Found {root.CountNodes()} elements"
        //     : "Standard scraping failed.");

        // // --- Run 2: Advanced Scraper ---
        // Console.WriteLine("\n--- Running Advanced Scraper (Multi-Threaded) ---");
        // var advancedScraper = new AdvancedScraper();
        // var swAdvanced = Stopwatch.StartNew();
        // var results = advancedScraper.Scrape(windowElement);
        // swAdvanced.Stop();
        // Console.WriteLine(results.Count > 0
        //     ? $"[RESULT] Completed in {swAdvanced.ElapsedMilliseconds} ms | Found {results.Count} elements"
        //     : "Advanced scraping failed.");

        // --- Run 3: Optimized Scraper ---
        Console.WriteLine("\n--- Running Optimized Scraper (Multi-Threaded) ---");
        var optimizedScraper = new OptimizedScraper();
        var swOptimized = Stopwatch.StartNew();
        var optimizedResults = optimizedScraper.Scrape(windowElement);
        swOptimized.Stop();
        Console.WriteLine(optimizedResults.Count > 0
            ? $"[RESULT] Completed in {swOptimized.ElapsedMilliseconds} ms | Found {optimizedResults.Count} elements"
            : "Optimized scraping failed.");


        Console.WriteLine("\nDone. Press any key to exit...");
        Console.ReadKey();
    }
}
