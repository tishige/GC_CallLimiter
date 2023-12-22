using CallLimiterWeb.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace BlazorApp2.Areas.MyLogin.Pages;

[IgnoreAntiforgeryToken]
public class IndexModel : PageModel
{

    private readonly IDNISService _dnisService;
    private readonly ILogger<DNISService> _logger;


	public IndexModel(IDNISService dnisService, ILogger<DNISService> logger)
    {
        _dnisService = dnisService;
        _logger = logger;
  
    }

	public async Task<IActionResult> OnPost([FromForm] string code)
    {
		// The access token is in the AccessToken property.  
		string? token = await _dnisService.FetchTokenFromCode(code);
		var userOrgObject = await _dnisService.FetchUserOrgInfo(token);
        string? orgCheckResult = (string)userOrgObject["status"]!;

        if (orgCheckResult == "orgError")
        {
            _logger.LogError($"CLM:Organization error");
            return Unauthorized();

        }

        var userObject = await _dnisService.FetchUserInfo(token);
        var userDivisions = await _dnisService.FetchUserDivision(token);

        string? userName = (string)userObject["name"]!;
        string? email = (string)userObject["email"]!;
        string? orgName = (string)userOrgObject["name"]!;

        string? divNamePiped = string.Join("|", userDivisions.Select(u => u.DivisionName));
        string? divIdsPiped = string.Join("|", userDivisions.Select(u => u.DivisionId));

        if (userDivisions.Count == 0)
        {
            _logger.LogError($"CLM:user:{email} unable to get divisionsID.");
            return Unauthorized();
        }

        if (token is null) return Page();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, userName),
            new Claim("OrgName", orgName),
            new Claim(ClaimTypes.Role, "User"),
        }, CookieAuthenticationDefaults.AuthenticationScheme));

		var authProperties = new AuthenticationProperties
		{
			IsPersistent = false
		};

		await HttpContext.SignInAsync(
	    CookieAuthenticationDefaults.AuthenticationScheme, principal,authProperties);

		HttpContext.Session.SetString("userName", userName);
        HttpContext.Session.SetString("email", email);
        HttpContext.Session.SetString("orgName", orgName);

        HttpContext.Session.SetString("GCCode", code);
		HttpContext.Session.SetString("GCToken", token);

		HttpContext.Session.SetString("userDivisionsName", divNamePiped);
        HttpContext.Session.SetString("userDivisionsId", divIdsPiped);

        _logger.LogInformation($"CLM:user:{email} logged in. division ${divNamePiped}");

        return Redirect("/");

    }

}

