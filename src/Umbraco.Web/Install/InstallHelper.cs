﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Core.Logging;
using Umbraco.Core.Migrations.Install;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.SqlSyntax;
using Umbraco.Core.Services;
using Umbraco.Web.Composing;
using Umbraco.Web.Install.Models;

namespace Umbraco.Web.Install
{
    public sealed class InstallHelper
    {
        private static HttpClient _httpClient;
        private readonly DatabaseBuilder _databaseBuilder;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger _logger;
        private readonly IGlobalSettings _globalSettings;
        private readonly IUmbracoVersion _umbracoVersion;
        private readonly IConnectionStrings _connectionStrings;
        private readonly IInstallationService _installationService;
        private InstallationType? _installationType;

        public InstallHelper(IHttpContextAccessor httpContextAccessor,
            DatabaseBuilder databaseBuilder,
            ILogger logger,
            IGlobalSettings globalSettings,
            IUmbracoVersion umbracoVersion,
            IConnectionStrings connectionStrings,
            IInstallationService installationService)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _globalSettings = globalSettings;
            _umbracoVersion = umbracoVersion;
            _databaseBuilder = databaseBuilder;
            _connectionStrings = connectionStrings ?? throw new ArgumentNullException(nameof(connectionStrings));
            _installationService = installationService;
        }

        public InstallationType GetInstallationType()
        {
            return _installationType ?? (_installationType = IsBrandNewInstall ? InstallationType.NewInstall : InstallationType.Upgrade).Value;
        }

        internal async Task InstallStatus(bool isCompleted, string errorMsg)
        {
            try
            {
                var httpContext = _httpContextAccessor.GetRequiredHttpContext();
                var userAgent = httpContext.Request.UserAgent;

                // Check for current install Id
                var installId = Guid.NewGuid();

                var installCookie = httpContext.Request.GetCookieValue(Constants.Web.InstallerCookieName);
                if (string.IsNullOrEmpty(installCookie) == false)
                {
                    if (Guid.TryParse(installCookie, out installId))
                    {
                        // check that it's a valid Guid
                        if (installId == Guid.Empty)
                            installId = Guid.NewGuid();
                    }
                    else
                    {
                        installId = Guid.NewGuid(); // Guid.TryParse will have reset installId to Guid.Empty
                    }
                }
                httpContext.Response.Cookies.Set(new HttpCookie(Constants.Web.InstallerCookieName, installId.ToString()));

                var dbProvider = string.Empty;
                if (IsBrandNewInstall == false)
                {
                    // we don't have DatabaseProvider anymore... doing it differently
                    //dbProvider = ApplicationContext.Current.DatabaseContext.DatabaseProvider.ToString();
                    dbProvider = GetDbProviderString(Current.SqlContext);
                }

                var installLog = new InstallLog(installId: installId, isUpgrade: IsBrandNewInstall == false,
                    installCompleted: isCompleted, timestamp: DateTime.Now, versionMajor: _umbracoVersion.Current.Major,
                    versionMinor: _umbracoVersion.Current.Minor, versionPatch: _umbracoVersion.Current.Build,
                    versionComment: _umbracoVersion.Comment, error: errorMsg, userAgent: userAgent,
                    dbProvider: dbProvider);

                await _installationService.LogInstall(installLog);
            }
            catch (Exception ex)
            {
                _logger.Error<InstallHelper>(ex, "An error occurred in InstallStatus trying to check upgrades");
            }
        }

        internal static string GetDbProviderString(ISqlContext sqlContext)
        {
            var dbProvider = string.Empty;

            // we don't have DatabaseProvider anymore...
            //dbProvider = ApplicationContext.Current.DatabaseContext.DatabaseProvider.ToString();
            //
            // doing it differently
            var syntax = sqlContext.SqlSyntax;
            if (syntax is SqlCeSyntaxProvider)
                dbProvider = "SqlServerCE";
            else if (syntax is SqlServerSyntaxProvider)
                dbProvider = (syntax as SqlServerSyntaxProvider).ServerVersion.IsAzure ? "SqlAzure" : "SqlServer";

            return dbProvider;
        }

        /// <summary>
        /// Checks if this is a brand new install meaning that there is no configured version and there is no configured database connection
        /// </summary>
        private bool IsBrandNewInstall
        {
            get
            {
                var databaseSettings = _connectionStrings[Constants.System.UmbracoConnectionName];
                if (_globalSettings.ConfigurationStatus.IsNullOrWhiteSpace()
                    && databaseSettings.IsConnectionStringConfigured() == false)
                {
                    //no version or conn string configured, must be a brand new install
                    return true;
                }

                //now we have to check if this is really a new install, the db might be configured and might contain data

                if (databaseSettings.IsConnectionStringConfigured() == false
                    || _databaseBuilder.IsDatabaseConfigured == false)
                {
                    return true;
                }

                return _databaseBuilder.HasSomeNonDefaultUser() == false;
            }
        }

        internal IEnumerable<Package> GetStarterKits()
        {
            if (_httpClient == null)
                _httpClient = new HttpClient();

            var packages = new List<Package>();
            try
            {
                var requestUri = $"https://our.umbraco.com/webapi/StarterKit/Get/?umbracoVersion={_umbracoVersion.Current}";

                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    var response = _httpClient.SendAsync(request).Result;
                    packages = response.Content.ReadAsAsync<IEnumerable<Package>>().Result.ToList();
                }
            }
            catch (AggregateException ex)
            {
                _logger.Error<InstallHelper>(ex, "Could not download list of available starter kits");
            }

            return packages;
        }
    }
}
