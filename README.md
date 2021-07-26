# EcoSystem Provisioner Demostrator #
This Durable function will create the following resources

- Team and Management Security Groups for given App Id
- Entitlement Package for Team members granting membership of Team Security Group and access to Microsoft Teams Team for App
- GitHub Team for App linked to Team Security Group
- GitHub Repo from Org Template assigned to the new GitHub Team
- Service Principal with contributor rights to a subscription
- Added Creds for Service Principal as secret to Repo
- Azure DevOps Project with Sample pipeline

## Configuring for your environment ##
In ecoSystemProvisioner.cs there are some variables which will need changing, again like many things at the moment these should be refactored out to either additional environment variables or runtime vars.

```
string catalogName = "Engineering"; //Hard coded for now
string orgName = "EgUnicorn"; // Hard coded for now
string orgRepoTemplate = "org-template"; // Hard coded for now
string myaccessLink = "@egunicorn.co.uk" ; // Hard coded for now
```

- `catalogName` refers to the name of the Entitlement Management Catalog in which to provision the Access Packages and their resources
- `orgName` refers to the GitHub Organisation in which to provision the repos
- `orgRepoTemplate` refers to the GitHub Repository which we will use as the template for the new repository
- `myaccessLink` is used to build the URI for the myaccess link for users

## Environment Settings

| Name | Comment
| --- | --- |
| KEY_VAULT_NAME | Used to store the Azure DevOps Personal Access Token, as code is refactored other secrets will move here
| AzureDevOpsOrg | Used to define the Azure DevOps Organisation to create Projects in etc.
| GITHUB_PAT | Stores the GitHub Personal Access Token, note this should move to Key Vault at some point
| AZURE_SPN_ID | This is the Object Id of the Managed Identity used for the Durable Function
| AZURE_SUBSCRIPTION | Used to define the Subscription in which the Service Principal will be created - in a final solution this should be defined at run time to reflect multiple Subscriptions as possible targets, indeed potentially an earlier step would be to create a new Subscription
| AZURE_CLIENT_ID | The DefaultAzureCredential class should be able to use Managed Identity in testing it wasn't picked up so have specified all 3.  
| AZURE_TENANT_ID | The DefaultAzureCredential class should be able to use Managed Identity in testing it wasn't picked up so have specified all 3. 
| AZURE_CLIENT_SECRET | The DefaultAzureCredential class should be able to use Managed Identity in testing it wasn't picked up so have specified all 3. 
| FLOW_URL | The Endpoint for a Power Automate task to send approval emails.

### GitHub Note ###
The GitHub Personal Access Token (PAT) will need access to create Repositories, Teams and Secrets

### Managed Identity Note ###
The Managed Identity will be made an owner of any Azure Active Directory Groups which are created, this is because certain operations are blocked unless the caller is one of the Group Owners.

The Managed Identity will also need various Graph permissions assigning see Provisioning notes.ps1

### Azure DevOps Note ###
For the Azure DevOps integration you need a secret called ADOPAT in the defined key vault containing a Azure Dev Ops Personal Access Token (PAT) with suitable permissions to create projects, service connections and pipelines.

## Running the Durable Function ##
There are two Orchestrators currently in the code
- `ecoSystemOrchestrator` use this to provision the full set of capabailities listed above
- `ecoSystemDevOpsOrchestrator` this simple orchestrator provisions just the Azure DevOps Project, Service Connection and pipeline.  Along with an Azure Active Directory (AAD) Group and GitHub Repo with an AAD Sync'd Team.

The original demo utilised a simple Microsoft Form to collect input from a "Product Owner" to kick off a Power Automate based Approval Workflow, once approved the flow calls the `ecoSystemOrchestrator` Orchestrator within the durable function.

### Request Body ###
You can access the Durable Function via Postman with the body below:
```
{
    "appId": "1234",
    "appName": "Test App 5",
    "ownerEmail": "ija@acme.co",
    "budgetCode": "IA01234",
    "criticality": "Mission Critical",
    "pii": "Maybe",
    "audience": "[\"Partners\"]",
    "gitHubOrg": "AcmeCorp"

}
```

