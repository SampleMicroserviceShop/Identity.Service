## Identity.Microservice
Sample Microservice Shop Identity microservice.


## Create and publish package
```powershell
$version="1.0.3"
$owner="SampleMicroserviceShop"
dotnet pack --configuration Release -p:PackageVersion=$version -o ..\..\packages\$owner
```

 ## Add the GitHub package source
```powershell
$owner="SampleMicroserviceShop"
$gh_pat="[PAT HERE]"
dotnet nuget add source --username USERNAME --password $gh_pat --store-password-in-clear-text --name github https://nuget.pkg.github.com/$owner/index.json
```

 ## Push Package to GitHub
```powershell
$version="1.0.3"
$gh_pat="[PAT HERE]"
$owner="SampleMicroserviceShop"
dotnet nuget push ..\..\packages\$owner\Identity.Service.$version.nupkg --api-key $gh_pat --source "github"
or
dotnet nuget push ..\..\packages\$owner\Identity.Contracts.$version.nupkg --api-key $gh_pat --source "github"
```

## Build the docker image
```powershell
$env:GH_OWNER="SampleMicroserviceShop"
$env:GH_PAT="[PAT HERE]"
docker build --secret id=GH_OWNER --secret id=GH_PAT -t identity.service:$version .
```

## Run the docker image
```powershell
$adminPass="[PASSWORD HERE]"
docker run -it --rm -p 5002:5002 --name identity -e MongoDbSettings__Host=mongo -e RabbitMQSettings__Host=rabbitmq -e IdentitySettings__AdminUserPassword=$adminPass --network infra_default identity.service:$version
```
# > or for using connection string:
```powershell
$adminPass="[PASSWORD HERE]"
$cosmosDbConnString="[Connection String]"
docker run -it --rm -p 5002:5002 --name identity -e MongoDbSettings__ConnectionString=$cosmosDbConnString -e RabbitMQSettings__Host=rabbitmq -e IdentitySettings__AdminUserPassword=$adminPass --network infra_default play.identity:$version
```
