using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.Identity;
using System.Threading.Tasks;
using Microsoft.Graph;
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

using Microsoft.AspNetCore.Http;


namespace GingerDesigns.ecoSytemProvisioner
{

 
    /** Need to include process to email Security to set up buddy for new app **/
    public static class ecoSystemOrchestrator {
        [FunctionName("ecoSystemOrchestrator")]
        public static async Task<bool> Run(
            //[OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger, string appId, string appName, string ownerEmail, string budgetCode, string criticality)
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger logger)
        {
            ////var logger = executionContext.GetLogger("ecoSystemOrchestrator");
            logger.LogInformation("New EcoSystem Orchestration request");
            try {
                string appName = context.GetInput<ecoSystemRequest>()?.AppName;
                string ownerEmail = context.GetInput<ecoSystemRequest>()?.OwnerEmail;
                string catalogName = "Jaguar"; //Hard coded for now
                logger.LogDebug($"AppName:{appName}");

                AadGroupDefinition teamMembersGroupDef = new AadGroupDefinition(appName, false, false);
                teamMembersGroupDef.addOwners(ownerEmail);
                
                logger.LogDebug("Calling add Team Members AAD Group");
                var aadTeamMembersGroup = await context.CallActivityAsync<string>(nameof(createAADGroup),teamMembersGroupDef );
                
                AadGroupDefinition teamOwnersGroupDef = new AadGroupDefinition(appName, true, false);
                teamOwnersGroupDef.addOwners(ownerEmail);
                
                logger.LogDebug("Calling add Team Owners AAD Group");
                var aadTeamOwnersGroup = await context.CallActivityAsync<string>(nameof(createAADGroup), teamOwnersGroupDef);

                AccessPackageDefinition teamAccessPackageDef = new AccessPackageDefinition(appName, false);
                //await teamAccessPackageDef.SetCatalog(catalogName);

                var teamsAccessPackage = await context.CallActivityAsync<string>(nameof(createAccessPackage), teamAccessPackageDef);

                AccessPackageDefinition mgrAccessPackageDef = new AccessPackageDefinition(appName, true);
                //await mgrAccessPackageDef.SetCatalog(catalogName);

                var mgrAccessPackage = await context.CallActivityAsync<string>(nameof(createAccessPackage), teamAccessPackageDef);


            } catch {
                logger.LogError("Error in orchestrator");
                return false;
            }
            return true;
        }
    }

    
    
    public static class createAADGroup {
        [FunctionName("createAADGroup")]
        public static async Task<string> Run([ActivityTrigger] AadGroupDefinition groupDef, ILogger logger)
            {
                //var logger = executionContext.GetLogger("createAADGroup");
                logger.LogInformation("Processing Create AAD Group");

                GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

                var existingGroup = await graphServiceClient.Groups.Request().Filter($"displayName eq '{groupDef.Name}'").GetAsync();
                logger.LogDebug($"existing Group Count:{existingGroup.Count}");

                if (existingGroup.Count == 0) {
                    Group newGroup;

                    try {
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
                        }
                    } catch {
                        throw new Exception("Error creating group");
                    }

                    return newGroup.Id;
                } else {
                    if (existingGroup.Count == 1) {
                        return existingGroup[0].Id;
                    } else {
                        throw new Exception($"Duplicate existing matching groups found for {groupDef.Name}");
                    }
                }
            }
    }
    public static class addAADGroupToEntitlementManagementCatalog
    {
        [FunctionName("addAADGroupToEntitlementManagementCatalog")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req, string catalogId, string aadGroupId,
            ILogger logger)
        {
            //var logger = executionContext.GetLogger("addAADGroupToEntitlementManagementCatalog");
            logger.LogInformation("C# HTTP trigger function processed a request.");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

            AccessPackageResourceRequestObject newAccessPackage;

            try {
                var fetchedAadGroup = await graphServiceClient.Groups[aadGroupId].Request().GetAsync();

                var accessPackageResourceRequest = new AccessPackageResourceRequestObject {
                        CatalogId = catalogId,
                        RequestType = "AdminAdd",
                        Justification = "",
                        AccessPackageResource = new AccessPackageResource
                        {
                            DisplayName = fetchedAadGroup.DisplayName,
                            Description = fetchedAadGroup.Description,
                            ResourceType = "Group",
                            OriginId = aadGroupId,
                            OriginSystem = "AadGroup"
                        }
                    };

                    newAccessPackage = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageResourceRequests.Request().AddAsync(accessPackageResourceRequest);
            } catch {
                var errResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                return errResponse;
            }
            
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            return response;
        }
    }
    public static class createAccessPackage
    {
        [FunctionName("createAccessPackage")]
        public static async Task<string> Run([ActivityTrigger] AccessPackageDefinition accessPackageDef,
            ILogger logger)
        {
            //var logger = executionContext.GetLogger("createAccessPackage");
            logger.LogInformation("Processing Access Package Request");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();

            var existingPackage = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages.Request().Filter($"displayName eq '{accessPackageDef.DisplayName}'").GetAsync();
            
            if (existingPackage.Count == 0) {
                var catalog = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs.Request().Filter($"(displayName eq '{accessPackageDef.CatalogName}')").GetAsync();

                if (catalog.Count != 1) {
                    //AccessPackageCatalog newCatalog;
                    throw new System.Exception($"{accessPackageDef.CatalogName} not found");
                } else {
                    accessPackageDef.CatalogId = catalog[0].Id;
                }
                var accessPackage = new AccessPackage
                    {
                        CatalogId = accessPackageDef.CatalogId,
                        DisplayName = accessPackageDef.DisplayName,
                        Description = accessPackageDef.Description
                    };

                var newAccessPackage = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages
                .Request()
                .AddAsync(accessPackage);

                return newAccessPackage.Id;
            } else {
                if (existingPackage.Count == 1) {
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
        public async static Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req, string catalogId, string accessPackageId, string roleName, string originId, string originType, string originSystem,
            ILogger logger)
        {
            //var logger = executionContext.GetLogger("addResourceRoleToAccessPackage");
            logger.LogInformation("C# HTTP trigger function processed a request.");

            GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();   

            //catalogResourceRole = graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageResources
            //$resourceRole = invokeGraphAPI -call "beta/identityGovernance/entitlementManagement/accessPackageCatalogs/$($catalogId)/accessPackageResourceRoles?`$filter=(originSystem+eq+%27$($resourceType)%27+and+accessPackageResource/id+eq+%27$($catalogEntry.Id)%27)&`$expand=accessPackageResource" -body "" -Method "GET" | ConvertFrom-JSON
            /*
            var accessPackageResourceRoles = await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackageCatalogs["{accessPackageCatalog-id}"].AccessPackageResourceRoles
                .Request()
                .Filter("(originSystem eq 'AadGroup' and accessPackageResource/id eq 'a35bef72-a8aa-4ca3-af30-f6b2ece7208f')")
                .Expand("accessPackageResource/id%20eq%20'a35bef72-a8aa-4ca3-af30-f6b2ece7208f')")
                .GetAsync();
            */

            try {
                var accessPackageResourceRoleScope = new AccessPackageResourceRoleScope
                {
                    AccessPackageResourceRole = new AccessPackageResourceRole
                    {
                        OriginId = "{roleName}_{originId}",
                        DisplayName = roleName,
                        OriginSystem = originSystem,
                        AccessPackageResource = new AccessPackageResource
                        {
                            Id = accessPackageId,
                            ResourceType = originType,
                            OriginId = originId,
                            OriginSystem = originSystem
                        }
                    },
                    AccessPackageResourceScope = new AccessPackageResourceScope
                    {
                        OriginId = originId,
                        OriginSystem = originSystem
                    }
                };

                await graphServiceClient.IdentityGovernance.EntitlementManagement.AccessPackages["{accessPackageId}"].AccessPackageResourceRoleScopes
                    .Request()
                    .AddAsync(accessPackageResourceRoleScope);
            } catch {
                var errResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                return errResponse;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            return response;
        }    
    }
    public static class createServicePrincipal
    {
        [FunctionName("createServicePrincipal")]
        public static HttpResponseMessage Run([ActivityTrigger]  HttpRequest req,
            ILogger logger)
        {
            //**** See https://www.serverless360.com/blog/azure-service-principal-using-graph-client ****//
            //https://docs.microsoft.com/en-us/graph/api/serviceprincipal-addpassword?view=graph-rest-beta&tabs=http
            //https://stackoverflow.com/questions/64532020/how-to-use-microsoft-graph-api-for-assigning-role-to-the-user-in-azure-ad
            //*** GIT https://docs.microsoft.com/en-us/azure/developer/github/connect-from-azure **
            //var logger = executionContext.GetLogger("createServicePrincipal");
            logger.LogInformation("C# HTTP trigger function processed a request.");

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            //response.WriteString("Welcome to Azure Functions!");

            return response;
        }
    }

    public static class configureGitHub {
        [FunctionName("configureGitHub")]
        public static async Task<string> Run([ActivityTrigger] ILogger logger) {
            //In here add code to if local use env vars else connect to key vault to read PAT
            
        }
    }
    public static class createTeam {
        [FunctionName("createTeam")]
        public static HttpResponseMessage Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req,
            ILogger logger)
        {
            //**** See https://www.serverless360.com/blog/azure-service-principal-using-graph-client ****//
            //var logger = executionContext.GetLogger("createTeam");
            logger.LogInformation("C# HTTP trigger function processed a request.");

            var team = new Team {
                Visibility = TeamVisibilityType.Private,
                DisplayName = "Sample Engineering Team",
                Description = "This is a sample engineering team, used to showcase the range of properties supported by this API",
                Channels = new TeamChannelsCollectionPage()
                {
                    new Channel
                    {
                        DisplayName = "Announcements 📢",
                        IsFavoriteByDefault = true,
                        Description = "This is a sample announcements channel that is favorited by default. Use this channel to make important team, product, and service announcements."

                    }
                }
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            //response.WriteString("Welcome to Azure Functions!");

            return response;
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
        object eventData = await req.Content.ReadAsAsync<ecoSystemRequest>();
        string instanceId = await starter.StartNewAsync(functionName, eventData);

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return starter.CreateCheckStatusResponse(req, instanceId);
    }
}
}
