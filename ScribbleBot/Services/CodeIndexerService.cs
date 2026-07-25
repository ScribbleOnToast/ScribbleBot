using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScribbleBot.Models;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace ScribbleBot.Services
{
    public class CodeIndexerService
    {
        private readonly DatabaseService _dbService;

        public CodeIndexerService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task<int> IndexDirectoryAsync(string directoryPath, CancellationToken ct = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
            }

            // Infer project name from directory if not explicitly provided
            string projectName = new DirectoryInfo(directoryPath).Name;

            await _dbService.ClearProjectSymbolsAsync(projectName);

            var targetFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) &&
                            !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

            var allSymbols = new List<CodeSymbolModel>();

            foreach (var filePath in targetFiles)
            {
                ct.ThrowIfCancellationRequested();

                if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var csharpSymbols = await ParseCSharpFileAsync(filePath, ct);
                    allSymbols.AddRange(csharpSymbols);
                }
                else if (filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    var xamlSymbols = await ParseXamlFileAsync(filePath, ct);
                    allSymbols.AddRange(xamlSymbols);
                }
            }

            if (allSymbols.Count > 0)
            {
                await _dbService.SaveCodeSymbolsAsync(allSymbols, projectName);
            }

            return allSymbols.Count;
        }

        private async Task<List<CodeSymbolModel>> ParseCSharpFileAsync(string filePath, CancellationToken ct)
        {
            string sourceCode = await File.ReadAllTextAsync(filePath, ct);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: ct);
            SyntaxNode root = await tree.GetRootAsync(ct);

            var symbols = new List<CodeSymbolModel>();

            var typeNodes = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeNode in typeNodes)
            {
                var lineSpan = typeNode.SyntaxTree.GetLineSpan(typeNode.Span);
                symbols.Add(new CodeSymbolModel
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = filePath,
                    SymbolType = typeNode.Kind().ToString().Replace("Declaration", string.Empty),
                    SymbolName = typeNode.Identifier.Text,
                    Signature = typeNode.HeaderToString(),
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    SpanStart = typeNode.Span.Start,
                    SpanLength = typeNode.Span.Length,
                    Content = typeNode.ToString()
                });
            }

            var methodNodes = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodNode in methodNodes)
            {
                var lineSpan = methodNode.SyntaxTree.GetLineSpan(methodNode.Span);
                symbols.Add(new CodeSymbolModel
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = filePath,
                    SymbolType = "Method",
                    SymbolName = methodNode.Identifier.Text,
                    Signature = $"{methodNode.ReturnType} {methodNode.Identifier}{methodNode.ParameterList}",
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    SpanStart = methodNode.Span.Start,
                    SpanLength = methodNode.Span.Length,
                    Content = methodNode.ToString()
                });
            }

            return symbols;
        }

        private async Task<List<CodeSymbolModel>> ParseXamlFileAsync(string filePath, CancellationToken ct)
        {
            var symbols = new List<CodeSymbolModel>();
            string content = await File.ReadAllTextAsync(filePath, ct);

            try
            {
                using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings { Async = true });
                var doc = await XDocument.LoadAsync(reader, LoadOptions.SetLineInfo, ct);

                // Index root element (Window, UserControl, Application, ResourceDictionary)
                if (doc.Root != null)
                {
                    var lineInfo = (IXmlLineInfo)doc.Root;
                    string xClass = doc.Root.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Class")?.Value ?? string.Empty;

                    symbols.Add(new CodeSymbolModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        FilePath = filePath,
                        SymbolType = "XamlView",
                        SymbolName = doc.Root.Name.LocalName,
                        Signature = !string.IsNullOrEmpty(xClass) ? $"x:Class=\"{xClass}\"" : doc.Root.Name.LocalName,
                        StartLine = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                        EndLine = content.Split('\n').Length,
                        SpanStart = 0,
                        SpanLength = content.Length,
                        Content = doc.Root.ToString()
                    });

                    // Extract named controls (x:Name / Name) and DataBindings across the view
                    foreach (var element in doc.Root.Descendants())
                    {
                        var elLineInfo = (IXmlLineInfo)element;
                        string? elementName = element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")?.Value
                                           ?? element.Attribute("Name")?.Value;

                        // Index named controls (e.g. <Button x:Name="SubmitBtn">)
                        if (!string.IsNullOrEmpty(elementName))
                        {
                            symbols.Add(new CodeSymbolModel
                            {
                                Id = Guid.NewGuid().ToString(),
                                FilePath = filePath,
                                SymbolType = "XamlControl",
                                SymbolName = elementName,
                                Signature = $"<{element.Name.LocalName} x:Name=\"{elementName}\">",
                                StartLine = elLineInfo.HasLineInfo() ? elLineInfo.LineNumber : 0,
                                EndLine = elLineInfo.HasLineInfo() ? elLineInfo.LineNumber : 0,
                                SpanStart = 0,
                                SpanLength = element.ToString().Length,
                                Content = element.ToString()
                            });
                        }
                    }
                }
            }
            catch (XmlException)
            {
                // Silently skip malformed XAML files or report parsing failure
            }

            return symbols;
        }
    }
    internal static class SyntaxExtensions
    {
        public static string HeaderToString(this TypeDeclarationSyntax typeNode)
        {
            var modifiers = typeNode.Modifiers.ToString();
            var keyword = typeNode.Keyword.Text;
            var identifier = typeNode.Identifier.Text;
            var typeParams = typeNode.TypeParameterList?.ToString() ?? string.Empty;
            return $"{modifiers} {keyword} {identifier}{typeParams}".Trim();
        }
    }
}