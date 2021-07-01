# EcoSystem Provisioner Demostrator #
This Durable function will create the following resources

- Team and Management Security Groups for given App Id
- Entitlement Package for Team members granting membership of Team Security Group and access to Microsoft Teams Team for App
- GitHub Team for App linked to Team Security Group
- GitHub Repo from Org Template assigned to the new GitHub Team
- Service Principal with contributor rights to a subscription
- Added Creds for Service Principal as secret to Repo

