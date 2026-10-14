    public class ValidationResult
    {
        public ValidationSeverity Severity;
        public string Message;
    }

    public enum ValidationSeverity
    {
        Warning,
        Error
    }