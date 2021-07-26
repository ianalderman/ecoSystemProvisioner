extern alias V1Lib;
using V1Graph = V1Lib.Microsoft.Graph;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.Graph;
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using Octokit;
using System.Linq;
using Sodium;
using System.IO;
using System.Threading;
using System.Text.Encodings;


namespace GingerDesigns.ecoSytemProvisioner
{

    public static class ecoSystemOrchestrator {
        [FunctionName("ecoSystemOrchestrator")]
        public static async Task<bool> Run(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {
            logger.LogInformation("New EcoSystem Orchestration request");


            try {
                string appName = context.GetInput<EcoSystemRequest>()?.AppName;
                string ownerEmail = context.GetInput<EcoSystemRequest>()?.OwnerEmail;
                string catalogName = "Engineering"; //Hard coded for now
                string orgName = "EgUnicorn"; // Hard coded for now
                string orgRepoTemplate = "org-template"; // Hard coded for now
                string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION"); // Hard coded for now
                string myaccessLink = "@egunicorn.co.uk" ; // Hard coded for now

                var retryOptions = new RetryOptions(
                    firstRetryInterval: TimeSpan.FromSeconds(5),
                    maxNumberOfAttempts: 5
                );
                retryOptions.BackoffCoefficient = 2;

                //Add Security Group to contain the Application Team Members
                AadGroupDefinition teamMembersGroupDef = new AadGroupDefinition(appName, false, false);
                teamMembersGroupDef.addOwners(ownerEmail);
                
                logger.LogDebug("Calling add Team Members AAD Group");
                var aadTeamMembersGroupId = await context.CallActivityAsync<string>(nameof(createAADGroup),teamMembersGroupDef );
                
                //Add Security Group to contain the Application Team Managers
                AadGroupDefinition teamOwnersGroupDef = new AadGroupDefinition(appName, true, false);
                teamOwnersGroupDef.addOwners(ownerEmail);
                
                logger.LogDebug("Calling add Team Owners AAD Group");
                var aadTeamOwnersGroupId = await context.CallActivityAsync<string>(nameof(createAADGroup), teamOwnersGroupDef);

                //Look up the Catalog Id for the Entitlement Packages
                var catalogId = await context.CallActivityAsync<string>(nameof(getCatalogId), catalogName);

                //Add the Application Team Members group to the Entitlement Management Catalog
                AccessPackageCatalogResource teamGroupResourceReq = new AccessPackageCatalogResource(catalogId, aadTeamMembersGroupId);
                logger.LogDebug($"Adding {teamMembersGroupDef.Name} to catalog {catalogName}");
                var aadTeamMemberGroupResourceRequest = await context.CallActivityWithRetryAsync<string>(nameof(addAADGroupToEntitlementManagementCatalog), retryOptions, teamGroupResourceReq);

                //Add the Application Team Managers group to the Entitlement Management Catalog
                AccessPackageCatalogResource mgrGroupResourceReq = new AccessPackageCatalogResource(catalogId, aadTeamOwnersGroupId);
                mgrGroupResourceReq.CatalogId = catalogId;
                mgrGroupResourceReq.GroupId = aadTeamOwnersGroupId;
                logger.LogDebug($"Adding {teamOwnersGroupDef.Name} to catalog {catalogName}");
                string aadTeamManagerGroupResourceRequest = await context.CallActivityWithRetryAsync<string>(nameof(addAADGroupToEntitlementManagementCatalog), retryOptions, mgrGroupResourceReq);

                //Create the Access Package for the Application Team Members
                AccessPackageDefinition teamAccessPackageDef = new AccessPackageDefinition(appName, false, catalogId);
                var teamsAccessPackageId = await context.CallActivityWithRetryAsync<string>(nameof(createAccessPackage), retryOptions, teamAccessPackageDef);

                //Create an Access Policy for the Application Team Members Application Package
                AccessPackagePolicyDefinition teamsAccessPackagePolDef = new AccessPackagePolicyDefinition(teamsAccessPackageId,ownerEmail, appName );
                var teamsAccessPackagePolicyId = await context.CallActivityWithRetryAsync<string>(nameof(addAccesPolicyToAccessPackage), retryOptions, teamsAccessPackagePolDef);
              
                //Create the Access Package for the Application Team Managers
                AccessPackageDefinition mgrAccessPackageDef = new AccessPackageDefinition(appName, true, catalogId);
                mgrAccessPackageDef.CatalogId = catalogId;
                var mgrAccessPackageId = await context.CallActivityAsync<string>(nameof(createAccessPackage), mgrAccessPackageDef);
                
                //Valid Group Types: Group / O365 Group
                //Add the Application Team Members Security Group Catalog Entry to the Access Package add as Members
                AccessPackageResourceToAdd teamAccessPackageAADGroup = new AccessPackageResourceToAdd(catalogId, aadTeamMemberGroupResourceRequest, teamsAccessPackageId, "Member", teamMembersGroupDef.Name, "Group", "AadGroup", aadTeamMembersGroupId);
                var teamsAccessPackageAadGroupAdd = await context.CallActivityWithRetryAsync<string>(nameof(addResourceRoleToAccessPackage), retryOptions, teamAccessPackageAADGroup);

                //Create the Microsoft Teams Team
                var teamsDef = new TeamDefinition($"{appName} Engineering Team");
                teamsDef.addOwners(ownerEmail);
                var teamCreated = await context.CallActivityAsync<bool>(nameof(createTeam), teamsDef);

                //Lookup the underlying Microsoft Teams Team Security Group created for the above Team
                var microsoftTeamGroupId = await context.CallActivityWithRetryAsync<string>(nameof(getMicrosoftTeamsGroup), retryOptions, teamsDef.TeamName);
                //Add the Ecosystem app as an owner to the group (We cannot manage groups we do not own under Graph)
                var addAppOwner = await context.CallActivityWithRetryAsync<bool>(nameof(addAppToGroupOwners),retryOptions, microsoftTeamGroupId );
                //Add the Microsoft Teams Team Security Group to the Entitlement Management Catalog
                AccessPackageCatalogResource teamUnifiedGroupResourceReq = new AccessPackageCatalogResource(catalogId, microsoftTeamGroupId);
                logger.LogDebug($"Adding {teamMembersGroupDef.Name} to catalog {catalogName}");
                var aadMicrosofotTeamMemberGroupResourceRequest = await context.CallActivityWithRetryAsync<string>(nameof(addAADGroupToEntitlementManagementCatalog), retryOptions, teamUnifiedGroupResourceReq);

                //Add the Microsoft Teams Team Security Group Catalog Entry to the Application Team Members Access Package as Members
                AccessPackageResourceToAdd teamAccessPackageMSFTTeamsAADGroup = new AccessPackageResourceToAdd(catalogId, aadMicrosofotTeamMemberGroupResourceRequest, teamsAccessPackageId, "Member", teamsDef.TeamName, "O365 Group", "AadGroup", microsoftTeamGroupId);
                var teamsAccessPackageMicrosoftTeamsAadGroupAdd = await context.CallActivityWithRetryAsync<string>(nameof(addResourceRoleToAccessPackage), retryOptions, teamAccessPackageMSFTTeamsAADGroup);

                //Create a default GitHub Repo for the Application
                var repoDef = new GitHubRepoDefinition(orgName, appName.Replace(" ", "-"), $"Source code for {appName}", orgRepoTemplate);
                var gitHubRepo = await context.CallActivityAsync<bool>(nameof(addGitHubRepo), repoDef);

                //Create a GitHub Team sync'd to the Application Team Members Security Group
                var gitHubTeamDefinition = new GitHubTeamDefinition(teamMembersGroupDef.Name, orgName, appName, aadTeamMembersGroupId, teamMembersGroupDef.Name, teamMembersGroupDef.Description, repoDef.Name, repoDef.Org);
                var gitHubTeam = await context.CallActivityAsync<int>(nameof(addGitHubTeam), gitHubTeamDefinition);

                //Create a Service Principal for the Application
                ServicePrincipalDefinition spDef = new ServicePrincipalDefinition($"spn-for-{appName}", true, repoDef.Name, repoDef.Org, subscriptionId);
                var svcP = await context.CallActivityWithRetryAsync<bool>(nameof(createServicePrincipal), retryOptions, spDef);

                var  ecoSystemRequest = context.GetInput<EcoSystemRequest>();
                var adoProjectId = await context.CallActivityWithRetryAsync<string>(nameof(createADOProject), retryOptions, ecoSystemRequest);

                AzureDeveOpsServiceConnectionRequest azureDeveOpsServiceConnectionRequest = new  AzureDeveOpsServiceConnectionRequest(appName, "GitHub", adoProjectId);
                var adoServiceConnectionId = await context.CallActivityAsync<string>(nameof(createADOServiceConnectionForProject),azureDeveOpsServiceConnectionRequest );

                AzureDevOpsPipelineRequest newPipelineRequest = new AzureDevOpsPipelineRequest(appName, adoServiceConnectionId, "egUnicorn/AzureDevOpsPipelineTemplate", "template1.yaml");
                var adoPipelineId = await context.CallActivityAsync<string>(nameof(createADOPipeline), newPipelineRequest);

                //Send confirmation Emails
                var ecoSystemReq = new EcoSystemRequest() {
                    AppId = context.GetInput<EcoSystemRequest>()?.AppId,
                    AppName = context.GetInput<EcoSystemRequest>()?.AppName,
                    OwnerEmail = context.GetInput<EcoSystemRequest>()?.OwnerEmail,
                    BudgetCode = context.GetInput<EcoSystemRequest>()?.BudgetCode,
                    Criticality = context.GetInput<EcoSystemRequest>()?.Criticality,
                    Audience = context.GetInput<EcoSystemRequest>()?.Audience,
                    GitHubOrg = orgName,
                    GitHubRepo = repoDef.Name,
                    MSFTTeam = teamsDef.TeamName,
                    AcccessPackageLink = $"https://myaccess.microsoft.com{myaccessLink}#/access-packages/{teamsAccessPackageId}"
                };

                var emailsSent = await context.CallActivityAsync<bool>(nameof(sendConfirmationEmails), ecoSystemReq);
                logger.LogInformation($"EcoSystem Provisioner completed for {appName}");
            } catch (Exception ex) {
                logger.LogError($"Error in orchestrator {ex.Message}");
                return false;
            }
            return true;
        }
    }
    
     public static class ecoSystemDevOpsOrchestrator {
        [FunctionName("ecoSystemDevOpsOrchestrator")]
        public static async Task<bool> Run(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {   
            string appName = context.GetInput<EcoSystemRequest>()?.AppName;
            string orgName = "EgUnicorn"; // Hard coded for now
            string orgRepoTemplate = "org-template"; // Hard coded for now
            string ownerEmail = context.GetInput<EcoSystemRequest>()?.OwnerEmail;
            EcoSystemRequest ecoSystemRequest = context.GetInput<EcoSystemRequest>();
            
            var retryOptions = new RetryOptions(
                    firstRetryInterval: TimeSpan.FromSeconds(5),
                    maxNumberOfAttempts: 5
                );
            retryOptions.BackoffCoefficient = 2;

            //Add Security Group to contain the Application Team Members
            AadGroupDefinition teamMembersGroupDef = new AadGroupDefinition(appName, false, false);
            teamMembersGroupDef.addOwners(ownerEmail);
                
            logger.LogDebug("Calling add Team Members AAD Group");
            var aadTeamMembersGroupId = await context.CallActivityAsync<string>(nameof(createAADGroup),teamMembersGroupDef );

            //Create a default GitHub Repo for the Application
            var repoDef = new GitHubRepoDefinition(orgName, appName.Replace(" ", "-"), $"Source code for {appName}", orgRepoTemplate);
            var gitHubRepo = await context.CallActivityAsync<bool>(nameof(addGitHubRepo), repoDef);

            //Create a GitHub Team sync'd to the Application Team Members Security Group
            var gitHubTeamDefinition = new GitHubTeamDefinition(teamMembersGroupDef.Name, orgName, appName, aadTeamMembersGroupId, teamMembersGroupDef.Name, teamMembersGroupDef.Description, repoDef.Name, repoDef.Org);
            var gitHubTeam = await context.CallActivityAsync<int>(nameof(addGitHubTeam), gitHubTeamDefinition);


            var adoProjectId = await context.CallActivityWithRetryAsync<string>(nameof(createADOProject), retryOptions, ecoSystemRequest);

            AzureDeveOpsServiceConnectionRequest azureDeveOpsServiceConnectionRequest = new  AzureDeveOpsServiceConnectionRequest(appName, "GitHub", adoProjectId);
            var adoServiceConnectionId = await context.CallActivityAsync<string>(nameof(createADOServiceConnectionForProject),azureDeveOpsServiceConnectionRequest );

            AzureDevOpsPipelineRequest newPipelineRequest = new AzureDevOpsPipelineRequest(appName, adoServiceConnectionId, "egUnicorn/AzureDevOpsPipelineTemplate", "template1.yaml");
            var adoPipelineId = await context.CallActivityAsync<string>(nameof(createADOPipeline), newPipelineRequest);
            return true;
        }
     }
    public static class getMicrosoftTeamsGroup {
        [FunctionName("getMicrosoftTeamsGroup")]
        public static async Task<string> Run([ActivityTrigger] string teamName, ILogger logger) {
            logger.LogInformation($"Looking up Microsoft 365 Group for Team {teamName}");

            try {
                GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();
                var existingGroup = await graphServiceClient.Groups.Request().Filter($"displayName eq '{teamName}'").GetAsync();
                
                if (existingGroup.Count == 1) {
                    return existingGroup[0].Id;
                }

                if (existingGroup.Count > 1) {
                    throw new Exception("Multiple potential group matches found, aborting.");
                }

                if (existingGroup.Count == 0) {
                    throw new Exception("Unable to find matching group for Team");
                }
                return "";
            } catch (Exception ex) {
                throw new Exception($"Unable to locate Microsoft Teams group for team: {teamName}.  Error: {ex.Message}");
            }
          
        }
    }   
    public static class addAppToGroupOwners {
        [FunctionName("addAppToGroupOwners")]
        public static async Task<bool> Run([ActivityTrigger] string groupId, ILogger logger) {
            logger.LogInformation($"Processing request to add app as owner on Group Id {groupId}");

            try {
                GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();
                string appId = "";

                if(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("UserAssignedIdentity"))) {
                    appId = Environment.GetEnvironmentVariable("UserAssignedIdentity");
                } else {
                    appId = Environment.GetEnvironmentVariable("AZURE_SPN_ID");
                }

                var existingOwners = await graphServiceClient.Groups[groupId].Owners.Request().GetAsync();

                if (existingOwners.Count > 0) {
                    foreach(var owner in existingOwners.CurrentPage) {
                        Type t = owner.GetType();
                        System.Reflection.PropertyInfo ownerAppId = t.GetProperty("AppId") ;
                        if (ownerAppId != null) {
                            if (owner.Id == appId) {
                                logger.LogInformation("App already an owner on group, aborting");
                                return true;
                            }
                        }
                    }
                }

                var directoryObject = await graphServiceClient.DirectoryObjects[appId].Request().GetAsync();
                
                if (directoryObject is null) {
                    throw new Exception("Unable to locate directory object for this app");
                }
                await graphServiceClient.Groups[groupId].Owners.References.Request().AddAsync(directoryObject);

                return true;
            } catch (Exception ex) {
                throw new Exception($"Error adding owner: {ex.Message}");
            }
        }
    }
    public static class createAADGroup {
        [FunctionName("createAADGroup")]
        public static async Task<string> Run([ActivityTrigger] AadGroupDefinition groupDef, ILogger logger)
            {
                logger.LogInformation($"Processing Create AAD Group: {groupDef.Name} for App {groupDef.AppName}");
                GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

                try {
                    var existingGroup = await graphServiceClient.Groups.Request().Filter($"displayName eq '{groupDef.Name}'").GetAsync();
                    logger.LogDebug($"existing Group Count:{existingGroup.Count}");
                
                    if (existingGroup.Count == 0) {
                        Group newGroup;

                        try {
                            //In order to add to an access package as an application we need the app to be an owner too...
                            string appId = "";

                            if(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("UserAssignedIdentity"))) {
                                appId = Environment.GetEnvironmentVariable("UserAssignedIdentity");
                            } else {
                                appId = Environment.GetEnvironmentVariable("AZURE_SPN_ID");
                            }

                            var directoryObject = await graphServiceClient.DirectoryObjects[appId].Request().GetAsync();

                            if (groupDef.GroupType == "Unified") {
                                var group = new Group
                                {
                                    Description = groupDef.Description,
                                    DisplayName = groupDef.Name,
                                    GroupTypes = new List<String>()
                                    {
                                        "Unified"
                                    },
                                    MailEnabled = true,
                                    MailNickname = groupDef.MailNickname,
                                    SecurityEnabled = false
                                };
                            
                                foreach(string upn in groupDef.Owners) {
                                    group.AddOwner(upn);
                                }
                                
                                newGroup = await graphServiceClient.Groups
                                .Request()
                                .AddAsync(group);
                                logger.LogInformation($"Added Unified Group {groupDef.Name} for app {groupDef.AppName}");
                            } else {
                                var group = new Group
                                {
                                    Description = groupDef.Description,
                                    DisplayName = groupDef.Name,
                                    MailEnabled = false,
                                    MailNickname = groupDef.MailNickname,
                                    SecurityEnabled = true
                                };

                                foreach(string upn in groupDef.Owners) {
                                    group.AddOwner(upn);
                                }
                                
                                newGroup = await graphServiceClient.Groups
                                .Request()
                                .AddAsync(group);
                                logger.LogInformation($"Added Group {groupDef.Name} for app {groupDef.AppName}");
                            }
                            //Need to avoid race conditions on the retry, sleeping
                            Thread.Sleep(15000);
                            await graphServiceClient.Groups[newGroup.Id].Owners.References.Request().AddAsync(directoryObject);
                            logger.LogInformation($"Added EcoSystem App as owner for {groupDef.Name} for app {groupDef.AppName}");
                        } catch (Exception ex) {
                            throw new Exception($"Error creating group: {ex.Message}");
                        }

                        return newGroup.Id;
                    } else {
                        if (existingGroup.Count == 1) {
                            logger.LogInformation($"Group {groupDef.Name} for app {groupDef.AppName} already exists");
                            return existingGroup[0].Id;
                        } else {
                            throw new Exception($"Duplicate existing matching groups found for {groupDef.Name}");
                        }
                    }
                } catch (Exception ex) {
                    throw new Exception($"Error creating Group: {ex.Message}");
                }
            }
    }
    public static class addAADGroupToEntitlementManagementCatalog
    {
        [FunctionName("addAADGroupToEntitlementManagementCatalog")]
        public static async Task<string> Run([ActivityTrigger] AccessPackageCatalogResource ackPkgResource,
            ILogger logger)
        {
            logger.LogInformation($"Executing addAADGroupToEntitlementManagementCatalog for Group {ackPkgResource.GroupId} and Catalog {ackPkgResource.CatalogId}");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

            AccessPackageResourceRequestObject newAccessPackageResourceRequest;

            try {
                var existingResource = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs[ackPkgResource.CatalogId].AccessPackageResources.Request().Filter($"originId eq '{ackPkgResource.GroupId}'").GetAsync();

                if (existingResource.Count == 0) {
                    var fetchedAadGroup = await graphServiceClient.Groups[ackPkgResource.GroupId].Request().WithMaxRetry(5).GetAsync();
                    var accessPackageResourceRequest = new AccessPackageResourceRequestObject {
                            CatalogId = ackPkgResource.CatalogId,
                            RequestType = "AdminAdd",
                            Justification = "",
                            AccessPackageResource = new AccessPackageResource
                            {
                                DisplayName = fetchedAadGroup.DisplayName,
                                Description = fetchedAadGroup.Description,
                                ResourceType = "Group",
                                OriginId = ackPkgResource.GroupId,
                                OriginSystem = "AadGroup"
                            }
                        };

                        newAccessPackageResourceRequest = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageResourceRequests.Request().WithMaxRetry(5).AddAsync(accessPackageResourceRequest);
                        //Thread.Sleep(10000);
                        //we get an ID back for the request, what we need to do is now get the actual Id assigned
                        var newResource = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs[ackPkgResource.CatalogId].AccessPackageResources.Request().Filter($"originId eq '{ackPkgResource.GroupId}'").GetAsync();
                        if (newResource.Count == 0) {
                            throw new Exception($"Group {ackPkgResource.GroupId} has not completed provisioning, please retry");
                        } else {
                            return newResource[0].Id;
                        }
                        
                } else {
                    if (existingResource.Count == 1) {
                        logger.LogInformation($"Resource already created in Catalog");
                        return existingResource[0].Id;
                    } else {
                        throw new Exception("Duplicate existing resources found");
                    }
                }
            } catch {
                throw new Exception("Error adding to catalog");
            }
            
           
        }
    }
    public static class addAccesPolicyToAccessPackage {
        [FunctionName("addAccesPolicyToAccessPackage")]
        public static async Task<string> Run([ActivityTrigger] AccessPackagePolicyDefinition accessPkgPolDef, ILogger logger) {
            try {
                logger.LogInformation ($"Adding Access Package Policy for {accessPkgPolDef.AccessPackageId}");
                GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

                var existingPolicy = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages[accessPkgPolDef.AccessPackageId].Request().Expand("AccessPackageAssignmentPolicies").GetAsync();
                if (existingPolicy.AccessPackageAssignmentPolicies != null) {
                    if (existingPolicy.AccessPackageAssignmentPolicies.Count == 1) {
                        logger.LogInformation("Access Package Policy already created");
                        return existingPolicy.AccessPackageAssignmentPolicies[0].Id;
                    }

                    if (existingPolicy.AccessPackageAssignmentPolicies.Count> 1) {
                        throw new Exception ("Multiple access policies already defined!");
                    }
                }
                
                var owner = await graphServiceClient.Users[accessPkgPolDef.Owner].Request().GetAsync();
            
                var accessPol = new AccessPackageAssignmentPolicy {
                    AccessPackageId = accessPkgPolDef.AccessPackageId,
                    DisplayName = $"Manage Access for {accessPkgPolDef.AppName}",
                    Description = $"Enable Self-Service to join the {accessPkgPolDef.AppName} team",
                    AccessReviewSettings = null,
                    RequestorSettings = new RequestorSettings
                    {
                        ScopeType = "AllExistingDirectorySubjects",
                        AcceptRequests = true
                    },
                    RequestApprovalSettings = new ApprovalSettings
                    {
                        IsApprovalRequired = true,
                        IsApprovalRequiredForExtension = false,
                        IsRequestorJustificationRequired = false,
                        ApprovalMode = "SingleStage",
                        ApprovalStages = new List<ApprovalStage>()
                        {
                            new ApprovalStage
                            {
                                ApprovalStageTimeOutInDays = 14,
                                IsApproverJustificationRequired = true,
                                IsEscalationEnabled = false,
                                EscalationTimeInMinutes = 0,
                                PrimaryApprovers = new List<UserSet>()
                                {
                                    new SingleUser
                                    {
                                        ODataType = "#microsoft.graph.singleUser",
                                        IsBackup = false,
                                        Id = owner.Id,
                                        Description = "App Owner"
                                    }
                                }
                            }
                        }
                    },
                    Questions = new List<AccessPackageQuestion>()
                    {
                        new AccessPackageMultipleChoiceQuestion 
                        {
                            IsRequired = true,
                            Text = new AccessPackageLocalizedContent
                            {
                                DefaultText = "Please confirm you will abide by EgUnicorn's Safe Development Lifecycle and comply with our Security & Privacy policies"
                            },
                            Choices = new List<AccessPackageAnswerChoice>() 
                            {
                                new AccessPackageAnswerChoice
                                {
                                    ActualValue = "Yes",
                                    DisplayValue = new AccessPackageLocalizedContent
                                    {
                                        LocalizedTexts = new List<AccessPackageLocalizedText>()
                                        {
                                            new AccessPackageLocalizedText
                                            {
                                                Text = "Yes",
                                                LanguageCode = "en"
                                            }
                                        }
                                    }
                                },
                                new AccessPackageAnswerChoice
                                {
                                    ActualValue = "No",
                                    DisplayValue = new AccessPackageLocalizedContent
                                    {
                                        LocalizedTexts = new List<AccessPackageLocalizedText>()
                                        {
                                            new AccessPackageLocalizedText
                                            {
                                                Text = "No",
                                                LanguageCode = "en"
                                            }
                                        }
                                    }
                                }
                            }
                        }  
                    }
                };
                
                var accessPolRequest = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageAssignmentPolicies.Request().AddAsync(accessPol);
                logger.LogInformation("Access Policy created");
                return accessPolRequest.Id;
            } catch (Exception ex) {
                throw new Exception ($"Error adding Access Policy to package: {ex.Message}");
            }
        }
    }
    public static class getCatalogId {
        [FunctionName("getCatalogId")]
        public static async Task<string> Run([ActivityTrigger] string catalogName, ILogger logger) {
            logger.LogInformation("Retrieving catalog Id");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

            var catalog = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs.Request().Filter($"(displayName eq '{catalogName}')").GetAsync();

            if (catalog.Count != 1) {
                throw new System.Exception($"{catalogName} not found");
            } else {
                return catalog[0].Id;
            }
        }
    }
    public static class createAccessPackage
    {
        [FunctionName("createAccessPackage")]
        public static async Task<string> Run([ActivityTrigger] AccessPackageDefinition accessPackageDef,
            ILogger logger)
        {
            logger.LogInformation($"Processing Access Package Request for {accessPackageDef.AppName}");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

            var existingPackage = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages.Request().Filter($"displayName eq '{accessPackageDef.DisplayName}'").WithMaxRetry(5).GetAsync();
            
            if (existingPackage.Count == 0) {
                var accessPackage = new AccessPackage
                    {
                        CatalogId = accessPackageDef.CatalogId,
                        DisplayName = accessPackageDef.DisplayName,
                        Description = accessPackageDef.Description
                    };

                var newAccessPackage = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages
                .Request()
                .AddAsync(accessPackage);
                logger.LogInformation("Access Package Created");
                return newAccessPackage.Id;
            } else {
                if (existingPackage.Count == 1) {
                    logger.LogInformation("Existing Access Package Found");
                    return existingPackage[0].Id;
                } else {
                    throw new Exception("Duplicate Access Packages already exist unable to identify correct Id");
                }
            }
        }
    }
    public static class addResourceRoleToAccessPackage
    {
        [FunctionName("addResourceRoleToAccessPackage")]
        public async static Task<string> Run([ActivityTrigger] AccessPackageResourceToAdd accessPackageResource, ILogger logger)
        {
            logger.LogInformation($"Processing access Package Resource Request");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();               

            try {
                var accessPackageResources = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs[accessPackageResource.CatalogId].AccessPackageResources.Request().Filter($"originId eq '{accessPackageResource.OriginId}'").GetAsync();

                if (accessPackageResources.Count > 1) {
                    throw new Exception ("Duplicate access package catalog resources found");
                }

                if (accessPackageResources.Count == 0) {
                    throw new Exception("Unable to locate matching Access package catalog resource");
                }

                var accessPackageResourceRoles = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs[accessPackageResource.CatalogId].AccessPackageResourceRoles
                    .Request()
                    .Filter($"(originSystem eq 'AadGroup' and accessPackageResource/id eq '{accessPackageResource.ResourceId}' and displayName eq '{accessPackageResource.RoleName}')")
                    .Expand($"accessPackageResource")
                    .GetAsync();

                if (accessPackageResourceRoles.Count != 1) {
                    throw new Exception("Multiple potential resource roles found, unable to assign to access package");
                } else {

                    var catalogResource = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs[accessPackageResource.CatalogId].AccessPackageResources
                        .Request()
                        .Filter($"(displayName eq '{accessPackageResource.DisplayName}' and originSystem eq '{accessPackageResource.OriginSystem}')")
                        .WithMaxRetry(5)
                        .GetAsync();

                    if (catalogResource.Count != 1) {
                        throw new Exception("Unable to identify Catalog Resource to add");
                    }
                    
                    var accessPackageResourceRoleScope = new AccessPackageResourceRoleScope
                    {
                        AccessPackageResourceRole = new AccessPackageResourceRole
                        {
                            OriginId = accessPackageResourceRoles[0].OriginId,
                            DisplayName = accessPackageResourceRoles[0].DisplayName,
                            OriginSystem = accessPackageResourceRoles[0].OriginSystem,
                            AccessPackageResource = new AccessPackageResource
                            {
                                Id = catalogResource[0].Id,
                                ResourceType = accessPackageResource.OriginType,
                                OriginId = catalogResource[0].OriginId,
                                OriginSystem = catalogResource[0].OriginSystem
                            }
                        },
                        AccessPackageResourceScope = new AccessPackageResourceScope
                        {
                            OriginId = catalogResource[0].OriginId,
                            OriginSystem = catalogResource[0].OriginSystem
                        }
                    };

                   
                    var responseId = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages[accessPackageResource.AccessPackageId].AccessPackageResourceRoleScopes.Request().AddAsync(accessPackageResourceRoleScope);
                    logger.LogInformation($"Role Scope added for {accessPackageResource.DisplayName} role {accessPackageResource.RoleName}");
                    return responseId.Id;
                }
            } catch(Exception ex) {
                if (ex.Message == "Code: ResourceNotFound") {
                    throw new Exception($"Possible replication issue");
                }
                throw new Exception($"Error adding resource to access package: {ex.Message}");
            }
        }    
    }
    public static class createServicePrincipal
    {
        [FunctionName("createServicePrincipal")]
        public static async Task<bool> Run([ActivityTrigger]  ServicePrincipalDefinition svcPrincipalDef,
            ILogger logger)
        {
            //**** See https://www.serverless360.com/blog/azure-service-principal-using-graph-client ****//
            //https://docs.microsoft.com/en-us/graph/api/serviceprincipal-addpassword?view=graph-rest-beta&tabs=http
            //https://stackoverflow.com/questions/64532020/how-to-use-microsoft-graph-api-for-assigning-role-to-the-user-in-azure-ad
            //*** GIT https://docs.microsoft.com/en-us/azure/developer/github/connect-from-azure **
            //var logger = executionContext.GetLogger("createServicePrincipal");
            logger.LogInformation("Processing Service Principal Request...");

            try {
                V1Graph.GraphServiceClient graphServiceClient = v1GraphClientBuilder.getGraphClient();

                var existingApp = await graphServiceClient.Applications.Request().Filter($"(displayName eq 'Automation for the {svcPrincipalDef.Name} App')").GetAsync();
                var newAppId = "";

                if (existingApp.Count == 1) {
                    logger.LogInformation($"Application for {svcPrincipalDef.Name} already exists");
                    newAppId = existingApp[0].AppId;
                }

                if (existingApp.Count > 1) {
                    throw new Exception("Multiple matching names for App Id found, aborting");
                }

                if (existingApp.Count == 0) {
                    var app = new V1Graph.Application {
                        DisplayName = $"Automation for the {svcPrincipalDef.Name} App"
                    };

                    var newApp = await graphServiceClient.Applications.Request().AddAsync(app);
                    newAppId = newApp.Id;
                    logger.LogInformation($"Created new Application for {svcPrincipalDef.Name}");
                    //Need to pause we end up in a race condition where the SP fails as it can't find the app.  The retries kick in and can't find the app so recreate it
                    Thread.Sleep(10000);
                }
                
                var existingSP = await graphServiceClient.ServicePrincipals.Request().Filter($"AppId eq '{newAppId}'").GetAsync();

                if (existingSP.Count == 1) {
                    //throw new Exception("Service Principal already exists, aborting");
                    logger.LogInformation($"Service Principal {svcPrincipalDef.Name} already exists");
                    return true;
                }

                if (existingSP.Count > 1) {
                    throw new Exception("Multiple potential matches found for SP, aborting");
                }

                if (existingSP.Count == 0) {
                    var svcPrincipal = new V1Graph.ServicePrincipal {
                        AppId = newAppId
                    };

                    var newSp = await graphServiceClient.ServicePrincipals.Request().AddAsync(svcPrincipal);
                    logger.LogInformation($"Service Principal {svcPrincipalDef.Name} created");
                    var passwordCredential = new V1Graph.PasswordCredential
                    {
                        DisplayName = "Password friendly name"
                    };
                    
                    var addPasswordResponse = await graphServiceClient.ServicePrincipals[newSp.Id].AddPassword(passwordCredential).Request().PostAsync();
                    logger.LogInformation($"Service Principal {svcPrincipalDef.Name} password added");
                    //*** We need to configure the GitHub Secret here, we don't want to pass it back to the Orchestrator as there is a risk it will be stored as part of the durable functions serialization **/
                    var client = new GitHubClient(new Octokit.ProductHeaderValue("EcoSystemProvisioner"));
                    var tokenAuth = new Credentials(System.Environment.GetEnvironmentVariable("GITHUB_PAT")); // NOTE: need to move to akv
                    client.Credentials = tokenAuth;

                    var getURI = new System.UriBuilder($"https://api.github.com/repos/{svcPrincipalDef.RepoOrg}/{svcPrincipalDef.RepoName}/actions/secrets/public-key");
                    string accepts = "application/vnd.github.v3+json";
                    
                    Dictionary<string, string> getReq = new Dictionary<string, string>();
                    getReq.Add("owner", svcPrincipalDef.RepoOrg);
                    getReq.Add("repo", svcPrincipalDef.RepoName);
                    var publicKeyRequest = await client.Connection.Get<GitHubRepoPublicKeyResponse>(getURI.Uri, getReq, accepts);

                
                    var secretValue = System.Text.Encoding.UTF8.GetBytes(addPasswordResponse.SecretText);
                    var publicKey = Convert.FromBase64String(publicKeyRequest.Body.key);
                    var sealedPublicKeyBox = Sodium.SealedPublicKeyBox.Create(secretValue, publicKey);

                    GitHubEncryptedSecret newSecret = new GitHubEncryptedSecret();
                    newSecret.encrypted_value = Convert.ToBase64String(sealedPublicKeyBox);
                    newSecret.key_id = publicKeyRequest.Body.key_id;
                    newSecret.secret_name = svcPrincipalDef.Name.Replace(" ","_").Replace("-", "_");
                    newSecret.owner = svcPrincipalDef.RepoOrg;
                    newSecret.repo = svcPrincipalDef.RepoName;

                    var putURI = new System.UriBuilder($"https://api.github.com/repos/{svcPrincipalDef.RepoOrg}/{svcPrincipalDef.RepoName}/actions/secrets/{newSecret.secret_name}");
                    
                    var secretAddRequest = await client.Connection.Put<string>(putURI.Uri, newSecret ); 
                    logger.LogInformation($"Service Principal Secret added to Repo {svcPrincipalDef.RepoName}");
                    //https://docs.microsoft.com/en-us/azure/role-based-access-control/role-assignments-rest
                    string restURI = $"https://management.azure.com/subscriptions/{svcPrincipalDef.SubscriptionId}/providers/Microsoft.Authorization/roleAssignments/{Guid.NewGuid()}?api-version=2018-09-01-preview";
                    WebRequest addToContributorRole = WebRequest.Create(restURI);
                    addToContributorRole.Headers.Add("Authorization", $"Bearer {AzureManagementToken.getAzureManagementToken()}");
                    addToContributorRole.Method = "PUT";
                    addToContributorRole.ContentType = "application/json";

                    ServicePrincipalRoleAssignment spRoleA = new ServicePrincipalRoleAssignment();
                    ServicePrincipalRoleAssignmentDefinition spRoleDef = new ServicePrincipalRoleAssignmentDefinition();

                    spRoleDef.principalId = newSp.Id;
                    spRoleDef.roleDefinitionId = $"/subscriptions/{svcPrincipalDef.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c";
                    spRoleDef.principalType = "ServicePrincipal";
                    spRoleA.properties = spRoleDef;

                    using (var streamWriter = new StreamWriter(addToContributorRole.GetRequestStream())){
                        string json = $"{{\"properties\": {{ \"roleDefinitionId\": \"/subscriptions/{svcPrincipalDef.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c\",\"principalId\":\"{newSp.Id}\", \"principalType\": \"ServicePrincipal\"}}}}";
                        streamWriter.Write(json.ToString());
                        streamWriter.Flush();
                    }

                    var httpResponse = await addToContributorRole.GetResponseAsync();
                    logger.LogInformation($"Service Principal added as Contributor");
                }
            } catch (Exception ex) {
                throw new Exception($"Error adding Service Principal: {ex.Message}");
            }
            return true;
        }
    }
    public static class addGitHubTeam {
        [FunctionName("addGitHubTeam")]
        public static async Task<int> Run([ActivityTrigger] GitHubTeamDefinition newTeamDef, ILogger logger) {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("EcoSystemProvisioner"));
            var tokenAuth = new Credentials(System.Environment.GetEnvironmentVariable("GITHUB_PAT")); // NOTE: need to move to akv
            client.Credentials = tokenAuth;

            try {
                logger.LogInformation($"Processing addGitHubTeam for {newTeamDef.Name}");
            
                var gitTeams = await client.Organization.Team.GetAll(newTeamDef.Organisation);

                var existingTeam = gitTeams.Where(t => t.Name == newTeamDef.Name).ToList();
            
                if (existingTeam.Count() > 1) {
                    throw new Exception("Multiple potential team matches aborting");
                }

                if (existingTeam.Count() == 1) {
                    logger.LogInformation("Existing GitHub Team found no further changes will be processed");
                    return existingTeam[0].Id;
                }

                Octokit.NewTeam newTeam = new Octokit.NewTeam(newTeamDef.Name);
                newTeam.Description = $"Engineering Team supporting the {newTeamDef.AppName} application";
                newTeam.Privacy = TeamPrivacy.Closed;
                var org = await client.Organization.Get(newTeamDef.Organisation);
                
                var newTeamResult = await client.Organization.Team.Create(newTeamDef.Organisation, newTeam);
                logger.LogInformation($"New GitHub Team created:{newTeamDef.Name}");

                var addRepoResult = await client.Organization.Team.AddRepository(newTeamResult.Id, newTeamDef.Organisation, newTeamDef.RepoName);
                logger.LogInformation($"Added {newTeamDef.RepoName} to Team {newTeamDef.Name}");

                var patchURI = new System.UriBuilder($"https://api.github.com/organizations/{org.Id}/team/{newTeamResult.Id}/team-sync/group-mappings");

                var patchBody = new GitHubIDPPatchMessage();

                GitHubIDPGroup[] newMapping = new GitHubIDPGroup[1];
                newMapping[0] = new GitHubIDPGroup();
                newMapping[0].group_id = newTeamDef.AadGroupId;
                newMapping[0].group_name = newTeamDef.AadGroupName;
                newMapping[0].group_description = newTeamDef.AadGroupDescription;

                patchBody.groups = newMapping;
                
                string accepts = "application/vnd.github.v3+json";
                
                var newMappingResult = await client.Connection.Patch<Octokit.Team>(patchURI.Uri, patchBody, accepts);
                logger.LogInformation($"Group Sync for {newTeamDef.Name} configured");
                return newTeamResult.Id;
            } catch (Exception ex) {
                throw new Exception($"Error adding GitHub Team: {ex.Message}");
            }
        }
    }
    public static class addGitHubRepo {
        [FunctionName("addGitHubRepo")]
        public static async Task<bool> Run([ActivityTrigger] GitHubRepoDefinition repoDef, ILogger logger) {
            var client = new GitHubClient(new Octokit.ProductHeaderValue("EcoSystemProvisioner"));
            var tokenAuth = new Credentials(System.Environment.GetEnvironmentVariable("GITHUB_PAT")); // NOTE: need to move to akv
            client.Credentials = tokenAuth;

            logger.LogInformation($"Processing new Repo request for {repoDef.Name} in org {repoDef.Org}");
            try {

            
                var gitRepos = await client.Repository.GetAllForOrg(repoDef.Org);

                var existingRepo = gitRepos.Where(t => t.Name == repoDef.Name).ToList();
                
                if (existingRepo.Count() > 1) {
                    throw new Exception("Multiple potential repo matches aborting");
                }

                if (existingRepo.Count() == 1) {
                    logger.LogInformation("Existing GitHub Repo found no further changes will be processed");
                    return true;
                }
            
            
                logger.LogInformation($"No existing repository named {repoDef.Name} in org {repoDef.Org}");

                string accepts = "application/vnd.github.baptiste-preview+json";
                GitHubRepoFromTemplateMessage newRepoFromTemplate = new GitHubRepoFromTemplateMessage();
                newRepoFromTemplate.name = repoDef.Name;
                newRepoFromTemplate.description = repoDef.Description;
                newRepoFromTemplate.include_all_branches = false;
                newRepoFromTemplate.owner = repoDef.Org;
                newRepoFromTemplate.Private = true;
                GitHubRepoFromTemplateMessageMediaType repoMT = new GitHubRepoFromTemplateMessageMediaType();
                string[] p = new string[1];
                p[0] = "baptiste";

                repoMT.previews = p;

                var postURI = new System.UriBuilder($"https://api.github.com/repos/{repoDef.Org}/{repoDef.TemplateName}/generate");

                var newRepoRequest = await client.Connection.Post(postURI.Uri, newRepoFromTemplate, accepts);
                
                logger.LogInformation($"New Repo {repoDef.Name} created in {repoDef.Org} from template {repoDef.TemplateName}");

                return true;
            } catch (Exception ex) {
                throw new Exception($"Error adding repo: {ex.Message}");
            }
        }
    }
    public static class createTeam {
        [FunctionName("createTeam")]
        public async static Task<bool> Run([ActivityTrigger] TeamDefinition teamDef,
            ILogger logger)
        {
            //**** See https://www.serverless360.com/blog/azure-service-principal-using-graph-client ****//
            //var logger = executionContext.GetLogger("createTeam");
            logger.LogInformation("Processing Create Team");

            try {
                GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();   
                var existingTeam = await graphServiceClient.Groups.Request()
                .Filter($"(displayName eq '{teamDef.TeamName}')")
                .GetAsync();
                
                if (existingTeam.Count == 1) {
                    logger.LogInformation("Team already exists");
                    return true;
                } else {
                    if (existingTeam.Count > 1) {
                        throw new Exception("Multiple potential existing teams found");
                    }
                }

                //Due to binding issues to standard template created my own and set channels there rather than here
                var teamOwners = new Dictionary<string, object>();

                 foreach(string owner in teamDef.ownersList) {
                    var ownerObj = await graphServiceClient.Users[owner].Request().GetAsync();
                    if(ownerObj.Id != "") {
                        teamOwners.Add("user@odata.bind", $"https://graph.microsoft.com/v1.0/users('{ownerObj.Id}')");
                    } else {
                        throw new Exception("Unable to locate User Object for Owner");
                    }
                    
                };
                

                var team = new Microsoft.Graph.Team {
                    Visibility = TeamVisibilityType.Public,
                    DisplayName = $"{teamDef.TeamName}",
                    Description = $"Collaboration workspace for the {teamDef.TeamName} Engineering Team",
                    Channels = new TeamChannelsCollectionPage()
                    {
                        new Channel
                        {
                            DisplayName = "GitHub",
                            IsFavoriteByDefault = true
                        },
                        new Channel
                        {
                            DisplayName = "Incidents",
                            IsFavoriteByDefault = true
                        }
                    },
                    Members = new TeamMembersCollectionPage()
                    {
                        new AadUserConversationMember
                        {
                            Roles = new List<String>()
                            {
                                "owner"
                            },
                            AdditionalData = teamOwners
                        }
                    },
                    AdditionalData = new Dictionary<string, object>() 
                    {
                        {"template@odata.bind", "https://graph.microsoft.com/beta/teamsTemplates('standard')"}
                    }
                };

                //{"template@odata.bind", "https://graph.microsoft.com/beta/teamsTemplates('Engineering App Team')"}
                //Below would create from an existing group
                /*var team = new Team
                {
                    AdditionalData = new Dictionary<string, object>()
                    {
                        {"template@odata.bind", "https://graph.microsoft.com/beta/teamsTemplates('standard')"},
                        {"group@odata.bind", $"https://graph.microsoft.com/beta/groups('{aadGroupId}')"}
                    }
                };
                */
                var newTeam = await graphServiceClient.Teams.Request().AddAsync(team);
                logger.LogInformation($"Team {teamDef.TeamName} created");
                Thread.Sleep(15000);
                return true;
            } catch(Exception ex) {
                throw new Exception($"Error creating Team: {ex}");
            }
        } 
    }
    public static class createADOProject {
        [FunctionName("createADOProject")]
        public static string Run([ActivityTrigger] EcoSystemRequest request, ILogger logger) {
            try {

                var existingProject = ADOAPIClient.runAPICommand("GET", "projects");

                if (!String.IsNullOrEmpty(existingProject)) {
                    dynamic projects = JsonConvert.DeserializeObject(existingProject);
                    foreach(var project in (IEnumerable<dynamic>)projects.value) {
                        if (request.AppName == project.name.ToString()) {
                            return project.id.ToString();
                        }
                    }
                }

                string jsonData = $"{{\"name\":\"{request.AppName}\", \"description\": \"Azure DevOps Project for {request.AppName}\", \"capabilities\": {{ \"versioncontrol\": {{ \"sourceControlType\": \"Git\" }}, \"processTemplate\": {{\"templateTypeId\":\"adcc42ab-9882-485e-a3ed-7678f01f66bc\" }}}}}}";
                string newProjectRequestResponse = ADOAPIClient.runAPICommand("POST", "projects", "",jsonData, "6.1-preview.4");
                dynamic newProjectRequest = JsonConvert.DeserializeObject(newProjectRequestResponse);

                while (newProjectRequest.status != "succeeded") {
                    if (newProjectRequest.status == "cancelled" || newProjectRequest.status == "failed") {
                        throw new Exception($"Unable to create new project returned operation status: {newProjectRequest.status}");
                    }
                    Thread.Sleep(2000);
                    newProjectRequest = JsonConvert.DeserializeObject(ADOAPIClient.runAPICommand("GET", $"operations/{newProjectRequest.id}","" ,"","6.1-preview.1"));
                }

                var newProject = ADOAPIClient.runAPICommand("GET", "projects");
                if (!String.IsNullOrEmpty(newProject)) {
                    dynamic projects = JsonConvert.DeserializeObject(newProject);
                    foreach(var project in (IEnumerable<dynamic>)projects.value) {
                        if (request.AppName == project.name.ToString()) {
                            return project.id.ToString();
                        }
                    }
                }
                //Unreachable code in the land of happy paths... in the world of sad paths we seem to hit this when the endpoint the GET hits hasn't replicated the new project ID.  Calling the activity with retry should overcome this.
                throw new Exception("Failed to create / identify new Project Id");
            } catch (Exception ex) {
                throw new Exception($"Error creating Azure DevOps Project: {ex.Message}");
            }
        }
    }

    public static class createADOServiceConnectionForProject {
        [FunctionName("createADOServiceConnectionForProject")]
        public static async Task<string> Run([ActivityTrigger] AzureDeveOpsServiceConnectionRequest request, ILogger logger) {
            try {
                var existingServiceConnections = ADOAPIClient.runAPICommand("GET", "serviceendpoint/endpoints", request.AppName);

                if (!String.IsNullOrEmpty(existingServiceConnections)) {
                    dynamic serviceConnections = JsonConvert.DeserializeObject(existingServiceConnections);
                    foreach(var serviceConnection in (IEnumerable<dynamic>)serviceConnections.value) {
                        if ($"{request.AppName} GitHub Service Connection (PAT)" == serviceConnection.name.ToString()) {
                            return serviceConnection.id.ToString();
                        }
                    }
                }
                string endPointName = $"{request.AppName} GitHub Service Connection (PAT)";
                string jsonData = $"{{\"name\":\"{endPointName}\", \"type\": \"github\", \"url\": \"https://github.com\", \"authorization\": {{\"scheme\": \"PersonalAccessToken\", \"parameters\": {{\"accessToken\":\"{Environment.GetEnvironmentVariable("GITHUB_PAT")}\" }}}}, \"isShared\": false, \"isReady\": true, \"serviceEndpointProjectReferences\": [{{\"projectReference\": {{\"id\": \"{request.AzureDevOpsProjectId}\", \"name\":\"{request.AppName}\"}}, \"name\":\"{endPointName}\"}}]}}";
                string newServiceConnectionReqeustReponse = ADOAPIClient.runAPICommand("POST", "serviceendpoint/endpoints", "", jsonData);
                dynamic newServiceConnection = JsonConvert.DeserializeObject(newServiceConnectionReqeustReponse);

                return newServiceConnection.id.ToString();

                //Unreachable code in the land of happy paths...
                throw new Exception("Failed to create / identify Service Connection");
               
            } catch (Exception ex) {
                throw new Exception($"Error creating Service Connection for  DevOps Project: {ex.Message}");
            }
        }
    }
    public static class createADOPipeline {
        [FunctionName("createADOPipeline")]
        public static async Task<string> Run([ActivityTrigger] AzureDevOpsPipelineRequest request, ILogger logger) {
            try {
                var existingPipeline = ADOAPIClient.runAPICommand("GET", "pipelines", request.AppName, "", "6.0-preview.1");

                if (!String.IsNullOrEmpty(existingPipeline)) {
                    dynamic pipelines = JsonConvert.DeserializeObject(existingPipeline);
                    foreach(var pipeline in (IEnumerable<dynamic>)pipelines.value) {
                        if ($"{request.AppName} Pipeline" == pipeline.name.ToString()) {
                            return pipeline.id.ToString();
                        }
                    }
                }

                string jsonData = $"{{\"folder\": null, \"name\":\"{request.AppName} Pipeline\", \"configuration\": {{\"type\":\"yaml\", \"path\":\"{request.TemplateFileName}\",\"repository\": {{\"FullName\":\"{request.TemplateRepoName}\", \"type\": \"gitHub\", \"Connection\": {{\"id\":\"{request.ServiceConnectionId}\"}}}}}}}}";
                string newPipelineRequest = ADOAPIClient.runAPICommand("POST", "pipelines", request.AppName, jsonData, "6.0-preview.1");
                dynamic newPipeline = JsonConvert.DeserializeObject(newPipelineRequest);

                return newPipeline.id.ToString();

                //Unreachable code in the land of happy paths...
                throw new Exception("Failed to create / identify Service Connection");
            } catch (Exception ex) {
                throw new Exception($"Error creating Azure DevOps Pipeline: {ex.Message}");
            }
        }
    }

    public static class sendConfirmationEmails{
        [FunctionName("sendConfirmationEmails")]
        public static async Task<bool> Run([ActivityTrigger] EcoSystemRequest request, ILogger logger ) {
            try {
                logger.LogInformation($"Calling Flow for sending confirmation emails for {request.AppName}");
                //This generates a warning re deterministic compliance, however the URL at this stage has the token in which we don't want recorded in the log file
                string restURI = Environment.GetEnvironmentVariable("FLOW_URL");
                WebRequest sendEmails = WebRequest.Create(restURI);
                sendEmails.Method = "POST";
                sendEmails.ContentType = "application/json; charset=utf-8";
                var jsonout = System.Text.Json.JsonSerializer.Serialize(request);
                using (var streamWriter = new StreamWriter(sendEmails.GetRequestStream())){
                    streamWriter.Write(System.Text.Json.JsonSerializer.Serialize(request));
                    streamWriter.Flush();
                }

                var httpResponse = await sendEmails.GetResponseAsync();
                logger.LogInformation($"Flow called for sending confirmation emails for {request.AppName}");
                return true;    
            } catch (Exception ex) {
                throw new Exception($"Error sending confirmation emails: {ex.Message}");
            }
        }   
    }
    public static class HttpStart
{
    [FunctionName("HttpStart")]
    public static async Task<HttpResponseMessage> Run(
        [HttpTrigger(AuthorizationLevel.Function, methods: "post", Route = "orchestrators/{functionName}")] HttpRequestMessage req,
        [DurableClient] IDurableClient starter,
        string functionName,
        ILogger log)
    {
        // Function input comes from the request content.
        object eventData = await req.Content.ReadAsAsync<EcoSystemRequest>();
        string instanceId = await starter.StartNewAsync(functionName, eventData);

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return starter.CreateCheckStatusResponse(req, instanceId);
    }
}
}