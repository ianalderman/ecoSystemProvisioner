Connect-AzureAD

$appAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess" -ArgumentList "df021288-bdef-4463-88db-98f22de89214", "Scope"
#User.Read
$UserRead = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList "62a82d76-70ea-41e2-9197-370581804d09","Scope" 
#Group.ReadWrite.All
$GroupReadWriteAll = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList "62a82d76-70ea-41e2-9197-370581804d09","Scope" 
#Microsoft.Graph/EntitlementManagement.ReadWrite.All Delegated
$EntitlementManagementReadWriteAll = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList "ae7a573d-81d7-432b-ad44-4ed5c9d89038","Scope"
#Team.Create
$Teamcreate = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList "23fc2474-f741-46ce-8465-674744c5c361","Scope"
#Applications.ReadWrite.OwnedBy
$AppsRWOb = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList "18a4783c-866b-4cc7-a460-3d5e5662c884", "Scope"

$appAccess.ResourceAccess = $EntitlementManagementReadWriteAll,$Teamcreate,$AppsRWOb,$GroupReadWriteAll
$appAccess.ResourceAppId = "00000003-0000-0000-c000-000000000000" #Microsoft Graph API

#Get-AzureADApplication

#We need the below for Managed Identity rather than above for app
Set-AzureADApplication -ObjectId "" -RequiredResourceAccess $appAccess

$graphApp = Get-AzureADServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"

#get Role to read group objects
$userRead = $graphApp.AppRoles | where-Object {$_.Value -eq "User.Read.All"}
$groupReadWritePermission = $graphApp.AppRoles | where-Object {$_.Value -eq "Group.ReadWrite.All"}
$EntitlementManagementReadWriteAll = $graphApp.AppRoles | where-Object {$_.Value -eq "EntitlementManagement.ReadWrite.All"}
$Teamcreate = $graphApp.AppRoles | where-Object {$_.Value -eq "Team.Create"}
$AppsRWOb = $graphApp.AppRoles | where-Object {$_.Value -eq "Applications.ReadWrite.OwnedBy"}
$GroupCreate = $graphApp.AppRoles | where-Object {$_.Value -eq "Group.Create"}
#use the MSI from the Function App Creation
$msi = "4642fc0a-d7bb-40b1-8f9c-6ca733551b05"

New-AzureADServiceAppRoleAssignment -Id $groupReadWritePermission.Id -ObjectId $msi -PrincipalId $msi -ResourceId $graphApp.ObjectId
New-AzureADServiceAppRoleAssignment -Id $EntitlementManagementReadWriteAll.Id -ObjectId $msi -PrincipalId $msi -ResourceId $graphApp.ObjectId
New-AzureADServiceAppRoleAssignment -Id $Teamcreate.Id -ObjectId $msi -PrincipalId $msi -ResourceId $graphApp.ObjectId
New-AzureADServiceAppRoleAssignment -Id $AppsRWOb.Id -ObjectId $msi -PrincipalId $msi -ResourceId $graphApp.ObjectId
New-AzureADServiceAppRoleAssignment -Id $GroupCreate.Id -ObjectId $msi -PrincipalId $msi -ResourceId $graphApp.ObjectId
New-AzureADServiceAppRoleAssignment -Id $userRead.Id -ObjectId $msi -PrincipalId $msi -ResourceId $graphApp.ObjectId


