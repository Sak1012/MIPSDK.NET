using Microsoft.AspNetCore.Mvc;

namespace MipSdkDotnetNext.Models
{
    public class ProtectionOptions
    {
        public List<string> Emails { get; set; } = new();
        public string Rights { get; set; } = "View";
    }
}
