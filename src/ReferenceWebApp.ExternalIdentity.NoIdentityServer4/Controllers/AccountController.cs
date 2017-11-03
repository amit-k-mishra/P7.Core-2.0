﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P7.Core.Startup;
using P7.GraphQLCore;
using ReferenceWebApp.Models;
using ReferenceWebApp.Services;

namespace ReferenceWebApp.Controllers
{
    public class ClaimHandle
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
    public class AccountConfig
    {
        public const string WellKnown_SectionName = "account";
        public List<ClaimHandle> PostLoginClaims { get; set; }
    }
    public class MyAccountConfigureServicesRegistrant : ConfigureServicesRegistrant
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.Configure<AccountConfig>(Configuration.GetSection(AccountConfig.WellKnown_SectionName));

        }

        public MyAccountConfigureServicesRegistrant(IConfiguration configuration) : base(configuration)
        {
        }
    }
    [Authorize]
    [Route("[controller]/[action]")]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger _logger;
        private IOptions<AccountConfig> _settings;
        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            IOptions<AccountConfig> settings,
            ILogger<AccountController> logger)
        {
            _settings = settings;
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [TempData]
        public string ErrorMessage { get; set; }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Login(string returnUrl = null)
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ViewData["ReturnUrl"] = returnUrl;
            return View("Login.bulma");
        }

 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginWhatIf(string provider, string returnUrl = null)
        {
            var result = InternalExternalLogin(provider, returnUrl);
            return (result);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExternalLogin(string provider, string returnUrl = null)
        {
            var result = InternalExternalLogin(provider, returnUrl);
            return (result);
        }

        private IActionResult InternalExternalLogin(string provider, string returnUrl = null)
        {
            // Request a redirect to the external login provider.
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            var challeng = Challenge(properties, provider);
            return challeng;
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginCallback(string returnUrl = null, string remoteError = null)
        {
            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToAction(nameof(Login));
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var query = from claim in info.Principal.Claims
                where claim.Type == "DisplayName"
                select claim;
            var queryNameId = from claim in info.Principal.Claims
                where claim.Type == ClaimTypes.NameIdentifier
                select claim;
            var nameClaim = query.FirstOrDefault();
            var displayName = nameClaim.Value;
            var nameIdClaim = queryNameId.FirstOrDefault();

            

            // paranoid
            var leftoverUser = await _userManager.FindByEmailAsync(displayName);
            if (leftoverUser != null)
            {
                await _userManager.DeleteAsync(leftoverUser); // just using this inMemory userstore as a scratch holding pad
            }
            // paranoid end

            var user = new ApplicationUser { UserName = nameIdClaim.Value, Email = displayName };
            var result = await _userManager.CreateAsync(user);
            var newUser = await _userManager.FindByIdAsync(user.Id);

            var cQuery = from claim in _settings.Value.PostLoginClaims
                        let c = new Claim(claim.Name, claim.Value)
                        select c;
            var eClaims = cQuery.ToList();
            eClaims.Add(new Claim("custom-name", displayName));
            await _userManager.AddClaimsAsync(newUser, eClaims);

            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                await _userManager.DeleteAsync(user); // just using this inMemory userstore as a scratch holding pad
                _logger.LogInformation("User created an account using {Name} provider.", info.LoginProvider);
                return RedirectToLocal(returnUrl);

            }
            return RedirectToAction(nameof(Login));
        }

 

        #region Helpers

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
        }

        #endregion
    }
}