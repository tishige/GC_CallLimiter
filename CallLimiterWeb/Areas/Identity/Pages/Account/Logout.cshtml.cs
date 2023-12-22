using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace BlazorApp2.Areas.Identity.Pages.Account
{
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    public class LogoutModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public LogoutModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IActionResult> OnPost()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();

            var environment = _configuration["GenesysCloud:Environment"];
            var clientId = _configuration["GenesysCloud:ClientId"];
            var redirectUri = _configuration["GenesysCloud:RedirectUri"];

            return Redirect($"https://login.{environment}/logout?client_id={clientId}&redirect_uri={redirectUri}");
        }
    }
}
