public class Branch
{
    public string Name { get; set; }
    public string ObjectId { get; set; }
    public BranchCreator Creator { get; set; }
    public string Url { get; set; }
}

public class BranchCreator
{
    public string DisplayName { get; set; }
    public string Url { get; set; }
    public BranchAvatarLinks Links { get; set; }
    public string Id { get; set; }
    public string UniqueName { get; set; }
    public string ImageUrl { get; set; }
    public string Descriptor { get; set; }
}

public class BranchAvatarLinks
{
    public AvatarLink Avatar { get; set; }
}

public class AvatarLink
{
    public string Href { get; set; }
}
