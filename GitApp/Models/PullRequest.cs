namespace GitApp.Models
{
    // New Models for Git

    public class PullRequest
    {
        public int PullRequestId { get; set; }
        public string Status { get; set; }           // e.g., active, completed, abandoned
        public string Title { get; set; }
        public string Description { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public string SourceRefName { get; set; }    // e.g., refs/heads/feature-branch
        public string TargetRefName { get; set; }    // e.g., refs/heads/main
        public string Url { get; set; }
    }
}
