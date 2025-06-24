using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Web;
using Microsoft.InformationProtection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MipSdkDotnetNext.Services
{
    public class AuthDelegateImplementation : IAuthDelegate
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;

        // Optional: cached user to avoid lost context during async SDK operations
        public ClaimsPrincipal? CachedUser { get; set; }

        public AuthDelegateImplementation(
            ITokenAcquisition tokenAcquisition,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration)
        {
            _tokenAcquisition = tokenAcquisition ?? throw new ArgumentNullException(nameof(tokenAcquisition));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public string AcquireToken(Identity identity, string authority, string resource, string claim)
        {

            try
            {
                return GetAccessTokenAsync(resource).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthDelegate] Exception acquiring token: {ex}");
                throw;
            }
        }

        private async Task<string> GetAccessTokenAsync(string resource)
        {
            var user = CachedUser ?? _httpContextAccessor.HttpContext?.User;

            if (user != null)
            {
                Debug.WriteLine("==== User Claims ====");
                foreach (var claim in user.Claims)
                {
                    Debug.WriteLine($"{claim.Type} = {claim.Value}");
                }
                Debug.WriteLine("=====================");
            }
            else
            {
                Debug.WriteLine("[AuthDelegate] No user context available for token acquisition.");
                user = CachedUser;
            }

            var scopes = new[] { $"{resource.TrimEnd('/')}/.default" };
            var tenantId = _configuration["AzureAd:TenantId"];

            try
            {
                var token = await _tokenAcquisition.GetAccessTokenForUserAsync(scopes, tenantId, null, user);
                return token;
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                Debug.WriteLine("[AuthDelegate] User consent required.");
                throw; // Will be handled by the calling code (like IndexModel) via token pre-acquisition
            }
        }
    }
}
