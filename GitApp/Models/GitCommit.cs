public class GitCommit
{
    public string CommitId { get; set; }
    public string TreeId { get; set; }
    public GitAuthor Author { get; set; }
    public GitCommitter Committer { get; set; }
    public string Comment { get; set; }
    public DateTime CommitDate { get; set; }
    public string Url { get; set; }
    public string RemoteUrl { get; set; }
    public List<string> Parents { get; set; }
    public List<GitChange> Changes { get; set; }  // New property for changes

}

public class GitAuthor
{
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime Date { get; set; }
    public string ImageUrl { get; set; }
}

public class GitCommitter
{
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime Date { get; set; }
    public string ImageUrl { get; set; }
}
public class TreeResponse
{
    public List<TreeEntry> TreeEntries { get; set; }
}

public class TreeEntry
{
    public string ObjectId { get; set; }
    public string RelativePath { get; set; }
    public string Mode { get; set; }
    public string GitObjectType { get; set; }
    public string Url { get; set; }
    public int Size { get; set; }
}
public class CommitMetadata
{
    public string TreeId { get; set; }
}

