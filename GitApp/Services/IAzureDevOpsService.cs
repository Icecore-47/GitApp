using GitApp.Models;

namespace GitApp.Services
{
    public interface IAzureDevOpsService
    {

        // Misc
        Task<string> GetFileContentAsync(string organization, string projectId, string repositoryId, string commitId, string path);

        // Git - Repositories
        Task<List<GitRepository>> GetRepositoriesAsync(string projectNameOrId);
        Task<GitRepository> GetRepositoryAsync(string projectNameOrId, string repositoryId);

        // Git - Pull Requests
        Task<List<PullRequest>> GetPullRequestsAsync(string project, string repositoryId, string status = null);
        Task<PullRequest> GetPullRequestAsync(string project, string repositoryId, int pullRequestId);

        // Git - Commits
        Task<List<GitCommit>> GetCommitsAsync(string project, string repositoryId, string branch = null, int top = 100);
        Task<List<GitChange>> GetCommitChangesAsync(string organization, string projectId, string repositoryId, string commitId);
        Task<GitCommit> GetCommitAsync(string project, string repositoryId, string commitId);

        // Git - Branches
        Task<List<Branch>> GetBranchesAsync(string project, string repositoryId);
        Task<Branch> GetBranchAsync(string project, string repositoryId, string branchName);

        // Pipelines - Definitions
        Task<List<PipelineDefinition>> GetPipelineDefinitionsAsync(string project);
        Task<PipelineDefinition> GetPipelineDefinitionAsync(string project, int pipelineId);

        // Pipelines - Runs
        Task<List<PipelineRun>> GetPipelineRunsAsync(string project, int pipelineId, int top = 100);
        Task<PipelineRun> GetPipelineRunAsync(string project, int pipelineId, int runId);

        // Pipelines - Artifacts
        Task<List<PipelineArtifact>> GetPipelineArtifactsAsync(string project, int pipelineId, int runId);

        // Projects
        Task<List<Project>> GetProjectsAsync();
        Task<Project> GetProjectAsync(string projectIdOrName);
    }
}
