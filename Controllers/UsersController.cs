using Duende.IdentityServer;
using Identity.Contracts;
using Identity.Service.Dtos;
using Identity.Service.Entities;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Service.Controllers;
[Route("[controller]")]
[ApiController]
[Authorize(Policy = IdentityServerConstants.LocalApi.PolicyName, Roles = Roles.Admin)]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> userManager;
    private readonly IPublishEndpoint publishEndpoint;
    public UsersController(UserManager<ApplicationUser> userManager, IPublishEndpoint publishEndpoint)
    {
        this.userManager = userManager;
        this.publishEndpoint = publishEndpoint;
    }
    [HttpGet]
    public ActionResult<IEnumerable<UserDto>> Get()
    {
        var users = userManager.Users
            .ToList()
            .Select(user => user.AsDto());
        return Ok(users);
    }
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetByIdAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }
        return user.AsDto();
    }
    [HttpPut("{id}")]
    public async Task<IActionResult> PutAsync(Guid id, UpdateUserDto userDto)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }
        user.UserName = userDto.Email;
        user.Email = userDto.Email;
        user.Gil = userDto.Gil;
        await userManager.UpdateAsync(user);
        await publishEndpoint.Publish(new UserUpdated(user.Id, user.Email, user.Gil));
        return NoContent();
    }
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }
        await userManager.DeleteAsync(user);
        await publishEndpoint.Publish(new UserUpdated(user.Id, user.Email, 0));
        return NoContent();
    }
}
