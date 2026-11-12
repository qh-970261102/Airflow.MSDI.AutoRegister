using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Airflow.MSDI.AutoRegister.Generator;

/// <summary>
/// A KeyTemplate: literal text plus placeholders that expands to a C# expression.
/// The expression is emitted into generated code and evaluated at runtime; this type
/// never executes user code.
/// </summary>
internal sealed class KeyTemplateExpression
{
    private readonly Segment[] _segments;

    private KeyTemplateExpression(Segment[] segments)
    {
        _segments = segments;
    }

    /// <summary>解析模板并校验展开后的表达式语法。</summary>
    public static bool TryParse(string text, out KeyTemplateExpression? template, out string? error)
    {
        template = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The key template is empty.";
            return false;
        }

        var segments = new List<Segment>();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;
            segments.Add(new LiteralSegment(literal.ToString()));
            literal.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                var end = text.IndexOf('}', i + 1);
                if (end < 0)
                {
                    error = $"Unclosed placeholder at position {i}.";
                    return false;
                }

                var name = text.Substring(i + 1, end - i - 1);
                if (!TryParsePlaceholder(name, out var placeholder))
                {
                    error = $"Unknown placeholder '{{{name}}}' at position {i}; supported: {{Type}}, {{TypeName}}, {{ServiceType}}, {{ServiceTypeName}}, {{Namespace}}, {{AssemblyName}}.";
                    return false;
                }

                FlushLiteral();
                segments.Add(new PlaceholderSegment(placeholder));
                i = end + 1;
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }

                error = $"Unescaped '}}' at position {i}; use '}}}}' for a literal brace.";
                return false;
            }

            literal.Append(c);
            i++;
        }

        FlushLiteral();

        // Validate the expanded shape with representative values. This catches syntax errors
        // (e.g. a placeholder inside a type-of context) without resolving user symbols.
        var validation = new StringBuilder();
        foreach (var segment in segments)
            segment.AppendValidation(validation);

        var validationText = validation.ToString();
        if (string.IsNullOrWhiteSpace(validationText))
        {
            error = "The key template expands to an empty expression.";
            return false;
        }

        var expression = SyntaxFactory.ParseExpression(validationText);
        if (expression.ContainsDiagnostics || expression.FullSpan.Length == 0)
        {
            error = "The key template is not a valid C# expression.";
            return false;
        }

        if (expression.FullSpan.Length > validationText.Length ||
            validationText.Substring(expression.FullSpan.Length).Trim().Length != 0)
        {
            error = "The key template contains trailing tokens that are not part of a single C# expression.";
            return false;
        }

        template = new KeyTemplateExpression(segments.ToArray());
        return true;
    }

    /// <summary>按占位符类型展开为真实 C# 表达式。</summary>
    public string Expand(KeyTemplateContext context)
    {
        var sb = new StringBuilder();
        foreach (var segment in _segments)
            segment.AppendExpanded(sb, context);
        return sb.ToString();
    }

    private static bool TryParsePlaceholder(string name, out Placeholder placeholder)
    {
        switch (name)
        {
            case "Type":
                placeholder = Placeholder.Type;
                return true;
            case "TypeName":
                placeholder = Placeholder.TypeName;
                return true;
            case "ServiceType":
                placeholder = Placeholder.ServiceType;
                return true;
            case "ServiceTypeName":
                placeholder = Placeholder.ServiceTypeName;
                return true;
            case "Namespace":
                placeholder = Placeholder.Namespace;
                return true;
            case "AssemblyName":
                placeholder = Placeholder.AssemblyName;
                return true;
            default:
                placeholder = default;
                return false;
        }
    }

    private enum Placeholder
    {
        Type,
        TypeName,
        ServiceType,
        ServiceTypeName,
        Namespace,
        AssemblyName,
    }

    private abstract class Segment
    {
        public abstract void AppendValidation(StringBuilder sb);

        public abstract void AppendExpanded(StringBuilder sb, KeyTemplateContext context);
    }

    private sealed class LiteralSegment : Segment
    {
        private readonly string _text;

        public LiteralSegment(string text)
        {
            _text = text;
        }

        public override void AppendValidation(StringBuilder sb) => sb.Append(_text);

        public override void AppendExpanded(StringBuilder sb, KeyTemplateContext context) => sb.Append(_text);
    }

    private sealed class PlaceholderSegment : Segment
    {
        private readonly Placeholder _placeholder;

        public PlaceholderSegment(Placeholder placeholder)
        {
            _placeholder = placeholder;
        }

        public override void AppendValidation(StringBuilder sb)
        {
            switch (_placeholder)
            {
                case Placeholder.Type:
                case Placeholder.ServiceType:
                    sb.Append("typeof(global::System.Object)");
                    break;
                default:
                    sb.Append("\"x\"");
                    break;
            }
        }

        public override void AppendExpanded(StringBuilder sb, KeyTemplateContext context)
        {
            switch (_placeholder)
            {
                case Placeholder.Type:
                    sb.Append("typeof(").Append(KeyTemplateContext.FormatType(context.ImplementationType)).Append(')');
                    break;
                case Placeholder.TypeName:
                    sb.Append(SymbolDisplay.FormatLiteral(context.ImplementationType.Name, quote: true));
                    break;
                case Placeholder.ServiceType:
                    sb.Append("typeof(").Append(KeyTemplateContext.FormatType(context.ServiceType)).Append(')');
                    break;
                case Placeholder.ServiceTypeName:
                    sb.Append(SymbolDisplay.FormatLiteral(context.ServiceType.Name, quote: true));
                    break;
                case Placeholder.Namespace:
                    sb.Append(SymbolDisplay.FormatLiteral(context.Namespace, quote: true));
                    break;
                case Placeholder.AssemblyName:
                    sb.Append(SymbolDisplay.FormatLiteral(context.AssemblyName, quote: true));
                    break;
                default:
                    throw new InvalidOperationException("Unknown key template placeholder.");
            }
        }
    }
}

/// <summary>Expansion data for one registration.</summary>
internal readonly struct KeyTemplateContext
{
    public KeyTemplateContext(INamedTypeSymbol implementationType, INamedTypeSymbol serviceType)
    {
        ImplementationType = implementationType;
        ServiceType = serviceType;
    }

    public INamedTypeSymbol ImplementationType { get; }

    public INamedTypeSymbol ServiceType { get; }

    public string Namespace =>
        ImplementationType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : ImplementationType.ContainingNamespace.ToDisplayString();

    public string AssemblyName => ImplementationType.ContainingAssembly?.Name ?? string.Empty;

    public static string FormatType(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
