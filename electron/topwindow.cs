using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Text;

//-----------------------------------------------------------------------------------------------------------------------
// MODIFIED DTO: Simplified to its final form.
public record DesktopScrapedElementDto(
    string? Name,
    string ControlType,
    string? ClassName,
    string? ParentName,
    int[]? RuntimeId,
    bool IsImportant
);

namespace DesktopElementInspector
{
    //-----------------------------------------------------------------------------------------------------------------------------------------------
    // Data Structures

    public class TreeNode
    {
        public DesktopScrapedElementDto Data { get; set; }
        public List<TreeNode> Children { get; set; }
        public TreeNode? Parent { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public AutomationElement? LiveElement { get; set; }

        public TreeNode(DesktopScrapedElementDto data)
        {
            Data = data;
            Children = new List<TreeNode>();
            Parent = null;
        }
    }

    public enum SemanticComponentType { Unknown, Navigation, MainContent, ToolBar, StatusBar, Dialog, Other }

    public class SemanticViewComponent
    {
        public string ComponentName { get; set; }
        public SemanticComponentType ComponentType { get; set; }
        public List<TreeNode> RootNodes { get; set; }

        public SemanticViewComponent(string name, SemanticComponentType type, List<TreeNode> nodes)
        {
            ComponentName = name;
            ComponentType = type;
            RootNodes = nodes;
        }
    }
    
    public class SemanticRule
    {
        public string ComponentName { get; }
        public Func<TreeNode, bool> Predicate { get; }
        public int Priority { get; }
        public SemanticComponentType ComponentType { get; }
        public bool IsExpensive { get; }

        public SemanticRule(string componentName, Func<TreeNode, bool> predicate, int priority, SemanticComponentType componentType, bool isExpensive = false)
        {
            ComponentName = componentName;
            Predicate = predicate;
            Priority = priority;
            ComponentType = componentType;
            IsExpensive = isExpensive;
        }
    }

    public record RuleSet(
        Dictionary<string, List<SemanticRule>> RulesByControlType,
        Dictionary<string, List<SemanticRule>> RulesByClassName,
        List<SemanticRule> OtherShallowRules,
        List<SemanticRule> ExpensiveRules,
        List<SemanticRule> AllRules
    );
    public record FilterRules(
    HashSet<string> UnimportantControlTypes,
    HashSet<string> InteractiveControlTypes,
    HashSet<string> StructuralControlTypes
);

    public class OptimizedRuleProvider
    {
        private readonly Dictionary<string, List<SemanticRule>> _rulesByControlType;
        private readonly Dictionary<string, List<SemanticRule>> _rulesByClassName;
        private readonly List<SemanticRule> _otherShallowRules;
        private readonly List<SemanticRule> _expensiveRules;

        public OptimizedRuleProvider(RuleSet ruleSet)
        {
            _rulesByControlType = ruleSet.RulesByControlType;
            _rulesByClassName = ruleSet.RulesByClassName;
            _otherShallowRules = ruleSet.OtherShallowRules;
            _expensiveRules = ruleSet.ExpensiveRules;
        }

        public SemanticRule? FindMatch(TreeNode node)
        {
            if (node.Data.ControlType != null && _rulesByControlType.TryGetValue(node.Data.ControlType, out var potentialRulesByType))
            {
                foreach (var rule in potentialRulesByType) { if (rule.Predicate(node)) return rule; }
            }
            if (node.Data.ClassName != null && _rulesByClassName.TryGetValue(node.Data.ClassName, out var potentialRulesByClass))
            {
                foreach (var rule in potentialRulesByClass) { if (rule.Predicate(node)) return rule; }
            }
            foreach (var rule in _otherShallowRules) { if (rule.Predicate(node)) return rule; }
            foreach (var rule in _expensiveRules) { if (rule.Predicate(node)) return rule; }
            return null;
        }
    }

    public static class TreeNodeExtensions
    {
        public static List<TreeNode> FindAllNodesInTree(this TreeNode startNode, Func<TreeNode, bool> predicate)
        {
            var foundNodes = new List<TreeNode>();
            var queue = new Queue<TreeNode>();
            queue.Enqueue(startNode);
            while (queue.Count > 0)
            {
                var currentNode = queue.Dequeue();
                if (predicate(currentNode)) { foundNodes.Add(currentNode); }
                foreach (var child in currentNode.Children) { queue.Enqueue(child); }
            }
            return foundNodes;
        }

        public static TreeNode? FindNodeInTree(this TreeNode currentNode, Func<TreeNode, bool> predicate)
        {
            if (predicate(currentNode)) return currentNode;
            foreach (var child in currentNode.Children)
            {
                var result = FindNodeInTree(child, predicate);
                if (result != null) return result;
            }
            return null;
        }
    }

    
    // ===================================================================
    //  Semantic Rule Providers
    // ===================================================================
    public record EditorHelpDetails(string ErrorMessage);

    public interface ISemanticRuleProvider
    {
        RuleSet GetRuleSet();
        EditorHelpDetails GetEditorHelpDetails();
        FilterRules GetFilterRules();

    }

    public class FileExplorerRuleProvider : ISemanticRuleProvider
    {
        private static readonly RuleSet _cachedRuleSet = CreateRuleSet();
        public RuleSet GetRuleSet() => _cachedRuleSet;
        public EditorHelpDetails GetEditorHelpDetails() => new EditorHelpDetails(ErrorMessage: string.Empty);

        private static readonly FilterRules _cachedFilterRules = CreateFilterRules();

        public FilterRules GetFilterRules() => _cachedFilterRules;

        private static FilterRules CreateFilterRules()
        {
            // Here we define the specific rules for File Explorer
            var unimportant = new HashSet<string> { "Image", "Separator", "Edit","Text"};

            var interactive = new HashSet<string> { "Button", "ListItem", "TreeItem", "TabItem", "ComboBox", "CheckBox", "RadioButton", "Hyperlink", "MenuItem", "EditItem", "SplitButton" };

            var structural = new HashSet<string> { "Intermediate D3D Window", "PopupHost", "StatusBar", "MenuBar", "Tree", "List", "AppBar", "Tab", "Pane", "Group", "ToolBar", "Window", "Document" };

            return new FilterRules(unimportant, interactive, structural);
        }


        private static RuleSet CreateRuleSet()
        {
            var rulesByControlType = new Dictionary<string, List<SemanticRule>>
            {
                ["TitleBar"] = new List<SemanticRule> { new SemanticRule("TitleBar", n => n.Data.ControlType == "TitleBar", 100, SemanticComponentType.ToolBar) },
                ["StatusBar"] = new List<SemanticRule> { new SemanticRule("StatusBar", n => n.Data.ControlType == "StatusBar", 80, SemanticComponentType.StatusBar) },
                ["Tree"] = new List<SemanticRule> { new SemanticRule("NavigationPane", n => n.Data.Name == "Navigation Pane" && n.Data.ControlType == "Tree", 50, SemanticComponentType.Navigation) },
                ["List"] = new List<SemanticRule> { new SemanticRule("MainContent", n => n.Data.Name == "Items View" && n.Data.ControlType == "List", 49, SemanticComponentType.MainContent) }
            };

            var rulesByClassName = new Dictionary<string, List<SemanticRule>>
            {
                ["Microsoft.UI.Xaml.Controls.TabView"] = new List<SemanticRule> { new SemanticRule("TabBar", n => n.Data.ClassName == "Microsoft.UI.Xaml.Controls.TabView", 95, SemanticComponentType.Navigation) },
                ["FileExplorerExtensions.FirstCrumbStackPanelControl"] = new List<SemanticRule> { new SemanticRule("AddressBar(rootitem)", n => n.Data.ClassName == "FileExplorerExtensions.FirstCrumbStackPanelControl", 91, SemanticComponentType.Navigation) }
            };

            var otherShallowRules = new List<SemanticRule>
            {
                new SemanticRule("PopupMenu", n => n.Data.Name != null && n.Data.Name.StartsWith("Popup"), 102, SemanticComponentType.Dialog)
            };

            var expensiveRules = new List<SemanticRule>
            {
                new SemanticRule("Window", n => n.Parent == null && n.FindNodeInTree(c => c.Data.ControlType == "TitleBar") != null, 101, SemanticComponentType.Unknown, isExpensive: true),
                new SemanticRule("NavigationToolBar", n => n.Data.ControlType == "AppBar" && n.FindNodeInTree(c => c.Data.Name == "Back") != null, 95, SemanticComponentType.ToolBar, isExpensive: true),
                new SemanticRule("BreadcrumbBar", n => n.Data.ClassName == "LandmarkTarget" && n.FindNodeInTree(c => c.Data.ClassName == "FileExplorerExtensions.BreadcrumbBarItemControl") != null, 92, SemanticComponentType.Navigation, isExpensive: true),
                new SemanticRule("AddressBar(input)", n => n.Data.ClassName == "AutoSuggestBox" && n.FindNodeInTree(c => c.Data.Name == "Address Bar") != null, 90, SemanticComponentType.Navigation, isExpensive: true),
                new SemanticRule("SearchBox", n => n.Data.ClassName == "AutoSuggestBox" && n.FindNodeInTree(c => c.Data.Name != null && c.Data.Name.Trim().StartsWith("Search")) != null, 89, SemanticComponentType.ToolBar, isExpensive: true),
                new SemanticRule("CommandBar", n => n.Data.ControlType == "AppBar" && n.FindNodeInTree(c => c.Data.Name == "Cut" || c.Data.Name == "View") != null, 85, SemanticComponentType.ToolBar, isExpensive: true),
                new SemanticRule("DetailsBar", n => n.Data.ControlType == "AppBar" && n.FindNodeInTree(c => c.Data.Name == "Details") != null, 83, SemanticComponentType.ToolBar, isExpensive: true)
            };

            var allRules = rulesByControlType.Values.SelectMany(r => r)
                .Concat(rulesByClassName.Values.SelectMany(r => r))
                .Concat(otherShallowRules)
                .Concat(expensiveRules)
                .OrderByDescending(r => r.Priority)
                .ToList();

            return new RuleSet(rulesByControlType, rulesByClassName, otherShallowRules, expensiveRules, allRules);
        }
    }

    public class VSCodeRuleProvider : ISemanticRuleProvider
    {
        private static readonly RuleSet _cachedRuleSet = CreateRuleSet();
        private static readonly FilterRules _cachedFilterRules = CreateFilterRules();
        public RuleSet GetRuleSet() => _cachedRuleSet;
        public FilterRules GetFilterRules() => _cachedFilterRules;

        public EditorHelpDetails GetEditorHelpDetails() => new EditorHelpDetails(ErrorMessage: "Could not find an editor pane that supports text extraction via UI Automation.\n" + "FIX: Ensure accessibility support is enabled in VS Code. Press Shift+Alt+F1 for help or add the following to your settings.json file:\n" + "\"editor.accessibilitySupport\": \"on\"");
        
        private static FilterRules CreateFilterRules()
        {
            // Here we define the specific rules for File Explorer
            var unimportant = new HashSet<string> { "Image", "Separator", "Group", "Text" };

            var interactive = new HashSet<string> { "Button", "ListItem", "TreeItem", "TabItem", "ComboBox", "CheckBox", "RadioButton", "Hyperlink", "MenuItem", "EditItem", "SplitButton" };

            var structural = new HashSet<string> { "Intermediate D3D Window", "PopupHost", "StatusBar", "MenuBar", "Tree", "List", "AppBar", "Tab", "Pane" , "ToolBar", "Window", "Document" };

            return new FilterRules(unimportant, interactive, structural);
        }

        private static RuleSet CreateRuleSet()
        {
            var rulesByControlType = new Dictionary<string, List<SemanticRule>>
            {
                ["Button"] = new List<SemanticRule> { new SemanticRule("WindowControls", n => n.Data.ControlType == "Button" && (n.Data.Name == "Minimize" || n.Data.Name == "Restore" || n.Data.Name == "Maximize" || n.Data.Name == "Close"), 100, SemanticComponentType.ToolBar) },
                ["ToolBar"] = new List<SemanticRule>
                {
                    new SemanticRule("LayoutControls", n => n.Data.ControlType == "ToolBar" && n.Data.Name == "Title actions", 88, SemanticComponentType.ToolBar),
                    new SemanticRule("EditorActions", n => n.Data.ControlType == "ToolBar" && n.Data.Name == "Editor actions", 72, SemanticComponentType.ToolBar)
                },
                ["StatusBar"] = new List<SemanticRule> { new SemanticRule("StatusBar", n => n.Data.ControlType == "StatusBar", 50, SemanticComponentType.StatusBar) },
                ["List"] = new List<SemanticRule> { new SemanticRule("Notifications", n => n.Data.ControlType == "List" && n.Data.Name != null && n.Data.Name.Contains("notification"), 40, SemanticComponentType.Dialog) }
            };

            var rulesByClassName = new Dictionary<string, List<SemanticRule>>();

            var otherShallowRules = new List<SemanticRule>
            {
                // new SemanticRule("Window", n => n.Parent == null, 101, SemanticComponentType.Unknown)
            };

            var expensiveRules = new List<SemanticRule>
            {
                new SemanticRule("EditorGroup", n => n.Data.ControlType == "Group" && n.FindNodeInTree(c => c.Data.ControlType == "Tab" && string.IsNullOrEmpty(c.Data.Name)) != null && n.FindNodeInTree(c => c.Data.ControlType == "Edit") != null, 70, SemanticComponentType.MainContent, isExpensive: true),
                new SemanticRule("MenuBar", n => n.Data.ControlType == "MenuBar" && n.FindNodeInTree(c => c.Data.Name == "File") != null, 95, SemanticComponentType.Navigation, isExpensive: true),
                new SemanticRule("MainToolbar", n => n.Data.ControlType == "ToolBar" && n.FindNodeInTree(c => c.Data.Name != null && c.Data.Name.StartsWith("Go Back")) != null, 90, SemanticComponentType.ToolBar, isExpensive: true),
                new SemanticRule("ActivityBar", n => n.Data.Name == "Active View Switcher" && n.Data.ControlType == "Tab" && n.FindNodeInTree(c => c.Data.Name != null && c.Data.Name.Contains("Explorer (Ctrl+Shift+E)")) != null, 85, SemanticComponentType.Navigation, isExpensive: true),
                new SemanticRule("Account-SettingsBar", n => n.Data.ControlType == "ToolBar" && n.FindNodeInTree(c => c.Data.Name == "Accounts" || c.Data.Name == "Manage") != null, 84, SemanticComponentType.ToolBar, isExpensive: true),
                new SemanticRule("SideBar", n => n.Data.ControlType == "Group" && n.Children.Any(c => c.Data.ControlType == "ToolBar" && c.Data.Name != null && c.Data.Name.EndsWith("actions")) && n.FindNodeInTree(c => c.Data.ControlType == "Tree" || c.Data.ControlType == "List") != null, 80, SemanticComponentType.Navigation, isExpensive: true),
                new SemanticRule("EditorTabs", n => n.Data.ControlType == "Tab" && n.FindNodeInTree(c => c.Data.ControlType == "TabItem") != null && n.FindNodeInTree(c => c.Data.Name == "Active View Switcher") == null, 71, SemanticComponentType.Navigation, isExpensive: true),
                new SemanticRule("Panel", n => n.Data.ControlType == "Group" && n.FindNodeInTree(c => c.Data.ControlType == "TabItem" && c.Data.Name != null && (c.Data.Name.StartsWith("Problems") || c.Data.Name.StartsWith("Terminal"))) != null && n.FindNodeInTree(c => c.Data.Name == "Maximize Panel Size" || c.Data.Name == "Hide Panel (Ctrl+J)") != null, 60, SemanticComponentType.MainContent, isExpensive: true)
            };

            var allRules = rulesByControlType.Values.SelectMany(r => r)
                .Concat(rulesByClassName.Values.SelectMany(r => r))
                .Concat(otherShallowRules)
                .Concat(expensiveRules)
                .OrderByDescending(r => r.Priority)
                .ToList();

            return new RuleSet(rulesByControlType, rulesByClassName, otherShallowRules, expensiveRules, allRules);
        }
    }

    public class DefaultRuleProvider : ISemanticRuleProvider
    {
        private static readonly RuleSet _cachedRuleSet = CreateRuleSet();
        private static readonly FilterRules _cachedFilterRules = CreateFilterRules();

        public RuleSet GetRuleSet() => _cachedRuleSet;
        public FilterRules GetFilterRules() => _cachedFilterRules;

        public EditorHelpDetails GetEditorHelpDetails() => new EditorHelpDetails(ErrorMessage: "Could not find an editor pane that supports text extraction.");

        private static FilterRules CreateFilterRules()
        {
            // Here we define the specific rules for File Explorer
            var unimportant = new HashSet<string> { "Image", "Separator"};

            var interactive = new HashSet<string> { "Button", "ListItem", "TreeItem", "TabItem", "ComboBox", "CheckBox", "RadioButton", "Hyperlink", "MenuItem", "EditItem", "SplitButton" };

            var structural = new HashSet<string> { "Intermediate D3D Window", "PopupHost", "StatusBar", "MenuBar", "Tree", "List", "AppBar", "Tab", "Pane", "Group", "ToolBar", "Window", "Text", "Document" };

            return new FilterRules(unimportant, interactive, structural);
        }
        private static RuleSet CreateRuleSet()
        {
            var rulesByControlType = new Dictionary<string, List<SemanticRule>>
            {
                ["TitleBar"] = new List<SemanticRule> { new SemanticRule("Title Bar", n => n.Data.ControlType == "TitleBar", 100, SemanticComponentType.ToolBar) }
            };
            var rulesByClassName = new Dictionary<string, List<SemanticRule>>();
            var otherShallowRules = new List<SemanticRule>
            {
                new SemanticRule("Window", n => n.Parent == null, 101, SemanticComponentType.Unknown)
            };
            var expensiveRules = new List<SemanticRule>();

            var allRules = rulesByControlType.Values.SelectMany(r => r)
                .Concat(otherShallowRules)
                .OrderByDescending(r => r.Priority)
                .ToList();

            return new RuleSet(rulesByControlType, rulesByClassName, otherShallowRules, expensiveRules, allRules);
        }
    }

    public class SemanticRuleFactory
    {
        public ISemanticRuleProvider GetProvider(AutomationElement windowElement)
        {
            string processName = "unknown";
            try
            {
                if (windowElement != null && windowElement.Properties.ProcessId.IsSupported)
                {
                    var process = Process.GetProcessById(windowElement.Properties.ProcessId.Value);
                    processName = process.ProcessName.ToLowerInvariant();
                }
            }
            catch { /* Ignore errors */ }

            switch (processName)
            {
                case "explorer": return new FileExplorerRuleProvider();
                case "code": return new VSCodeRuleProvider();
                default: return new DefaultRuleProvider();
            }
        }
    }
    
    public static class IdGenerator
    {
    private static readonly uint[] _crcTable = CreateCrc32Table();

    /// <summary>
    /// NEW: Generates a compact ID from an array of integers.
    /// </summary>
    /// <param name="data">The input int array (e.g., a FlaUI RuntimeId).</param>
    /// <returns>A short string ID (e.g., "2GV4zS").</returns>
    public static string GenerateIdFromIntArray(int[] data)
    {
        if (data == null || data.Length == 0)
        {
            return "0";
        }

        byte[] inputBytes = new byte[data.Length * sizeof(int)];
        Buffer.BlockCopy(data, 0, inputBytes, 0, inputBytes.Length);
        uint hashValue = CalculateCrc32(inputBytes);
        return EncodeToBase62(hashValue);
    }

    public static string GenerateMinimalId(string signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return "0";
        }
        byte[] inputBytes = Encoding.UTF8.GetBytes(signature);
        uint hashValue = CalculateCrc32(inputBytes);
        return EncodeToBase62(hashValue);
    }

    /// <summary>
    /// Private helper for Base62 encoding the final hash value.
    /// </summary>
    private static string EncodeToBase62(uint n)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (n == 0)
        {
            return "0";
        }
        
        var sb = new StringBuilder();
        while (n > 0)
        {
            sb.Append(alphabet[(int)(n % 62)]);
            n /= 62;
        }
        char[] arr = sb.ToString().ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }
        private static uint CalculateCrc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            crc = (crc >> 8) ^ _crcTable[b ^ (crc & 0xFF)];
        }
        return ~crc;
    }

    private static uint[] CreateCrc32Table()
    {
        const uint polynomial = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint entry = i;
            for (int j = 0; j < 8; j++)
            {
                if ((entry & 1) == 1)
                    entry = (entry >> 1) ^ polynomial;
                else
                    entry >>= 1;
            }
            table[i] = entry;
        }
        return table;
    }
    }



    // ===================================================================
    // FINAL MODIFIED SCRAPER CLASS
    // ===================================================================
    public class TopWindowScraper
    {
        private readonly UIA3Automation _automation;
        private readonly SemanticRuleFactory _ruleFactory = new();
        private List<SemanticRule> _rules = new List<SemanticRule>();
        private EditorHelpDetails _editorHelp = new("");

        public record ProcessedElementInfo(string DbId, string SummarizedName, string? name, string? ClassName, string ControlType, bool IsImportant, int IndentationLevel);

        public Dictionary<string, Dictionary<string, ProcessedElementInfo>> _componentCache = new();
        public readonly Dictionary<string, (AutomationElement? Element, string ComponentName, string Info)> _liveElementCache = new();
        // NEW: Cache for unimportant elements for debugging
        public readonly List<DesktopScrapedElementDto> _unimportantCache = new();

        public TopWindowScraper(UIA3Automation automation)
        {
            _automation = automation;
        }

        public async Task AnalyzeSemantically()
        {
            IntPtr initialHandle = IntPtr.Zero;
            initialHandle = NativeMethods.GetForegroundWindow();
            var windowElement = _automation.FromHandle(initialHandle);
            if (windowElement == null) return;

            var stopwatch = Stopwatch.StartNew();

            TreeNode? semanticTreeRoot;
            try
            {
                IntPtr handleBefore = NativeMethods.GetForegroundWindow();
                if (handleBefore != windowElement.Properties.NativeWindowHandle.ValueOrDefault)
                {
                    Console.WriteLine("VALIDATION FAILED: Focus changed before scrape could start.");
                    return;
                }

                semanticTreeRoot = await Task.Run(() => Scrape(windowElement));

                stopwatch.Stop();
                Console.WriteLine($"--- Time to scrape: {stopwatch.ElapsedMilliseconds} ms ---");

                IntPtr handleAfter = NativeMethods.GetForegroundWindow();
                if (handleBefore != handleAfter)
                {
                    Console.WriteLine("VALIDATION FAILED: Window focus changed *during* the scrape. Data is unreliable.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VALIDATION FAILED: Scrape was interrupted by an internal error: {ex.Message}");
                return;
            }

            if (semanticTreeRoot == null)
            {
                Console.WriteLine("VALIDATION FAILED: Scrape completed but returned no elements.");
                return;
            }

            var stopwatche = Stopwatch.StartNew();

            var ruleProvider = _ruleFactory.GetProvider(windowElement);
            _editorHelp = ruleProvider.GetEditorHelpDetails();

            var nodeToRuleMap = ClassifyNodes(semanticTreeRoot, ruleProvider);
            stopwatche.Stop();
            Console.WriteLine($"--- Time to classify: {stopwatche.ElapsedMilliseconds} ms ---");

            var stopwatch2 = Stopwatch.StartNew();

            FinalizeAndCacheComponentsOptimized(semanticTreeRoot, nodeToRuleMap, ruleProvider);

            stopwatch2.Stop();
            Console.WriteLine($"--- Time to group: {stopwatch2.ElapsedMilliseconds} ms ---");
        }

        public string? GetComponentNameBySignature(string? signature)
        {
            if (signature == null) return null;
            if (_liveElementCache.TryGetValue(signature, out var info))
            {
                return $"{info.ComponentName}:{signature}:{info.Info}";
            }
            return null;
        }

        public void PrintSemanticView()
        {
            Console.WriteLine("\n--- Semantic Window Summary ---");
            if (_componentCache.Count == 0)
            {
                Console.WriteLine("No components were found or analyzed.");
                return;
            }

            var ruleLookup = _rules.ToDictionary(r => r.ComponentName);
            var componentNames = _componentCache.Keys.ToList();

            var toolAndStatusComponents = componentNames.Where(name =>
                ruleLookup.TryGetValue(name, out var rule) &&
                (rule.ComponentType == SemanticComponentType.ToolBar || rule.ComponentType == SemanticComponentType.StatusBar)
            ).ToList();
            var otherComponents = componentNames.Except(toolAndStatusComponents).ToList();

            var priorityLookup = _rules.ToDictionary(r => r.ComponentName, r => r.Priority);

            if (toolAndStatusComponents.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n\n--- Infrastructure Components ---");
                Console.ResetColor();
                foreach (var componentName in toolAndStatusComponents.OrderByDescending(name => priorityLookup.GetValueOrDefault(name, 0)))
                {
                    PrintComponentDetails(componentName);
                }
            }

            if (otherComponents.Any())
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n\n--- Content & Navigation Components ---");
                Console.ResetColor();
                foreach (var componentName in otherComponents.OrderByDescending(name => priorityLookup.GetValueOrDefault(name, 0)))
                {
                    PrintComponentDetails(componentName);
                }
            }
        }

        private void PrintComponentDetails(string componentName)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n## {componentName}");
            Console.ResetColor();
            Console.WriteLine("------------------------------------");

            if (_componentCache.TryGetValue(componentName, out var componentElements))
            {
                bool printedSomething = false;
                foreach (var info in componentElements.Values.Where(i => i.IsImportant))
                {
                    string indent = new string(' ', info.IndentationLevel * 2);
                    Console.WriteLine($"{indent}{info.DbId}:{info.SummarizedName}");
                    printedSomething = true;
                }

                if (!printedSomething && componentName != "Editor Group")
                {
                    Console.WriteLine("(No important elements found in this component)");
                }

                // Special logic for editor text extraction
                if (componentName == "Editor Group")
                {
                    bool editorFoundAndPrinted = false;
                    var potentialEditorElements = componentElements.Values.Where(info => info.ControlType == "Edit");

                    foreach (var editorInfo in potentialEditorElements)
                    {
                        // *** THIS IS THE FIX ***
                        // We get the signature directly from the DbId stored in ProcessedElementInfo.
                        // No need to access a non-existent OriginalDto.
                        string signature = editorInfo.DbId.Substring(editorInfo.DbId.IndexOf(':') + 1);

                        if (_liveElementCache.TryGetValue(signature, out var cacheEntry))
                        {
                            var editorElement = cacheEntry.Element;
                            if (editorElement != null && editorElement.Patterns.Text.IsSupported)
                            {
                                var textPattern = editorElement.Patterns.Text.Pattern;
                                string extractedText = textPattern.DocumentRange.GetText(-1).Trim();
                                if (!string.IsNullOrEmpty(extractedText))
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine("--- Contained Code (via TextPattern) ---");
                                    Console.ResetColor();
                                    Console.WriteLine(extractedText);
                                    editorFoundAndPrinted = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (!editorFoundAndPrinted && !string.IsNullOrEmpty(_editorHelp.ErrorMessage))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n--- Code Extraction Failed ---");
                        Console.WriteLine(_editorHelp.ErrorMessage);
                        Console.ResetColor();
                    }
                }
            }
        }

        // NEW: Function to print the unimportant elements for debugging.
        public void PrintUnimportantElementsForDebugging()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n\n--- Unimportant Elements (For Debugging) ---");
            Console.WriteLine("==============================================");
            if (_unimportantCache.Count == 0)
            {
                Console.WriteLine("No unimportant elements were found or filtered out.");
            }
            else
            {
                foreach (var dto in _unimportantCache.OrderBy(d => d.ControlType).ThenBy(d => d.Name))
                {
                    string name = string.IsNullOrEmpty(dto.Name) ? "(no name)" : dto.Name;
                    Console.WriteLine($"- Type: {dto.ControlType,-15} | Name: {name,-40} | Class: {dto.ClassName ?? "(no class)"}");
                }
            }
            Console.WriteLine("==============================================");
            Console.ResetColor();
        }

        private Dictionary<TreeNode, SemanticRule?> ClassifyNodes(TreeNode windowNode, ISemanticRuleProvider ruleProvider)
        {
            var ruleSet = ruleProvider.GetRuleSet();
            _rules = ruleSet.AllRules;
            var optimizedRules = new OptimizedRuleProvider(ruleSet);
            var nodeToRuleMap = new Dictionary<TreeNode, SemanticRule?>();
            ClassifyNodeRecursive(windowNode, null, nodeToRuleMap, optimizedRules);
            return nodeToRuleMap;
        }

        private void ClassifyNodeRecursive(TreeNode node, SemanticRule? parentRule, Dictionary<TreeNode, SemanticRule?> nodeToRuleMap, OptimizedRuleProvider rules)
        {
            var currentRule = rules.FindMatch(node);
            var effectiveRule = currentRule ?? parentRule;
            nodeToRuleMap[node] = effectiveRule;
            foreach (var child in node.Children)
            {
                ClassifyNodeRecursive(child, effectiveRule, nodeToRuleMap, rules);
            }
        }

        private void FinalizeAndCacheComponentsOptimized(TreeNode windowNode, Dictionary<TreeNode, SemanticRule?> nodeToRuleMap, ISemanticRuleProvider ruleProvider)
        {
            _componentCache.Clear();
            _liveElementCache.Clear();
            _unimportantCache.Clear(); // Clear the debug cache
            ProcessAndCacheNodeRecursive(windowNode, -1, "Window", nodeToRuleMap, ruleProvider);
        }

        private void ProcessAndCacheNodeRecursive(TreeNode node, int parentComponentIndent, string parentSummarizedName, Dictionary<TreeNode, SemanticRule?> nodeToRuleMap, ISemanticRuleProvider ruleProvider)
        {
            // if (node.LiveElement == null || !node.LiveElement.IsAvailable) return;

            //This is the correct way to handle structural vs important nodes
            if (!node.Data.IsImportant)
            {
                // NEW: Add the unimportant DTO to our debug cache.
                _unimportantCache.Add(node.Data);

                // For unimportant nodes, pass the parent's indent level down without incrementing
                foreach (var child in node.Children)
                {
                    ProcessAndCacheNodeRecursive(child, parentComponentIndent, parentSummarizedName, nodeToRuleMap, ruleProvider);
                }
                return;
            }

            // --- Logic for IMPORTANT nodes starts here ---
            var nodeRule = nodeToRuleMap.GetValueOrDefault(node);
            var parentRule = (node.Parent != null) ? nodeToRuleMap.GetValueOrDefault(node.Parent) : null;
            string componentName = nodeRule?.ComponentName ?? "Other Controls";

            // Recalculate the correct indentation level for this node
            int currentIndent = (nodeRule != parentRule) ? 0 : parentComponentIndent + 1;

            string summarizedName = GetMeaningfulName(node.Data);

            summarizedName = $"{summarizedName} ({node.Data.ControlType})";

            int[]? runtimeId = node.Data.RuntimeId;
            string signature = "NULL";
            if (runtimeId != null)
            {
                signature = IdGenerator.GenerateIdFromIntArray(runtimeId);
            }
            string dbId = $"{componentName}:{signature}";

            var processedInfo = new ProcessedElementInfo(
                DbId: dbId,
                SummarizedName: summarizedName,
                name: node.Data.Name,
                ClassName: node.Data.ClassName,
                ControlType: node.Data.ControlType,
                IsImportant: true,
                IndentationLevel: currentIndent
            );

            if (!_componentCache.ContainsKey(componentName))
            {
                _componentCache[componentName] = new Dictionary<string, ProcessedElementInfo>();
            }
            _componentCache[componentName][dbId] = processedInfo;

            _liveElementCache[signature] = (node.LiveElement, componentName, summarizedName);

            foreach (var child in node.Children)
            {
                // Pass the NEW currentIndent to the children
                ProcessAndCacheNodeRecursive(child, currentIndent, summarizedName, nodeToRuleMap, ruleProvider);
            }
        }


        private string GetMeaningfulName(DesktopScrapedElementDto element)
        {
            // This function provides the best possible fallback name for an element.
            // It follows a clear priority: Name > ClassName > ParentName.
            string? name = element.Name;
            if (!string.IsNullOrEmpty(name) && name != "[Not Supported]")
            {
                return element.Name!;
            }

            if (!string.IsNullOrEmpty(element.ClassName))
            {
                return element.ClassName!;
            }

            if (!string.IsNullOrEmpty(element.ParentName))
            {
                return element.ParentName;
            }

            return "Unnamed";
        }

        public TreeNode? Scrape(AutomationElement elementToScrape)
        {
            if (elementToScrape == null) return null;

            var ruleProvider = _ruleFactory.GetProvider(elementToScrape);

            var filterRules = ruleProvider.GetFilterRules();


            var rootDto = CreateDtoFromElement(elementToScrape, null, filterRules);
            if (rootDto == null)
            {
                // This should not happen with the final logic, but it is a safeguard.
                return null;
            }

            var rootNode = new TreeNode(rootDto) { LiveElement = elementToScrape };
            var queue = new Queue<(AutomationElement element, TreeNode parentNode)>();
            queue.Enqueue((elementToScrape, rootNode));

            var walker = _automation.TreeWalkerFactory.GetRawViewWalker();

            while (queue.Count > 0)
            {
                var (currentElement, parentTreeNode) = queue.Dequeue();
                var childElement = walker.GetFirstChild(currentElement);
                while (childElement != null)
                {
                    try
                    {
                        var dto = CreateDtoFromElement(childElement, parentTreeNode, filterRules);
                        // In this final architecture, the dto is never null.

                        var childNode = new TreeNode(dto) { LiveElement = childElement, Parent = parentTreeNode };
                        parentTreeNode.Children.Add(childNode);
                        queue.Enqueue((childElement, childNode));
                    }
                    catch (Exception ex)
                    {
                        LogScrapeException(ex, childElement);
                    }
                    childElement = walker.GetNextSibling(childElement);
                }
            }
            return rootNode;
        }

        private bool HasMeaningfulContent(string? name, string? className)
        {
            if (!string.IsNullOrEmpty(name) && name != "[Not Supported]")
            {

                return name.Any(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c));
            }
            if (!string.IsNullOrEmpty(className))
            {
                return true;
            }
            return false;
        }
        private DesktopScrapedElementDto CreateDtoFromElement(AutomationElement element, TreeNode? parentNode, FilterRules rules)
        {
            var controlType = element.Properties.ControlType.ValueOrDefault.ToString();

            // Handle universally irrelevant types by marking them as unimportant
            if (rules.UnimportantControlTypes.Contains(controlType))
            {
                return new DesktopScrapedElementDto(
                    Name: "Not important", ControlType: controlType,
                    ClassName: "Not important", ParentName: parentNode?.Data.Name,
                    RuntimeId: default, IsImportant: false);
            }

            var name = element.Properties.Name.ValueOrDefault;
            var className = element.Properties.ClassName.ValueOrDefault;

            // Handle generic interactive types by checking their name

            if (rules.InteractiveControlTypes.Contains(controlType))
            {
                if (HasMeaningfulContent(name, className))
                {
                    return new DesktopScrapedElementDto(
                        Name: name, ControlType: controlType,
                       ClassName: className, ParentName: parentNode?.Data.Name,
                       RuntimeId: element.Properties.RuntimeId.ValueOrDefault,
                       IsImportant: true);
                }
                else
                {
                    // Unnamed interactive item becomes an unimportant structural node
                    return new DesktopScrapedElementDto(
                       Name: name, ControlType: controlType, ClassName: className, ParentName: parentNode?.Data.Name,
                       RuntimeId: default, IsImportant: false);
                }
            }

            if (rules.StructuralControlTypes.Contains(controlType) || rules.StructuralControlTypes.Contains(className ?? string.Empty))
            {
                if (HasMeaningfulContent(name, className))
                {
                    return new DesktopScrapedElementDto(
                       Name: name, ControlType: controlType,
                       ClassName: className, ParentName: parentNode?.Data.Name,
                       RuntimeId: default,
                       IsImportant: true);
                }
                else
                {
                    // Unnamed interactive item becomes an unimportant structural node
                    return new DesktopScrapedElementDto(
                       Name: name, ControlType: controlType, ClassName: className, ParentName: parentNode?.Data.Name,
                       RuntimeId: default, IsImportant: false);
                }
            }
            return new DesktopScrapedElementDto(
                        Name: name, ControlType: controlType, ClassName: className, ParentName: parentNode?.Data.Name,
                        RuntimeId: default, IsImportant: false);
        }

        private void LogScrapeException(Exception ex, AutomationElement element)
        {
            string elementName = "Unknown (element was null)";
            string elementAutomationId = "Unknown";
            if (element != null)
            {
                try { elementName = element.Name ?? "Unknown Name"; } catch { /* ignore */ }
                try { elementAutomationId = element.AutomationId ?? "Unknown AutomationId"; } catch { /* ignore */ }
            }
            System.Diagnostics.Debug.WriteLine("--------------------------------------------------");
            System.Diagnostics.Debug.WriteLine($"An error occurred while processing an element during the scrape.");
            System.Diagnostics.Debug.WriteLine($"Element Details: Name='{elementName}', AutomationId='{elementAutomationId}'");
            System.Diagnostics.Debug.WriteLine($"Error Message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine("--------------------------------------------------");
        }
    }
}