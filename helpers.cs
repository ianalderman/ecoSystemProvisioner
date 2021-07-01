extern alias V1Lib;
using V1Graph = V1Lib.Microsoft.Graph;

using System.Collections.Generic;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Azure.Identity;
using System.Threading.Tasks;
using Microsoft.Graph;
using Newtonsoft.Json;
using System;
using System.IO;

namespace GingerDesigns.ecoSytemProvisioner
{
#region Models
    
     [JsonObject(MemberSerialization.OptIn)]
    public class ecoSystem {
        [JsonProperty("appId")]
        public string AppId {get; set;}
        [JsonProperty("appName")]
        public string AppName {get; set;}
        [JsonProperty("githubRepo")]
        public string GitHubRepo {get; set;}
        [JsonProperty("aadTeamGroupId")]
        public string AadTeamGroupId {get; set;}  
        [JsonProperty("aadMgrGroupId")]
        public string AadMgrGroupId {get; set;} 
        [JsonProperty("accPkgTeam")]
        public string AccPkgTeam {get; set;}
        [JsonProperty("accPkgMgr")]
        public string AccPkgMgr {get; set;}  
        [JsonProperty("ownerEmail")]
        public string OwnerEmail {get; set;}

        [FunctionName(nameof(ecoSystem))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
        => ctx.DispatchAsync<ecoSystem>();
    }

     [JsonObject(MemberSerialization.OptIn)]
    public class EcoSystemRequest {
        [JsonProperty("appId")]
        public string AppId {get; set;}
        [JsonProperty("appName")]
        public string AppName {get; set;}
        [JsonProperty("ownerEmail")]
        public string OwnerEmail {get; set;}
        [JsonProperty("budgetCode")]
        public string BudgetCode {get; set;}  
        [JsonProperty("criticality")]
        public string Criticality {get; set;}
        [JsonProperty("pii")]
        public string PII {get; set;}
        [JsonProperty("audience")]
        public string Audience {get; set;}
        [JsonProperty("gitHubOrg")]
        public string GitHubOrg {get; set;}              
        [JsonProperty("gitHubRepo")]
        public string GitHubRepo {get; set;}
        [JsonProperty("msftTeam")]
        public string MSFTTeam {get; set;}          
        [JsonProperty("accessPackageLink")]
        public string AcccessPackageLink {get; set;}  
    }
    
    public class AadGroupDefinition {
        public string AppName {get;}
        //public string Description {get; set;}

        public string GroupType {get; }
        public bool ManagerGroup {get;}
        //public IGroupOwnersCollectionWithReferencesPage Owners {get; set;}
        public List<string> ownersList {get; set;}
        public string Description {
            get {
                    if (GroupType == "") {
                        if (ManagerGroup) {
                            return $"Security group for managing the {this.AppName} application.";
                        } else {
                            return $"Security group for the {this.AppName} application.";
                        }
                        
                    } else {
                        return $"Group enabling collaboration for the {this.AppName} application.";
                    }
            }
        }

        public string Name {
            get {
                if (ManagerGroup) {
                    return $"{this.AppName} Team Managers";
                } else {
                    return $"{this.AppName} Team";
                }
            }            
        }

        public string MailNickname {
            get {
                if (ManagerGroup) {
                    return $"{this.AppName.Replace(" ","")}mgrs";
                } else {
                    return $"{this.AppName.Replace(" ","")}mbrs";
                }
            }
        }

        public string[] Owners {
            get {
                return this.ownersList.ToArray();
            }
        }
        public AadGroupDefinition(string appName, bool managerGroup, bool collab) {
            this.AppName = appName;
            this.ManagerGroup = managerGroup;
            this.ownersList = new List<string>();

            if (collab) {
                this.GroupType = "Unified";
            } else {
                this.GroupType = "";
            }
        }

        public void addOwners(string ownerUPNs) {
            //GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();
            
            string[] arrUPNs = ownerUPNs.Split(";");

            foreach(string ownerUPN in arrUPNs) {
                /*
                try {
                    var owner = await graphServiceClient.Users[$"{ownerUPN}"].Request().GetAsync();
                    this.Owners.Add(new DirectoryObject {Id = owner.Id});

                } catch {
                        throw new ArgumentException("Invalid UPN supplied as owner, multiple owners should be seperated by a semicolin (;)");
                }
                */
                this.ownersList.Add(ownerUPN);
            }
        }
    }

    public class AccessPackageDefinition {
        public string AppName {get; set;}
        public string CatalogId {get; set;}
        public string CatalogName {get; set;}
        public bool ManagerGroup {get; set;}
        public AccessPackageDefinition(string appName, bool managerGroup, string catalogId) {
            this.AppName = appName;
            this.ManagerGroup = managerGroup;
            this.CatalogId = catalogId;
        }

        public string DisplayName {
            get {
                if (this.ManagerGroup) {
                    return $"{this.AppName} application Team Management";
                } else {
                    return $"{this.AppName} application Team";
                }
            }
        }

        public string Description {
            get {
                if (this.ManagerGroup) {
                    return $"Access package providing access for managing the {this.AppName} application.";
                } else {
                    return $"Access package providing access for using / contributing to the {this.AppName} application";
                }
            }
        }
    }

    public class AccessPackageCatalogResource {
        public string CatalogId {get;set;}
        public string GroupId {get; set;}

        public AccessPackageCatalogResource(string catalogId, string groupId) {
            this.CatalogId = catalogId;
            this.GroupId = groupId;
        }
    }

    public class AccessPackageResourceToAdd {
        public string CatalogId {get; set;}
        public string ResourceId {get; set;}
        public string AccessPackageId {get; set;}
        public string RoleName {get; set;}
        public string DisplayName {get; set;}
        public string OriginType {get; set;}
        public string OriginSystem {get; set;}
        public string OriginId {get; set;}

        public AccessPackageResourceToAdd(string catalogId, string resourceId, string accessPackageId, string roleName, string displayName, string originType, string originSystem, string originId) {
            this.CatalogId = catalogId;
            this.ResourceId = resourceId;
            this.AccessPackageId = accessPackageId;
            this.RoleName = roleName;
            this.DisplayName = displayName;
            this.OriginType = originType;
            this.OriginSystem = originSystem;
            this.OriginId = originId;
        }


    }

    public class TeamDefinition {
        public string TeamName {get; set;}
        public List<string> ownersList {get; set;}

        public TeamDefinition(string teamName) {
            this.TeamName = teamName;
            this.ownersList = new List<string>();
            //this.addOwners(owners);
        }

        public void addOwners(string ownerUPNs) {
            //GraphServiceClient graphServiceClient = graphClientBuilder.getGraphClient();
            
            string[] arrUPNs = ownerUPNs.Split(";");

            foreach(string ownerUPN in arrUPNs) {
                /*
                try {
                    var owner = await graphServiceClient.Users[$"{ownerUPN}"].Request().GetAsync();
                    this.Owners.Add(new DirectoryObject {Id = owner.Id});

                } catch {
                        throw new ArgumentException("Invalid UPN supplied as owner, multiple owners should be seperated by a semicolin (;)");
                }
                */
                this.ownersList.Add(ownerUPN);
            }
        }

    }

    public class GitHubTeamDefinition {
        public string Name {get; set;}
        public string Organisation {get; set;}

        public string AppName {get; set;}
        public string AadGroupId {get; set;}
        public string AadGroupName {get; set;}
        public string AadGroupDescription {get; set;}
        public string RepoName {get; set;}
        public string RepoOrg {get; set;}

        public GitHubTeamDefinition(string name, string organisation, string appName, string aadGroupId, string aadGroupName, string aadGroupDescription, string repoName, string repoOrg) {
            this.Name = name;
            this.Organisation = organisation;
            this.AppName = appName;
            this.AadGroupId = aadGroupId;
            this.AadGroupName = aadGroupName;
            this.AadGroupDescription = aadGroupDescription;
            this.RepoName = repoName;
            this.RepoOrg = repoOrg;
        }
    }

     public class GitHubIDPPatchMessage {
            public GitHubIDPGroup[] groups {get; set;}

    }

    public class GitHubIDPGroup{
        public string group_id {get; set;}
        public string group_name {get; set;}
        public string group_description {get; set;}
    }

    public class GitHubRepoDefinition {
        public string Org {get; set;}
        public string Name {get; set;}
        public string Description {get; set;}

        public string TemplateName {get; set;}

        public GitHubRepoDefinition(string org, string name, string description, string templateName = "") {
            this.Org = org;
            this.Name = name;
            this.Description = description;
            this.TemplateName = templateName;

        }
    }

    public class GitHubRepoFromTemplateMessage {
        //public string template_owner {get; set;}
        //public string template_repo {get; set;}
        public string name {get; set;}
        public string description {get; set;}
        public string owner {get; set;}
        public bool include_all_branches {get; set;}

        //public GitHubRepoFromTemplateMessageMediaType mediaType {get; set;}

    }

    public class GitHubRepoFromTemplateMessageMediaType {
        public string[] previews {get; set;}
    }

/*
    public class AddGroupOwner {
        public string GroupId {get; set;}
        public string AppName {get; set;}

        public AddGroupOwner(string groupId, string appName) {
            this.GroupId = groupId;
            this.AppName = appName;
        }
    }
    */

    public class ServicePrincipalDefinition {
        public bool AddToRepo {get; set;}
        public string RepoName {get; set;}
        public string Name {get; set;}
        public string RepoOrg {get; set;}
        public string SubscriptionId {get; set;}
        public ServicePrincipalDefinition(string name, bool addToRepo = false, string repoName = "", string repoOrg = "", string subscriptionId = "") {
            this.AddToRepo = addToRepo;
            this.RepoName = repoName;
            this.Name = name;
            this.RepoOrg = repoOrg;
            this.SubscriptionId = subscriptionId;
        }
    }

    /*
    public class GitHubRepoPublicKeyRequest {
        public string owner {get; set;}
        public string repo {get; set;}
    }
    */

    public class GitHubEncryptedSecret {
        public string owner {get; set;}
        public string repo {get; set;}
        public string key_id {get; set;}
        public string secret_name {get; set;}
        public string encrypted_value {get; set;}

    }
    public class GitHubRepoPublicKeyResponse {
        public string key_id {get; set;}
        public string key {get; set;}
    }

    public class ServicePrincipalRoleAssignment {
        public ServicePrincipalRoleAssignmentDefinition properties {get; set;}
    }

    public class ServicePrincipalRoleAssignmentDefinition {
        public string roleDefinitionId {get; set;}
        public string principalId {get; set;}
        public string principalType {get; set;}
    }

    public class AccessPackagePolicyDefinition {
        public string AccessPackageId {get; set;}
        public string Owner {get; set;}
        public string AppName {get; set;}
        
        public AccessPackagePolicyDefinition(string accessPackageId, string owner, string appName) {
            this.AccessPackageId = accessPackageId;
            this.Owner = owner;
            this.AppName = appName;
        }
    }
#endregion
#region Extension / Helper Classes
    public static class GroupExtension
{
	public static void AddMember(this Microsoft.Graph.Group group, string userId)
	{
		if (group.AdditionalData == null)
		{
			group.AdditionalData = new Dictionary<string, object>();
		}

		string[] membersToAdd = new string[1];
		membersToAdd[0] = $"https://graph.microsoft.com/v1.0/users/{userId}";
		group.AdditionalData.Add("members@odata.bind", membersToAdd);
	}

	public static void AddOwner(this Microsoft.Graph.Group group, string userId)
	{
		if (group.AdditionalData == null)
		{
			group.AdditionalData = new Dictionary<string, object>();

            string[] ownersToAdd = new string[1];
		    ownersToAdd[0] = $"https://graph.microsoft.com/v1.0/users/{userId}";
		    group.AdditionalData.Add("owners@odata.bind", ownersToAdd);
		} else {
            string[] existingOwners = (string[])group.AdditionalData["owners@odata.bind"];

            string[] newOwnersToAdd = new string[existingOwners.Length + 1];

            newOwnersToAdd[0] = $"https://graph.microsoft.com/v1.0/users/{userId}";

            for (int i = 0; i < existingOwners.Length; i++) {
                newOwnersToAdd[i + 1] = existingOwners[i];
            };
            group.AdditionalData["owners@odata.bind"] =  newOwnersToAdd;
        }		
	}
}  

 public static class DotEnv
    {
        //https://dusted.codes/dotenv-in-dotnet
        public static void Load(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return;

            foreach (var line in System.IO.File.ReadAllLines(filePath))
            {
                if (line == "") {
                    continue;
                }

                var parts = line.Split(
                    '=',
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != 2) {
                    //continue;
                    int i = 1;
                    string val = "";
                    foreach(string part in parts) {
                        if (i > 1) {
                            if (i == 2) {
                                val += part;
                            } else {
                                val += "=" + part;
                            }
                            
                        }
                        i++;
                    }
                    Environment.SetEnvironmentVariable(parts[0], val);
                } else {
                    Environment.SetEnvironmentVariable(parts[0], parts[1]);
                }
            }
        }
    }

    static class graphClientBuilder {
        public static GraphServiceClient getGraphClient() {
            var dotenv = Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".env");
            DotEnv.Load(dotenv);

            var config = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
    //DefaultAzureCredential
            //var credential = new ChainedTokenCredential(new EnvironmentCredential(), 
            //    new ManagedIdentityCredential(string.IsNullOrEmpty(config["UserAssignedIdentity"])
            //        ? null 
            //        : config["UserAssignedIdentity"]),
            //    new AzureCliCredential());
            var credential = new DefaultAzureCredential();

            var token = credential.GetToken(
                new Azure.Core.TokenRequestContext(
                    new[] { "https://graph.microsoft.com/.default" }));
            
            var accessToken = token.Token;
            var graphServiceClient = new GraphServiceClient(
                new DelegateAuthenticationProvider((requestMessage) =>
                {
                    requestMessage
                    .Headers
                    .Authorization = new AuthenticationHeaderValue("bearer", accessToken);

                    return Task.CompletedTask;
                }));
            return graphServiceClient;
        }
    }

    static class v1GraphClientBuilder {
        public static V1Graph.GraphServiceClient getGraphClient() {
            var dotenv = Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".env");
            DotEnv.Load(dotenv);

            var config = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
    //DefaultAzureCredential
            //var credential = new ChainedTokenCredential(new EnvironmentCredential(), 
            //    new ManagedIdentityCredential(string.IsNullOrEmpty(config["UserAssignedIdentity"])
            //        ? null 
            //        : config["UserAssignedIdentity"]),
            //    new AzureCliCredential());

            string managedIdentityClientId = Environment.GetEnvironmentVariable("UserAssignedIdentity", EnvironmentVariableTarget.Process);
            var options = new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId };
            var credential = new DefaultAzureCredential(options);

            var token = credential.GetToken(
                new Azure.Core.TokenRequestContext(
                    new[] { "https://graph.microsoft.com/.default" }));
            
            var accessToken = token.Token;
            var graphServiceClient = new V1Graph.GraphServiceClient(
                new DelegateAuthenticationProvider((requestMessage) =>
                {
                    requestMessage
                    .Headers
                    .Authorization = new AuthenticationHeaderValue("bearer", accessToken);

                    return Task.CompletedTask;
                }));
            return graphServiceClient;
        }
    }

    static class AzureManagementToken {
        public static string getAzureManagementToken() {
            var dotenv = Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".env");
            DotEnv.Load(dotenv);

            var config = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
    //DefaultAzureCredential
            //var credential = new ChainedTokenCredential(new EnvironmentCredential(), 
            //    new ManagedIdentityCredential(string.IsNullOrEmpty(config["UserAssignedIdentity"])
            //        ? null 
            //        : config["UserAssignedIdentity"]),
            //    new AzureCliCredential());

            
            string managedIdentityClientId = Environment.GetEnvironmentVariable("UserAssignedIdentity", EnvironmentVariableTarget.Process);
            var options = new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId };
            var credential = new DefaultAzureCredential(options);
            var token = credential.GetToken(
                new Azure.Core.TokenRequestContext(
                    new[] { "https://management.azure.com/.default" }));
            
            return token.Token;
        }
    }
#endregion
}