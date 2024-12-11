namespace GitApp.Models
{
    public class PipelineRun
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }           // e.g., notStarted, inProgress, succeeded, failed
        public string Result { get; set; }           // e.g., succeeded, failed
        public string Url { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime FinishTime { get; set; }
        // Add other relevant properties as needed
    }
}
