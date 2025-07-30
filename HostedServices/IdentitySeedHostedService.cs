using Identity.Service.Entities;
using Identity.Service.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Service.HostedServices;

public class IdentitySeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IdentitySettings settings;
    public IdentitySeedHostedService(IServiceScopeFactory serviceScopeFactory, IOptions<IdentitySettings>
    identityOptions)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        settings = identityOptions.Value;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Create a new scope to retrieve scoped services
        using var scope = serviceScopeFactory.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await CreateRoleIfNotExistsAsync(Roles.Admin, roleManager);
        await CreateRoleIfNotExistsAsync(Roles.User, roleManager);
        var adminUser = await userManager.FindByEmailAsync(settings.AdminUserEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = settings.AdminUserEmail,
                Email = settings.AdminUserEmail,
            };
            IdentityResult userResult = await userManager.CreateAsync(adminUser, settings.AdminUserPassword);
            if (!userResult.Succeeded)
            {
                throw new Exception(string.Join(Environment.NewLine, userResult.Errors.Select(e => e.Description)));
            }
            var roleToUserResult = await userManager.AddToRoleAsync(adminUser, Roles.Admin);
            if (!roleToUserResult.Succeeded)
            {
                throw new Exception(string.Join(Environment.NewLine, roleToUserResult.Errors.Select(e =>
                e.Description)));
            }
        }
    }
    private static async Task CreateRoleIfNotExistsAsync(string role, RoleManager<ApplicationRole> roleManager)
    {
        var roleExists = await roleManager.RoleExistsAsync(role);
        if (!roleExists)
        {
            var roleResult = await roleManager.CreateAsync(new ApplicationRole { Name = role });
            if (!roleResult.Succeeded)
            {
                throw new Exception(string.Join(Environment.NewLine, roleResult.Errors.Select(e => e.Description)));
            }
        }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
