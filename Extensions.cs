using Azure.Identity;
using Identity.Service.Dtos;
using Identity.Service.Entities;

namespace Identity.Service;

public static class Extensions
{
    public static UserDto AsDto(this ApplicationUser user)
    {
        return new UserDto(user.Id, user.UserName, user.Email, user.Gil, user.CreatedOn);
    }

    public static void AddAzureKeyVaultIfProduction(this ConfigurationManager configuration, IWebHostEnvironment env)
    {
        if (env.IsProduction())
        {
            configuration.AddAzureKeyVault(
                new Uri("https://microshop.vault.azure.net/"),
                new DefaultAzureCredential());
        }
    }
}
