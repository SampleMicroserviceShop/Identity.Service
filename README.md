## Identity.Microservice
Sample Microservice Shop Identity microservice.

## General Variables
```powershell
$version="1.0.4"
$contracts_version="1.0.2"
$owner="SampleMicroserviceShop"
$gh_pat="[PAT HERE]"
$adminPass="[PASSWORD HERE]"
$cosmosDbConnString="[CONN STRING HERE]"
$serviceBusConnString="[CONN STRING HERE]"
```

## Create and publish package
```powershell
dotnet pack --configuration Release -p:PackageVersion=$version -o ..\..\packages\$owner
```

 ## Add the GitHub package source
```powershell
dotnet nuget add source --username USERNAME --password $gh_pat --store-password-in-clear-text --name github https://nuget.pkg.github.com/$owner/index.json
```

 ## Push Package to GitHub
```powershell
dotnet nuget push ..\..\packages\$owner\Identity.Service.$version.nupkg --api-key $gh_pat --source "github"
```

 ## Push Contracts Package to GitHub
 ```powershell
dotnet nuget push ..\..\packages\$owner\Identity.Contracts.$contracts_version.nupkg --api-key $gh_pat --source "github"
```

## Build the docker image
```powershell
$env:GH_OWNER="SampleMicroserviceShop"
$env:GH_PAT="[PAT HERE]"
docker build --secret id=GH_OWNER --secret id=GH_PAT -t identity.service:$version .
```

## Run the docker image
```powershell
docker run -it --rm -p 5002:5002 --name identity -e MongoDbSettings__Host=mongo -e RabbitMQSettings__Host=rabbitmq -e IdentitySettings__AdminUserPassword=$adminPass --network infra_default identity.service:$version
```
# Run the docker image - using CosmosDB ConnectionString:
```powershell
docker run -it --rm -p 5002:5002 --name identity -e MongoDbSettings__ConnectionString=$cosmosDbConnString -e RabbitMQSettings__Host=rabbitmq -e IdentitySettings__AdminUserPassword=$adminPass --network infra_default identity.service:$version
```
## Run the docker image - using ServiceBus ConnectionString
```powershell
docker run -it --rm -p 5002:5002 --name identity -e MongoDbSettings__ConnectionString=$cosmosDbConnString -e \
ServiceBusSettings__ConnectionString=$serviceBusConnString -e ServiceSettings__MessageBroker="SERVICEBUS" -e \
IdentitySettings__AdminUserPassword=$adminPass --network infra_default identity.service:$version
```
