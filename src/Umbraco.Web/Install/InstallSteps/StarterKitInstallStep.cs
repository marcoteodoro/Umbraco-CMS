﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Umbraco.Core.Services;
using Umbraco.Web.Composing;
using Umbraco.Web.Install.Models;

namespace Umbraco.Web.Install.InstallSteps
{
    [InstallSetupStep(InstallationType.NewInstall,
        "StarterKitInstall", 31, "",
        PerformsAppRestart = true)]
    internal class StarterKitInstallStep : InstallSetupStep<object>
    {
        private readonly IHttpContextAccessor _httContextAccessor;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IPackagingService _packagingService;

        public StarterKitInstallStep(IHttpContextAccessor httContextAccessor, IUmbracoContextAccessor umbracoContextAccessor, IPackagingService packagingService)
        {
            _httContextAccessor = httContextAccessor;
            _umbracoContextAccessor = umbracoContextAccessor;
            _packagingService = packagingService;
        }


        public override Task<InstallSetupResult> ExecuteAsync(object model)
        {
            var installSteps = InstallStatusTracker.GetStatus().ToArray();
            var previousStep = installSteps.Single(x => x.Name == "StarterKitDownload");
            var packageId = Convert.ToInt32(previousStep.AdditionalData["packageId"]);

            InstallBusinessLogic(packageId);

            UmbracoApplication.Restart(_httContextAccessor.HttpContext);

            return Task.FromResult<InstallSetupResult>(null);
        }

        private void InstallBusinessLogic(int packageId)
        {
            var definition = _packagingService.GetInstalledPackageById(packageId);
            if (definition == null) throw new InvalidOperationException("Not package definition found with id " + packageId);

            var packageFile = new FileInfo(definition.PackagePath);

            _packagingService.InstallCompiledPackageData(definition, packageFile, _umbracoContextAccessor.UmbracoContext.Security.GetUserId().ResultOr(-1));
        }

        public override bool RequiresExecution(object model)
        {
            var installSteps = InstallStatusTracker.GetStatus().ToArray();
            //this step relies on the previous one completed - because it has stored some information we need
            if (installSteps.Any(x => x.Name == "StarterKitDownload" && x.AdditionalData.ContainsKey("packageId")) == false)
            {
                return false;
            }

            return true;
        }
    }
}
