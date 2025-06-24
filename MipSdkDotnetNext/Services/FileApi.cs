/*
 The MIT License (MIT)
 
Copyright (c) 2018 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.File;
using MipSdkDotnetNext.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using AppLabel = MipSdkDotnetNext.Models.Label;
using MipLabel = Microsoft.InformationProtection.Label;
using MipLogLevel = Microsoft.InformationProtection.LogLevel;

namespace MipSdkDotnetNext.Services
{
    public class FileApi
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthDelegate _authDelegate;
        private MipContext _mipContext;
        private IFileProfile _fileProfile;
        private IFileEngine _fileEngine;
        private ApplicationInfo _appInfo;
        private string _mipDataPath;
        public bool IsInitialized { get; private set; }

        public FileApi(IConfiguration configuration, IHttpContextAccessor httpContextAccessor, IAuthDelegate authDelegate)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _authDelegate = authDelegate;
        }

        public async Task InitializeAsync()
        {
            Debug.WriteLine("Initialization in process");
            _appInfo = new ApplicationInfo
            {
                ApplicationId = _configuration["AzureAd:ClientId"],
                ApplicationName = _configuration["Application:Name"],
                ApplicationVersion = _configuration["Application:Version"]
            };

            _mipDataPath = _configuration["Mip:MipDataPath"];
            if (!Directory.Exists(_mipDataPath))
            {
                Directory.CreateDirectory(_mipDataPath);
            }

            var mipSdkBinPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.Is64BitProcess ? "x64" : "x86");

            MIP.Initialize(MipComponent.File, mipSdkBinPath);

            _mipContext = MIP.CreateMipContext(new MipConfiguration(_appInfo, _mipDataPath, MipLogLevel.Trace, false));

            var profileSettings = new FileProfileSettings(_mipContext, CacheStorageType.OnDisk, new ConsentDelegateImplementation());
            _fileProfile = await MIP.LoadFileProfileAsync(profileSettings);

            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
            {
                Debug.WriteLine("HttpContext or User is null.");
                throw new InvalidOperationException("HttpContext or User is not available.");
            }

            var username = user.FindFirst("preferred_username")?.Value;
            if (string.IsNullOrEmpty(username))
            {
                throw new InvalidOperationException("Username (UPN) not found in user claims.");
            }

            if (_authDelegate is AuthDelegateImplementation typedDelegate)
            {
                typedDelegate.CachedUser = user;
            }

            var engineSettings = new FileEngineSettings(username, _authDelegate, "", "en-US")
            {
                Identity = new Identity(username)
            };

            _fileEngine = await _fileProfile.AddEngineAsync(engineSettings);
            IsInitialized = true;
            Debug.WriteLine("\nInitialization Finished\n");
        }

        private IFileHandler CreateFileHandler(Stream? stream, string fileName)
        {
            return Task.Run(async () =>
            {
                return stream != null
                    ? await _fileEngine.CreateFileHandlerAsync(stream, fileName, true)
                    : await _fileEngine.CreateFileHandlerAsync(fileName, fileName, true);
            }).Result;
        }

        public bool ApplyLabel(Stream? inputStream, Stream outputStream, string fileName, string labelId, string justificationMessage, ProtectionOptions protectionOptions)
        {
            Debug.WriteLine("This is Here somewhere");
            var handler = CreateFileHandler(inputStream, fileName);

            var labelingOptions = new LabelingOptions
            {
                JustificationMessage = justificationMessage,
                AssignmentMethod = AssignmentMethod.Standard,
                ExtendedProperties = new List<KeyValuePair<string, string>>()
            };
            Debug.WriteLine($"\nApplying Label: {_fileEngine.GetLabelById(labelId).Name}\n");
            bool protect = (bool)(protectionOptions.Emails?.Any());
            Debug.WriteLine($"Should I protect : {protect}");
            if (!protect) { 
                handler.SetLabel(_fileEngine.GetLabelById(labelId), labelingOptions, new ProtectionSettings());
            }
            else { 
                List<string> users = protectionOptions.Emails;
                var rights = protectionOptions.Rights.ToLower() switch
                {
                    "view" => new List<string> { "VIEW" },
                    "edit" => new List<string> { "VIEW", "EDIT" },
                    "all" => new List<string> { "VIEW", "EDIT", "COPY", "PRINT" },
                    _ => new List<string> { "VIEW" }
                };
                UserRights userRights = new UserRights(users, rights);
                List<UserRights> userRightsList = new List<UserRights>()
                {
                    userRights
                };
                ProtectionDescriptor protectionDescriptor = new ProtectionDescriptor(userRightsList);

                handler.SetProtection(protectionDescriptor, new ProtectionSettings());
                handler.SetLabel(_fileEngine.GetLabelById(labelId), labelingOptions, new ProtectionSettings());
            }

                var result = Task.Run(async () => await handler.CommitAsync(outputStream)).Result;

            if (result)
            {
                handler.NotifyCommitSuccessful(fileName);
            }
            Debug.WriteLine("This is Done Here somewhere");

            return result;
        }

        public List<AppLabel> ListAllLabels()
        {
            var labels = _fileEngine.SensitivityLabels;
            Debug.WriteLine("\nInside Listing Lables\n");
            foreach(MipLabel la in labels)
            {
                Debug.WriteLine($"{la.Name.ToString()} : {la.Sensitivity} : {la.Id}");
                foreach(MipLabel c in la.Children)
                {
                    Debug.WriteLine($"{c.Name.ToString()} : {c.Sensitivity} : {c.Id}");
                }
            }
            var returnLabels = new List<AppLabel>();

            foreach (MipLabel label in labels)
            {
                var appLabel = new AppLabel
                {
                    Name = label.Name,
                    Id = label.Id,
                    Description = label.Description,
                    Sensitivity = label.Sensitivity,
                    Children = new List<AppLabel>()
                };

                foreach (MipLabel child in label.Children)
                {
                    appLabel.Children.Add(new AppLabel
                    {
                        Name = child.Name,
                        Id = child.Id,
                        Description = child.Description,
                        Sensitivity = child.Sensitivity
                    });
                }

                returnLabels.Add(appLabel);
            }

            return returnLabels;
        }
    }
}
