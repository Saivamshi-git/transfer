using System;
using FlaUI.UIA3;
using System.Threading;
using FlaUI.Core.AutomationElements;
using System.Diagnostics;


namespace DesktopElementInspector
{

    public static class Program
    {


        public static void PrintTree(TreeNode? node, int indentLevel = 0)
        {
            if (node == null) return;

            // Indentation for better visual hierarchy
            string indent = new string(' ', indentLevel * 2);

            // Print current node in the format {Name : ControlType}
            Console.WriteLine($"{indent}{{ {node.Data.Name} : {node.Data.ControlType} }}");

            // Recursively print children
            foreach (var child in node.Children)
            {
                PrintTree(child, indentLevel + 1);
            }
        }

        // private static void PrintRawTreeRecursive(TreeNode node, int depth)
        // {
        //     // Create an indent string based on the current depth in the tree.
        //     string indent = new string(' ', depth * 2);

        //     // Format the output string for the current node, showing its raw data.
        //     var nodeData = node.Data;
        //     string output = $"{indent}{{ DbId: \"{nodeData.DbId ?? "null"}\", Name: \"{nodeData.Name}\", ControlType: \"{nodeData.ControlType}\", ClassName: \"{nodeData.ClassName}\" }}";

        //     Console.WriteLine(output);

        //     // Recursively call this method for all children of the current node.
        //     foreach (var child in node.Children)
        //     {
        //         PrintRawTreeRecursive(child, depth + 1);
        //     }
        // }
        public static async Task Main(string[] args)
        {
            Console.WriteLine("--- Desktop Inspector ---");
            Console.WriteLine("1. Scan Top-Most Application Window (Full Detail)");
            Console.WriteLine("2. Scan Taskbar (Interactive Elements Only)");
            Console.WriteLine("3. test taskbar (Interactive Elements Only)");
            Console.WriteLine("---------------------------------------------");

            // UIA3Automation is the main entry point for FlaUI.
            // It's best to create it once and reuse it.
            using var automation = new UIA3Automation();
            var topWindowScraper = new TopWindowScraper(automation);


            Console.Write("\nEnter option (1 or 2) and press Enter: ");
            string? userInput = Console.ReadLine();

            try
            {

                if (userInput == "1")
                {
                    Console.WriteLine("\nScanning Desktop for recursive elements...");
                    Console.WriteLine("=======================================================================");
                    Thread.Sleep(5000);
                    Console.WriteLine("==================waiting is done===================");

                    Console.WriteLine("\n\n=======================================================================");
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("--- Raw Scrape Output ---");
                    Console.ResetColor();
                    Console.WriteLine("=======================================================================");
                    // var windowElement = await topWindowScraper.WaitForInitialIdleAsync();

                    IntPtr initialHandle = IntPtr.Zero;
                    initialHandle = NativeMethods.GetForegroundWindow();
                    var windowElement = automation.FromHandle(initialHandle);

                    if (windowElement == null)
                    {
                        Console.WriteLine("(Scrape returned no elements.)");
                        return;
                    }
                    // var rawScrapeRoot = topWindowScraper.Scrape(windowElement);
                    // if (rawScrapeRoot == null)
                    // {
                    //     Console.WriteLine("(Scrape returned no elements.)");
                    //     return;
                    // }
                    // var stopwatch = new System.Diagnostics.Stopwatch();
                    // stopwatch.Start();
                    // var rootNode = topWindowScraper.Scrape(windowElement);
                    // stopwatch.Stop();
                    // Console.WriteLine($"--- Time to analyze and print view: {stopwatch.ElapsedMilliseconds} ms ---");
                    // PrintTree(rootNode);

                    //----------------------------------------------------------------------------------------------------------------------------------------------------
                    
                    await topWindowScraper.AnalyzeSemantically();
                    
                    //topWindowScraper.PrintUnimportantElementsForDebugging();
                    //---------------------------------------------------------------------------------------------------------------------------------------------------

                }

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.ResetColor();
            }
        }



    }
}

