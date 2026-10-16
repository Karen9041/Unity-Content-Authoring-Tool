using System.Collections.Generic;

public class PrefabContractResult
{
    public bool IsValid;
    public List<ValidationResult> Issues = new();
}