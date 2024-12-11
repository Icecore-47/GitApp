namespace GitApp.Models
{
    public class PipelineArtifact
    {
        public string Name { get; set; }
        public string ResourceType { get; set; }
        public string Url { get; set; }
        public string DownloadUrl { get; set; }
        // Add other relevant properties as needed
    }
}
