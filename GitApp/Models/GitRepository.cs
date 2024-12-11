namespace GitApp.Models
{
    public class GitRepository
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string DefaultBranch { get; set; }
        public string RemoteUrl { get; set; }
        public string SshUrl { get; set; }
        public string WebUrl { get; set; }
    }
}
