using System.Collections.Generic;

public class ContentLoadResult
{
    public List<ContentRecord> Records = new();
    public List<SourceDiagnostic> Diagnostics = new();
}