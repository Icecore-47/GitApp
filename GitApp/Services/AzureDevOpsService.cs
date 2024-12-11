using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GitApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GitApp.Services
{

    public class AzureDevOpsService : IAzureDevOpsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _organization;
        private readonly string _personalAccessToken;
        private readonly ILogger<AzureDevOpsService> _logger;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public AzureDevOpsService(HttpClient httpClient, IConfiguration configuration, ILogger<AzureDevOpsService> logger)
        {
            _httpClient = httpClient;
            _organization = configuration["AzureDevOps:Organization"]
                           ?? throw new ArgumentNullException("AzureDevOps:Organization configuration is missing.");
            _personalAccessToken = configuration["AzureDevOps:PersonalAccessToken"]
                                   ?? throw new ArgumentNullException("AzureDevOps:PersonalAccessToken configuration is missing.");
            _logger = logger;

            // Set authorization header with PAT
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_personalAccessToken}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        }


        #region Misc
        public async Task<string> GetFileContentAsync(string organization, string projectId, string repositoryId, string commitId, string path)
        {
            // Step 1: Get the commit's treeId by fetching the commit metadata
            var commitUrl = $"https://dev.azure.com/{organization}/{Uri.EscapeDataString(projectId)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/commits/{commitId}?api-version=6.0";
            var commitResponse = await _httpClient.GetAsync(commitUrl);

            if (!commitResponse.IsSuccessStatusCode)
            {
                var commitErrorContent = await commitResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to retrieve commit '{CommitId}' metadata. Status Code: {StatusCode}, Details: {Details}", commitId, commitResponse.StatusCode, commitErrorContent);
                throw new HttpRequestException($"Failed to retrieve commit '{commitId}' metadata. Status Code: {commitResponse.StatusCode}");
            }

            var commitContent = await commitResponse.Content.ReadAsStringAsync();
            var commitData = JsonSerializer.Deserialize<CommitMetadata>(commitContent, _jsonOptions);

            if (commitData == null || string.IsNullOrEmpty(commitData.TreeId))
            {
                throw new InvalidOperationException($"The commit '{commitId}' does not contain a valid tree ID.");
            }

            // Step 2: Traverse the tree structure to locate the specific file blob
            var treeId = commitData.TreeId;
            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in pathSegments)
            {
                var treeUrl = $"https://dev.azure.com/{organization}/{Uri.EscapeDataString(projectId)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/trees/{treeId}?recursive=false&api-version=6.0";
                var treeResponse = await _httpClient.GetAsync(treeUrl);

                if (!treeResponse.IsSuccessStatusCode)
                {
                    var treeErrorContent = await treeResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve tree structure for treeId '{TreeId}'. Status Code: {StatusCode}, Details: {Details}", treeId, treeResponse.StatusCode, treeErrorContent);

                    throw new HttpRequestException($"Failed to retrieve tree structure for treeId '{treeId}'. Status Code: {treeResponse.StatusCode}");
                }

                var treeContent = await treeResponse.Content.ReadAsStringAsync();
                var treeData = JsonSerializer.Deserialize<TreeResponse>(treeContent, _jsonOptions);

                // Find the tree entry matching the current path segment
                var entry = treeData.TreeEntries.FirstOrDefault(e => e.RelativePath.Equals(segment, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    throw new FileNotFoundException($"The file or directory '{segment}' was not found in the specified commit.");
                }

                // Update the treeId or objectId depending on whether it's a tree or a blob
                if (entry.GitObjectType == "tree")
                {
                    treeId = entry.ObjectId; // Traverse deeper into the tree
                }
                else if (entry.GitObjectType == "blob" && entry.RelativePath == pathSegments.Last())
                {
                    // Step 3: If it's a blob and the last segment in the path, retrieve its content
                    var blobResponse = await _httpClient.GetAsync(entry.Url);
                    if (!blobResponse.IsSuccessStatusCode)
                    {
                        var blobErrorContent = await blobResponse.Content.ReadAsStringAsync();
                        _logger.LogError("Failed to retrieve blob content for '{BlobObjectId}' at path '{Path}'. Status Code: {StatusCode}, Details: {Details}", entry.ObjectId, path, blobResponse.StatusCode, blobErrorContent);
                        throw new HttpRequestException($"Failed to retrieve blob content for '{entry.ObjectId}' at path '{path}'. Status Code: {blobResponse.StatusCode}");
                    }

                    return await blobResponse.Content.ReadAsStringAsync(); // Return the content of the file
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected object type '{entry.GitObjectType}' encountered for '{segment}'.");
                }
            }

            throw new FileNotFoundException($"The file '{path}' was not found in the specified commit.");
        }

        #endregion

        #region Projects

        public async Task<List<Project>> GetProjectsAsync()
        {
            var url = $"https://dev.azure.com/{_organization}/_apis/projects?api-version=6.0";
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve projects. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve projects. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var projectData = JsonSerializer.Deserialize<ProjectData>(json, _jsonOptions);
                return projectData?.Value ?? new List<Project>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving projects.");
                throw;
            }
        }

        public async Task<Project> GetProjectAsync(string projectIdOrName)
        {
            if (string.IsNullOrWhiteSpace(projectIdOrName))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(projectIdOrName));

            var url = $"https://dev.azure.com/{_organization}/_apis/projects/{Uri.EscapeDataString(projectIdOrName)}?api-version=6.0";
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve project '{ProjectIdOrName}'. Status Code: {StatusCode}, Details: {Details}", projectIdOrName, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve project '{projectIdOrName}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var project = JsonSerializer.Deserialize<Project>(json, _jsonOptions);
                return project;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving project '{ProjectIdOrName}'.", projectIdOrName);
                throw;
            }
        }

        #endregion

        #region Git - Repositories

        public async Task<List<GitRepository>> GetRepositoriesAsync(string projectNameOrId)
        {
            if (string.IsNullOrWhiteSpace(projectNameOrId))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(projectNameOrId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(projectNameOrId)}/_apis/git/repositories?api-version=6.0";
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve repositories for project '{Project}'. Status Code: {StatusCode}, Details: {Details}", projectNameOrId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve repositories for project '{projectNameOrId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var repositoryData = JsonSerializer.Deserialize<RepositoryData>(json, _jsonOptions);
                return repositoryData?.Value ?? new List<GitRepository>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving repositories for project '{Project}'.", projectNameOrId);
                throw;
            }
        }

        public async Task<GitRepository> GetRepositoryAsync(string projectNameOrId, string repositoryId)
        {
            if (string.IsNullOrWhiteSpace(projectNameOrId))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(projectNameOrId));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(projectNameOrId)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}?api-version=6.0";
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve repository '{RepositoryId}'. Status Code: {StatusCode}, Details: {Details}", repositoryId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve repository '{repositoryId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var repository = JsonSerializer.Deserialize<GitRepository>(json, _jsonOptions);
                return repository;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving repository '{RepositoryId}'.", repositoryId);
                throw;
            }
        }

        #endregion

        #region Git - Pull Requests

        public async Task<List<PullRequest>> GetPullRequestsAsync(string project, string repositoryId, string status = null)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullrequests?api-version=6.0";

            if (!string.IsNullOrWhiteSpace(status))
            {
                url += $"&status={Uri.EscapeDataString(status)}"; // e.g., active, completed, abandoned
            }

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pull requests. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pull requests. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var prData = JsonSerializer.Deserialize<PullRequestData>(json, _jsonOptions);
                return prData?.Value ?? new List<PullRequest>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pull requests.");
                throw;
            }
        }

        public async Task<PullRequest> GetPullRequestAsync(string project, string repositoryId, int pullRequestId)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullrequests/{pullRequestId}?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pull request '{PullRequestId}'. Status Code: {StatusCode}, Details: {Details}", pullRequestId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pull request '{pullRequestId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var pullRequest = JsonSerializer.Deserialize<PullRequest>(json, _jsonOptions);
                return pullRequest;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pull request '{PullRequestId}'.", pullRequestId);
                throw;
            }
        }

        #endregion

        #region Git - Commits
        public async Task<List<GitChange>> GetCommitChangesAsync(string organization, string projectId, string repositoryId, string commitId)
        {
            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(commitId))
                throw new ArgumentException("Invalid parameter(s) for GetCommitChangesAsync.");

            var url = $"https://dev.azure.com/{organization}/{Uri.EscapeDataString(projectId)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/commits/{Uri.EscapeDataString(commitId)}/changes?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve changes for commit '{CommitId}'. Status Code: {StatusCode}, Details: {Details}", commitId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve changes for commit '{commitId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var changeData = JsonSerializer.Deserialize<GitChangeData>(json, _jsonOptions);

                // Set DiffUrl by calling GetDiffUrl on each GitItem
                foreach (var change in changeData.Changes)
                {
                    if (change.Item != null)
                    {
                        change.Item.Url = change.Item.GetDiffUrl(organization, projectId, repositoryId);
                    }
                }

                return changeData?.Changes ?? new List<GitChange>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving changes for commit '{CommitId}'.", commitId);
                throw;
            }
        }


        public async Task<List<GitCommit>> GetCommitsAsync(string project, string repositoryId, string branch = null, int top = 100)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/commits?api-version=6.0&$top={top}";

            if (!string.IsNullOrWhiteSpace(branch))
            {
                url += $"&searchCriteria.itemVersion.version={Uri.EscapeDataString(branch)}";
            }

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve commits. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve commits. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var commitData = JsonSerializer.Deserialize<GitCommitData>(json, _jsonOptions);
                return commitData?.Value ?? new List<GitCommit>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving commits.");
                throw;
            }
        }

        public async Task<GitCommit> GetCommitAsync(string project, string repositoryId, string commitId)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));
            if (string.IsNullOrWhiteSpace(commitId))
                throw new ArgumentException("Commit ID cannot be null or empty.", nameof(commitId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/commits/{Uri.EscapeDataString(commitId)}?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve commit '{CommitId}'. Status Code: {StatusCode}, Details: {Details}", commitId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve commit '{commitId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var commit = JsonSerializer.Deserialize<GitCommit>(json, _jsonOptions);
                return commit;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving commit '{CommitId}'.", commitId);
                throw;
            }
        }

        #endregion

        #region Git - Branches

        public async Task<List<Branch>> GetBranchesAsync(string project, string repositoryId)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/refs?filter=heads/&api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve branches. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve branches. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var branchData = JsonSerializer.Deserialize<GitBranchData>(json, _jsonOptions);
                return branchData?.Value ?? new List<Branch>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving branches.");
                throw;
            }
        }

        public async Task<Branch> GetBranchAsync(string project, string repositoryId, string branchName)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (string.IsNullOrWhiteSpace(repositoryId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repositoryId));
            if (string.IsNullOrWhiteSpace(branchName))
                throw new ArgumentException("Branch name cannot be null or empty.", nameof(branchName));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/refs?filter={Uri.EscapeDataString(branchName)}&api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve branch '{BranchName}'. Status Code: {StatusCode}, Details: {Details}", branchName, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve branch '{branchName}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var branchData = JsonSerializer.Deserialize<GitBranchData>(json, _jsonOptions);
                return branchData?.Value.FirstOrDefault(b => b.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving branch '{BranchName}'.", branchName);
                throw;
            }
        }

        #endregion

        #region Pipelines - Definitions

        public async Task<List<PipelineDefinition>> GetPipelineDefinitionsAsync(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/pipelines?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pipeline definitions. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pipeline definitions. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var pipelineDefData = JsonSerializer.Deserialize<PipelineDefinitionData>(json, _jsonOptions);
                return pipelineDefData?.Value ?? new List<PipelineDefinition>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pipeline definitions.");
                throw;
            }
        }

        public async Task<PipelineDefinition> GetPipelineDefinitionAsync(string project, int pipelineId)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (pipelineId <= 0)
                throw new ArgumentException("Pipeline ID must be a positive integer.", nameof(pipelineId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pipeline definition '{PipelineId}'. Status Code: {StatusCode}, Details: {Details}", pipelineId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pipeline definition '{pipelineId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var pipelineDef = JsonSerializer.Deserialize<PipelineDefinition>(json, _jsonOptions);
                return pipelineDef;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pipeline definition '{PipelineId}'.", pipelineId);
                throw;
            }
        }

        #endregion

        #region Pipelines - Runs

        public async Task<List<PipelineRun>> GetPipelineRunsAsync(string project, int pipelineId, int top = 100)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (pipelineId <= 0)
                throw new ArgumentException("Pipeline ID must be a positive integer.", nameof(pipelineId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs?api-version=6.0&$top={top}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pipeline runs. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pipeline runs. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var pipelineRunData = JsonSerializer.Deserialize<PipelineRunData>(json, _jsonOptions);
                return pipelineRunData?.Value ?? new List<PipelineRun>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pipeline runs.");
                throw;
            }
        }

        public async Task<PipelineRun> GetPipelineRunAsync(string project, int pipelineId, int runId)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (pipelineId <= 0)
                throw new ArgumentException("Pipeline ID must be a positive integer.", nameof(pipelineId));
            if (runId <= 0)
                throw new ArgumentException("Run ID must be a positive integer.", nameof(runId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs/{runId}?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pipeline run '{RunId}'. Status Code: {StatusCode}, Details: {Details}", runId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pipeline run '{runId}'. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var pipelineRun = JsonSerializer.Deserialize<PipelineRun>(json, _jsonOptions);
                return pipelineRun;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pipeline run '{RunId}'.", runId);
                throw;
            }
        }

        #endregion

        #region Pipelines - Artifacts

        public async Task<List<PipelineArtifact>> GetPipelineArtifactsAsync(string project, int pipelineId, int runId)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Project identifier cannot be null or empty.", nameof(project));
            if (pipelineId <= 0)
                throw new ArgumentException("Pipeline ID must be a positive integer.", nameof(pipelineId));
            if (runId <= 0)
                throw new ArgumentException("Run ID must be a positive integer.", nameof(runId));

            var url = $"https://dev.azure.com/{_organization}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs/{runId}/artifacts?api-version=6.0";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to retrieve pipeline artifacts. Status Code: {StatusCode}, Details: {Details}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to retrieve pipeline artifacts. Status Code: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var artifactData = JsonSerializer.Deserialize<PipelineArtifactData>(json, _jsonOptions);
                return artifactData?.Value ?? new List<PipelineArtifact>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving pipeline artifacts.");
                throw;
            }
        }

        #endregion
    }
}
