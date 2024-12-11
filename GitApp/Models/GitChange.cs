public class GitChange
{
    public string ChangeType { get; set; }    // e.g., "edit", "add", "delete"
    public GitItem Item { get; set; }         // Nested item details
}

public class GitItem
{
    public string ObjectId { get; set; }
    public string OriginalObjectId { get; set; }
    public string GitObjectType { get; set; }
    public string CommitId { get; set; }
    public string Path { get; set; }
    public string Url { get; set; }

    public string GetDiffUrl(string organization, string projectId, string repositoryId)
    {
        if (!string.IsNullOrEmpty(OriginalObjectId) && !string.IsNullOrEmpty(ObjectId))
        {
            return $"https://dev.azure.com/{organization}/{projectId}/_git/{repositoryId}?path={Uri.EscapeDataString(Path)}&version=GC{Uri.EscapeDataString(OriginalObjectId)}&version=GC{Uri.EscapeDataString(ObjectId)}";
        }
        return null; // Return null if any required values are missing
    }
}



public class GitChangeData
{
    public Dictionary<string, int> ChangeCounts { get; set; } // Tracks counts by change type (e.g., "Edit": 1)
    public List<GitChange> Changes { get; set; }              // List of individual file changes
}
