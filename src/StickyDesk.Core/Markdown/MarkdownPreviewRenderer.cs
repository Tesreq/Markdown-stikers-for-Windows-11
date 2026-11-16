using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace StickyDesk.Core;

/// <summary>Renders a Markdown AST into native RTF; never writes or reconstructs the Markdown source.</summary>
public static class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseTaskLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).UsePreciseSourceLocation().Build();

    public static MarkdownPreviewDocument Render(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var writer = new Writer(source);
        writer.Blocks(Markdown.Parse(source, Pipeline), 0, false);
        return writer.Finish();
    }

    [Flags] private enum Style { Normal = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8, Link = 16 }

    private sealed class Writer(string source)
    {
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _rtf = new(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl{\f0 Segoe UI;}{\f1 Consolas;}{\f2 Segoe UI Symbol;}}{\colortbl;\red42\green48\blue57;\red239\green240\blue242;\red116\green123\blue132;\red61\green104\blue157;}\viewkind4\f0\fs22\cf1 ");
        private readonly List<PreviewLink> _links = [];
        private readonly List<PreviewSourceRange> _ranges = [];

        public void Blocks(ContainerBlock blocks, int indent, bool quote)
        {
            foreach (var block in blocks) Block(block, indent, quote);
        }

        private void Block(Block block, int indent, bool quote)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    BeginParagraph(indent, quote, heading.Level switch { 1 => 34, 2 => 30, 3 => 26, 4 => 24, _ => 22 }, 100);
                    Inlines(heading.Inline, Style.Bold);
                    EndParagraph();
                    break;
                case ParagraphBlock paragraph:
                    Paragraph(paragraph, indent, quote);
                    break;
                case CodeBlock code:
                    BeginParagraph(indent + 120, quote, 20, 100);
                    Append(code.Lines.ToString(), code.Span, Style.Code);
                    EndParagraph();
                    break;
                case ListBlock list:
                    var number = long.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
                    foreach (var child in list)
                    {
                        if (child is not ListItemBlock item) continue;
                        var first = true;
                        foreach (var itemBlock in item)
                        {
                            if (itemBlock is ParagraphBlock itemParagraph)
                            {
                                var task = itemParagraph.Inline?.FirstChild is TaskList;
                                var prefix = first && !task ? list.IsOrdered ? $"{number}. " : "• " : "";
                                BeginParagraph(indent + 120, quote, 22, list.IsLoose ? 100 : 30);
                                Append(prefix, item.Span);
                                Inlines(itemParagraph.Inline);
                                EndParagraph();
                            }
                            else Block(itemBlock, indent + 260, quote);
                            first = false;
                        }
                        number++;
                    }
                    break;
                case QuoteBlock quoted:
                    Blocks(quoted, indent + 220, true);
                    break;
                case ThematicBreakBlock:
                    BeginParagraph(indent, true, 20, 100);
                    Append("────────────", block.Span);
                    EndParagraph();
                    break;
                case ContainerBlock container:
                    Blocks(container, indent, quote);
                    break;
                default:
                    // Unsupported block types remain readable instead of disappearing.
                    BeginParagraph(indent, quote, 22, 100);
                    if (block.Span.Start >= 0 && block.Span.End < source.Length)
                        Append(source.Substring(block.Span.Start, block.Span.Length), block.Span);
                    EndParagraph();
                    break;
            }
        }

        private void Paragraph(ParagraphBlock paragraph, int indent, bool quote)
        {
            BeginParagraph(indent, quote, 22, 120);
            Inlines(paragraph.Inline);
            EndParagraph();
        }

        private void Inlines(ContainerInline? container, Style style = Style.Normal)
        {
            if (container is null) return;
            for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        Append(literal.Content.ToString(), literal.Span, style);
                        break;
                    case EmphasisInline emphasis:
                        var emphasisStyle = emphasis.DelimiterChar == '~' ? Style.Strike : emphasis.DelimiterCount >= 2 ? Style.Bold : Style.Italic;
                        Inlines(emphasis, style | emphasisStyle);
                        break;
                    case CodeInline code:
                        Append(code.Content, code.Span, style | Style.Code);
                        break;
                    case TaskList task:
                        Append(task.Checked ? "☑" : "☐", task.Span, style);
                        break;
                    case LinkInline link:
                        var offset = _text.Length;
                        if (link.IsImage) Append("[Изображение: ", link.Span, style);
                        Inlines(link, link.IsImage ? style : style | Style.Link);
                        if (link.IsImage) Append("]", link.Span, style);
                        else if (!string.IsNullOrEmpty(link.Url)) _links.Add(new(offset, _text.Length - offset, link.Url));
                        break;
                    case AutolinkInline auto:
                        var autoOffset = _text.Length;
                        Append(auto.Url, auto.Span, style | Style.Link);
                        _links.Add(new(autoOffset, _text.Length - autoOffset, auto.IsEmail ? "mailto:" + auto.Url : auto.Url));
                        break;
                    case HtmlEntityInline entity:
                        Append(entity.Transcoded.ToString(), entity.Span, style);
                        break;
                    case HtmlInline html:
                        Append(html.Tag, html.Span, style);
                        break;
                    case LineBreakInline line:
                        Append(line.IsHard ? "\n" : " ", line.Span, style);
                        break;
                    case ContainerInline nested:
                        Inlines(nested, style);
                        break;
                    default:
                        if (inline.Span.Start >= 0 && inline.Span.End < source.Length)
                            Append(source.Substring(inline.Span.Start, inline.Span.Length), inline.Span, style);
                        break;
                }
            }
        }

        private void BeginParagraph(int indent, bool quote, int fontSize, int after)
        {
            _rtf.Append(CultureInfo.InvariantCulture, $"\\pard\\li{indent}\\ri0\\sa{after}\\sb0\\sl290\\slmult1\\f0\\fs{fontSize}\\cf{(quote ? 3 : 1)} ");
        }

        private void EndParagraph() { _text.Append('\n'); _rtf.Append(@"\par "); }

        private void Append(string value, SourceSpan span, Style style = Style.Normal)
        {
            if (value.Length == 0) return;
            _ranges.Add(new(_text.Length, value.Length, Math.Max(0, span.Start), Math.Max(0, span.Length)));
            _text.Append(value);
            _rtf.Append('{');
            if (style.HasFlag(Style.Bold)) _rtf.Append(@"\b ");
            if (style.HasFlag(Style.Italic)) _rtf.Append(@"\i ");
            if (style.HasFlag(Style.Strike)) _rtf.Append(@"\strike ");
            if (style.HasFlag(Style.Link)) _rtf.Append(@"\ul\cf4 ");
            if (style.HasFlag(Style.Code)) _rtf.Append(@"\f1\fs20\highlight2 ");
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': case '{': case '}': _rtf.Append('\\').Append(character); break;
                    case '\n': _rtf.Append(@"\line "); break;
                    case '\r': break;
                    case '\t': _rtf.Append(@"\tab "); break;
                    default:
                        if (character < 32) _rtf.Append(' ');
                        else if (character > 126) _rtf.Append(CultureInfo.InvariantCulture, $"\\u{(short)character}?");
                        else _rtf.Append(character);
                        break;
                }
            }
            _rtf.Append('}');
        }

        public MarkdownPreviewDocument Finish() => new(_text.ToString(), _rtf.Append('}').ToString(), _links.AsReadOnly(), _ranges.AsReadOnly());
    }
}
