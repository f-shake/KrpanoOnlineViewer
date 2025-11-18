namespace KrpanoOnlineViewer;

public class ProcessingStatus
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // uploading, saving, processing, completed, error
    public int Progress { get; set; }
    public string? OriginalFileName { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public DateTime? CompletedAt { get; set; }
}
