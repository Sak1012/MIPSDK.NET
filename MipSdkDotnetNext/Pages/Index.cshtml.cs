using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;
using MipSdkDotnetNext.Models;
using MipSdkDotnetNext.Services;
using OfficeOpenXml;
using System.Diagnostics;
using System.Text.Json;

namespace MipSdkDotnetNext.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly FileApi _fileApi;
        private readonly ITokenAcquisition _tokenAcquisition;

        public List<Label> Labels { get; set; } = new();
        public List<CustomClass> Data { get; set; } = new();

        [BindProperty]
        public string SelectedLabelId { get; set; }

        [BindProperty]
        public string CustomEmails { get; set; }

        [BindProperty]
        public string SelectedRights { get; set; }

        [BindProperty]
        public DateTime? ExpiryDate { get; set; }

        public IndexModel(IConfiguration configuration, FileApi fileApi, ITokenAcquisition tokenAcquisition)
        {
            _configuration = configuration;
            _fileApi = fileApi;
            _tokenAcquisition = tokenAcquisition;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var scopes = new[] { "https://syncservice.o365syncservice.com/.default" };
                await _tokenAcquisition.GetAccessTokenForUserAsync(scopes);
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                return Challenge(); // triggers interactive prompt
            }
            // Delay FileApi initialization until user is authenticated and this method runs
            await _fileApi.InitializeAsync();

            await LoadLabelsAndDataAsync();
            return Page();
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostDownload()
        {
            Debug.WriteLine("Inside DownloadAsync");

            try
            {
                if (!User.Identity?.IsAuthenticated ?? true)
                    return Challenge();

                var scopes = new[] { "https://syncservice.o365syncservice.com/.default" };
                await _tokenAcquisition.GetAccessTokenForUserAsync(scopes); // Triggers consent if needed

                await _fileApi.InitializeAsync();
                await LoadLabelsAndDataAsync(); // Preserve table + dropdowns

                if (string.IsNullOrEmpty(SelectedLabelId))
                {
                    ModelState.AddModelError(string.Empty, "Please select a label.");
                    return Page();
                }

                var fileName = "MyAppOutput.xlsx";
                var templateFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Template.xlsx");

                // Build Excel from data
                var excelStream = new MemoryStream();
                using (var excel = new ExcelPackage(new FileInfo(templateFile)))
                {
                    var worksheet = excel.Workbook.Worksheets.Add("MyData");
                    worksheet.Cells["A1"].LoadFromCollection(Data, true);
                    excel.SaveAs(excelStream);
                }

                excelStream.Position = 0;
                var outputStream = new MemoryStream();

                var protectionOptions = new ProtectionOptions
                {
                    Emails = CustomEmails?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() ?? new(),
                    Rights = SelectedRights ?? "View",
                };

                var result = false;
                try
                {
                    result = _fileApi.ApplyLabel(excelStream, outputStream, fileName, SelectedLabelId, "", protectionOptions);
                }
                catch (Microsoft.InformationProtection.Exceptions.AdhocProtectionRequiredException)
                {
                    // Set a flag to show additional fields on the frontend
                    ViewData["NeedsAdditionalInfo"] = true;
                    ModelState.AddModelError(string.Empty, "Additional Info Required for the selected Label.");
                    await LoadLabelsAndDataAsync(); // Reload view model state
                    return Page();
                }

                outputStream.Position = 0;

                //  Return as a downloadable file response
                return File(
                    fileContents: outputStream.ToArray(),
                    contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileDownloadName: fileName
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download error: {ex}");
                ModelState.AddModelError(string.Empty, "An error occurred during download.");
                await LoadLabelsAndDataAsync();
                return Page();
            }
        }

        private async Task LoadLabelsAndDataAsync()
        {
            Labels = _fileApi.ListAllLabels();

            var dataEndpoint = _configuration["Application:DataEndpoint"];
            using var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync(dataEndpoint);

            Data = JsonSerializer.Deserialize<List<CustomClass>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<CustomClass>();
        }
    }
}
