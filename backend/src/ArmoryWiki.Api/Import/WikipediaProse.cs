using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ArmoryWiki.Api.Import;

/// <summary>An article's prose as the importer stores it (Markdown, PLAN.md Phase 4c).</summary>
public sealed record ArticleProse(string? Summary, string? DevelopmentHistory, string? ServiceHistory);

/// <summary>
/// Turns a Wikipedia article's Parsoid HTML into Markdown, keeping only paragraphs, headings, lists, links, bold and
/// italic; tables, infoboxes, references, images, galleries, hatnotes and other templates are dropped (Q12: our own
/// small converter rather than a general one that would carry over whatever the article contains). Pure: no I/O.
/// </summary>
public static partial class WikipediaProse
{
    public const int SummaryMaxLength = 500; // Firearm.Summary column

    // Checked first: "Service history" and "Operational history" are about service, not development.
    private static readonly string[] ServiceWords = ["service", "operational", "combat", "usage", "users", "deployment"];
    private static readonly string[] DevelopmentWords = ["history", "development", "design", "background", "origins", "origin"];

    // Dropped with everything inside them.
    private static readonly HashSet<string> Dropped =
    [
        "head", "table", "figure", "figcaption", "style", "script", "div", "img", "link", "meta", "dl", "blockquote",
        "math", "audio", "video", "gallery", "noscript", "svg", "map",
    ];

    private static readonly HashSet<string> Void =
        ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"];

    /// <summary>
    /// The lead's first paragraph (plain text, at most <see cref="SummaryMaxLength"/> characters, cut at a sentence),
    /// then the History/Development/Design sections and the Service/Combat/Users sections as Markdown, each under
    /// its own heading. Null where the article has nothing.
    /// </summary>
    public static ArticleProse Convert(string html)
    {
        var blocks = new List<Block>();
        Blocks(Parse(html), blocks);

        string? summary = null;
        var development = new StringBuilder();
        var service = new StringBuilder();
        StringBuilder? target = null;
        var headings = new List<Block>(); // written only once some content follows them
        var inLead = true;
        foreach (var block in blocks)
        {
            if (block.Level >= 2)
            {
                if (block.Level == 2)
                {
                    inLead = false;
                    target = Classify(block.Text) switch { "service" => service, "development" => development, _ => null };
                }

                headings.RemoveAll(h => h.Level >= block.Level); // a heading followed by nothing but headings is empty
                headings.Add(block);
                continue;
            }

            if (inLead)
            {
                if (summary is null && block.Plain.Length > 0 && !block.IsList)
                {
                    summary = Shorten(block.Plain);
                }

                continue;
            }

            if (target is null)
            {
                continue;
            }

            foreach (var heading in headings)
            {
                target.Append(target.Length > 0 ? "\n\n" : "").Append(new string('#', heading.Level)).Append(' ').Append(Escape(heading.Text));
            }

            headings.Clear();
            target.Append(target.Length > 0 ? "\n\n" : "").Append(block.Text);
        }

        return new ArticleProse(summary, NullIfEmpty(development), NullIfEmpty(service));

        static string? NullIfEmpty(StringBuilder text) => text.Length > 0 ? text.ToString() : null;
    }

    private static string? Classify(string heading)
    {
        var words = Words().Matches(heading.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        return words.Overlaps(ServiceWords) ? "service" : words.Overlaps(DevelopmentWords) ? "development" : null;
    }

    private static string Shorten(string text)
    {
        if (text.Length <= SummaryMaxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(". ", SummaryMaxLength - 1, StringComparison.Ordinal);
        if (cut > 0)
        {
            return text[..(cut + 1)];
        }

        var space = text.LastIndexOf(' ', SummaryMaxLength - 2);
        return text[..(space > 0 ? space : SummaryMaxLength - 1)] + "…";
    }

    // Blocks -------------------------------------------------------------------------------------------------------

    /// <summary>A heading (Level 2–4, Text = plain title), a paragraph (Level 0) or a list item run.</summary>
    private sealed record Block(int Level, string Text, string Plain, bool IsList = false);

    private static void Blocks(Node node, List<Block> blocks)
    {
        foreach (var child in node.Children)
        {
            if (child is not Element e || IsDropped(e))
            {
                continue;
            }

            switch (e.Name)
            {
                case "h2" or "h3" or "h4":
                    var title = Inline(e, plain: true);
                    if (title.Length > 0)
                    {
                        blocks.Add(new Block(e.Name[1] - '0', title, title));
                    }

                    break;
                case "p":
                    var text = Inline(e, plain: false);
                    if (text.Length > 0)
                    {
                        blocks.Add(new Block(0, text, Inline(e, plain: true)));
                    }

                    break;
                case "ul" or "ol":
                    var lines = new List<string>();
                    ListItems(e, lines, 0);
                    if (lines.Count > 0)
                    {
                        blocks.Add(new Block(0, string.Join("\n", lines), "", IsList: true));
                    }

                    break;
                default:
                    Blocks(e, blocks); // section, body, html, main, span wrappers…
                    break;
            }
        }
    }

    private static void ListItems(Element list, List<string> lines, int depth)
    {
        var number = 1;
        foreach (var li in list.Children.OfType<Element>().Where(c => c.Name == "li" && !IsDropped(c)))
        {
            var text = Inline(li, plain: false, skipLists: true);
            if (text.Length > 0)
            {
                lines.Add(new string(' ', depth * 2) + (list.Name == "ol" ? $"{number++}. " : "- ") + text);
            }

            foreach (var nested in li.Children.OfType<Element>().Where(c => c.Name is "ul" or "ol"))
            {
                ListItems(nested, lines, depth + 1);
            }
        }
    }

    // Inline -------------------------------------------------------------------------------------------------------

    private static string Inline(Element e, bool plain, bool skipLists = false)
    {
        var text = new StringBuilder();
        AppendInline(e, text, plain, skipLists);
        return Whitespace().Replace(text.ToString(), " ").Trim();
    }

    private static void AppendInline(Node node, StringBuilder text, bool plain, bool skipLists)
    {
        foreach (var child in node.Children)
        {
            if (child is Text t)
            {
                var decoded = WebUtility.HtmlDecode(t.Value);
                text.Append(plain ? decoded : Escape(decoded));
                continue;
            }

            var e = (Element)child;
            if (IsDropped(e) || (skipLists && e.Name is "ul" or "ol"))
            {
                continue;
            }

            if (e.Name == "br")
            {
                text.Append(' ');
                continue;
            }

            var inner = new StringBuilder();
            AppendInline(e, inner, plain, skipLists);
            var content = inner.ToString();
            if (plain || content.Trim().Length == 0)
            {
                text.Append(content);
                continue;
            }

            switch (e.Name)
            {
                case "b" or "strong":
                    Wrap(text, content, "**", "**");
                    break;
                case "i" or "em":
                    Wrap(text, content, "*", "*");
                    break;
                case "a" when LinkTarget(e) is { } href:
                    Wrap(text, content, "[", $"]({href})");
                    break;
                default:
                    text.Append(content);
                    break;
            }
        }
    }

    /// <summary>Markers hug the words: " **AK-47** " not "** AK-47 **", which Markdown would not render.</summary>
    private static void Wrap(StringBuilder text, string content, string open, string close)
    {
        var trimmed = content.Trim();
        text.Append(content[..(content.Length - content.TrimStart().Length)])
            .Append(open).Append(Whitespace().Replace(trimmed, " ")).Append(close)
            .Append(content[content.TrimEnd().Length..]);
    }

    /// <summary>
    /// Article links point at the English Wikipedia; red links (articles not written yet) keep only their text;
    /// only http(s) external links are kept.
    /// </summary>
    private static string? LinkTarget(Element a)
    {
        var href = a.Attribute("href");
        if (href is null || (a.Attribute("class") ?? "").Split(' ').Contains("new") || href.Contains("redlink=1", StringComparison.Ordinal))
        {
            return null;
        }

        if (href.StartsWith("./", StringComparison.Ordinal))
        {
            return "https://en.wikipedia.org/wiki/" + href[2..].Replace(" ", "_", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);
        }

        return href.StartsWith("https://", StringComparison.Ordinal) || href.StartsWith("http://", StringComparison.Ordinal)
            ? href.Replace(" ", "%20", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal)
            : null;
    }

    private static bool IsDropped(Element e)
    {
        var classes = e.Attribute("class") ?? "";
        if (Dropped.Contains(e.Name))
        {
            // Long lists (users, conflicts) are laid out in columns: the one kind of box whose content is prose.
            return !(e.Name == "div" && classes.Contains("div-col", StringComparison.Ordinal));
        }

        var typeOf = e.Attribute("typeof") ?? "";
        return typeOf.Contains("mw:Extension/ref", StringComparison.Ordinal)
            || classes.Contains("reference", StringComparison.Ordinal)
            || classes.Contains("noprint", StringComparison.Ordinal)
            || classes.Contains("mw-empty-elt", StringComparison.Ordinal)
            || (e.Attribute("style") ?? "").Replace(" ", "", StringComparison.Ordinal).Contains("display:none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Backslash-escapes what Markdown would read as formatting, links or raw HTML.</summary>
    private static string Escape(string text) => MarkdownSpecial().Replace(text, @"\$0");

    // A small HTML tokenizer ---------------------------------------------------------------------------------------

    private abstract class Node
    {
        public List<Node> Children { get; } = [];
    }

    private sealed class Element(string name, Dictionary<string, string> attributes) : Node
    {
        public string Name { get; } = name;
        public string? Attribute(string key) => attributes.GetValueOrDefault(key);
    }

    private sealed class Text(string value) : Node
    {
        public string Value { get; } = value;
    }

    /// <summary>
    /// Builds a tree from well-formed HTML such as Parsoid's: quoted attribute values may contain "&gt;", void
    /// elements need no closing tag, the text of style and script is skipped, and a stray closing tag is ignored.
    /// </summary>
    private static Element Parse(string html)
    {
        var root = new Element("#root", []);
        var open = new Stack<Element>([root]);
        var position = 0;
        foreach (Match m in Tag().Matches(html))
        {
            if (m.Index < position)
            {
                continue; // inside a skipped style/script body
            }

            if (m.Index > position)
            {
                open.Peek().Children.Add(new Text(html[position..m.Index]));
            }

            position = m.Index + m.Length;
            if (!m.Groups["name"].Success)
            {
                continue; // comment, doctype
            }

            var name = m.Groups["name"].Value.ToLowerInvariant();
            if (m.Groups["close"].Value == "/")
            {
                if (open.Any(e => e.Name == name))
                {
                    while (open.Pop().Name != name)
                    {
                    }
                }

                continue;
            }

            var attributes = new Dictionary<string, string>();
            foreach (Match a in Attribute().Matches(m.Groups["attrs"].Value))
            {
                attributes[a.Groups["key"].Value.ToLowerInvariant()] =
                    WebUtility.HtmlDecode(a.Groups["dq"].Success ? a.Groups["dq"].Value : a.Groups["sq"].Success ? a.Groups["sq"].Value : a.Groups["bare"].Value);
            }

            var element = new Element(name, attributes);
            open.Peek().Children.Add(element);
            if (name is "style" or "script")
            {
                var end = html.IndexOf("</" + name, position, StringComparison.OrdinalIgnoreCase);
                var close = end < 0 ? -1 : html.IndexOf('>', end);
                position = close < 0 ? html.Length : close + 1;
            }
            else if (!Void.Contains(name) && !m.Groups["self"].Success)
            {
                open.Push(element);
            }
        }

        if (position < html.Length)
        {
            open.Peek().Children.Add(new Text(html[position..]));
        }

        return root;
    }

    [GeneratedRegex("""<!--[\s\S]*?-->|<![^>]*>|<(?<close>/?)(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:[^>"']|"[^"]*"|'[^']*')*?)(?<self>/)?>""")]
    private static partial Regex Tag();

    [GeneratedRegex("""(?<key>[^\s=/>"']+)(?:\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)'|(?<bare>[^\s"'>]+)))?""")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();

    [GeneratedRegex(@"[\\`*_\[\]<>#|]")]
    private static partial Regex MarkdownSpecial();
}
