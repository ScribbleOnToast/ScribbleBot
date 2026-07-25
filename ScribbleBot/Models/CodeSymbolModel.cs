namespace ScribbleBot.Models;

public class CodeSymbolModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public string SymbolType { get; set; } = string.Empty; // Class, Method, Interface, Property
    public string SymbolName { get; set; } = string.Empty;
    public string? Signature { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Content { get; set; }

    // Text structure offsets for precise file editing
    public int SpanStart { get; set; }
    public int SpanLength { get; set; }
}

