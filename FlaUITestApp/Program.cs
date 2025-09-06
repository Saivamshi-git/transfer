using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

// --- ScrapedElement ---
public class ScrapedElement
{
    public string? Name { get; set; }
    public string ControlType { get; set; }
    public string? ClassName { get; set; }
    public Rectangle BoundingRectangle { get; set; }
    public AutomationElement AutomationElement { get; set; }

    public ScrapedElement(string? name, string controlType, string? className, Rectangle rect, AutomationElement elem)
    {
        Name = name;
        ControlType = controlType;
        ClassName = className;
        BoundingRectangle = rect;
        AutomationElement = elem;
    }

    public override string ToString()
    {
        return $"[{ControlType}] \"{Name ?? ""}\" (Class={ClassName ?? ""})";
    }
}

// --- Tree Node ---
public class TreeNode
{
    public ScrapedElement Data { get; set; }
    public List<TreeNode> Children { get; set; } = new();

    public TreeNode(ScrapedElement data)
    {
        Data = data;
    }
}

// --- Scraper ---
public class Scraper
{
    private readonly UIA3Automation _automation;

    public Scraper()
    {
        _automation = new UIA3Automation();
    }

    public TreeNode BuildTree(AutomationElement root)
    {
        var rootElement = new ScrapedElement(
            root.Properties.Name.ValueOrDefault,
            root.Properties.ControlType.ValueOrDefault.ToString(),
            root.Properties.ClassName.ValueOrDefault,
            root.Properties.BoundingRectangle.ValueOrDefault,
            root
        );

        var rootNode = new TreeNode(rootElement);
        FillChildren(rootNode, root);
        return rootNode;
    }

    private void FillChildren(TreeNode node, AutomationElement element)
    {
        var walker = _automation.TreeWalkerFactory.GetRawViewWalker();
        var child = walker.GetFirstChild(element);
        while (child != null)
        {
            try
            {
                var childElement = new ScrapedElement(
                    child.Properties.Name.ValueOrDefault,
                    child.Properties.ControlType.ValueOrDefault.ToString(),
                    child.Properties.ClassName.ValueOrDefault,
                    child.Properties.BoundingRectangle.ValueOrDefault,
                    child
                );

                var childNode = new TreeNode(childElement);
                node.Children.Add(childNode);

                // Recursively fill children
                FillChildren(childNode, child);
            }
            catch { /* Ignore disappearing elements */ }

            child = walker.GetNextSibling(child);
        }
    }
}

// --- Container Collector ---
public static class ContainerCollector
{
    private static readonly HashSet<string> ContainerTypes = new() { "Pane", "ToolBar", "AppBar", "MenuBar", "Tab" };

    public static Dictionary<string, List<ScrapedElement>> Collect(TreeNode root)
    {
        var map = new Dictionary<string, List<ScrapedElement>>();

        void Traverse(TreeNode node, string? currentKey)
        {
            if (ContainerTypes.Contains(node.Data.ControlType))
            {
                string keyName = node.Data.Name ?? node.Data.ClassName ?? node.Data.ControlType;
                currentKey = $"{node.Data.ControlType}: {keyName}";
                if (!map.ContainsKey(currentKey))
                    map[currentKey] = new List<ScrapedElement>();
            }
            else if (currentKey != null)
            {
                map[currentKey].Add(node.Data);
            }

            foreach (var child in node.Children)
                Traverse(child, currentKey);
        }

        // Root window as key
        string rootKey = $"Window: {root.Data.Name ?? root.Data.ClassName}";
        map[rootKey] = new List<ScrapedElement>();
        foreach (var child in root.Children)
            Traverse(child, rootKey);

        return map;
    }

    public static void PrintDictionary(Dictionary<string, List<ScrapedElement>> map)
    {
        foreach (var kvp in map)
        {
            Console.WriteLine($"\n--- {kvp.Key} ---");
            foreach (var item in kvp.Value)
                Console.WriteLine(item);
        }
    }
}

// --- Program ---
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

        IntPtr handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine("No foreground window found.");
            return;
        }

        using var automation = new UIA3Automation();
        var rootElement = automation.FromHandle(handle);

        Console.WriteLine($"--- Target window: {rootElement.Name} ---");

        var scraper = new Scraper();
        var sw = Stopwatch.StartNew();
        var treeRoot = scraper.BuildTree(rootElement);
        sw.Stop();
        Console.WriteLine($"Scraping completed in {sw.ElapsedMilliseconds} ms");

        var containerMap = ContainerCollector.Collect(treeRoot);
        ContainerCollector.PrintDictionary(containerMap);

        Console.WriteLine("\nDone.");
    }
}
