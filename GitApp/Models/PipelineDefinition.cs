namespace GitApp.Models
{
    // New Models for Pipelines

    public class PipelineDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Path { get; set; }
        public bool QueueStatus { get; set; }
        public string Revision { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        // Add other relevant properties as needed
    }
}
