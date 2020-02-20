﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using Microsoft.Owin;
using Moq;
using NUnit.Framework;
using Umbraco.Core;
using Umbraco.Tests.TestHelpers;
using Umbraco.Web.Composing;
using Umbraco.Tests.Testing;
using Umbraco.Tests.Testing.Objects.Accessors;
using Umbraco.Web;
using Umbraco.Web.PublishedCache;
using Umbraco.Web.Routing;
using Umbraco.Web.Security;


namespace Umbraco.Tests.Security
{
    [TestFixture]
    [UmbracoTest(WithApplication = true)]
    public class BackOfficeCookieManagerTests : UmbracoTestBase
    {
        [Test]
        public void ShouldAuthenticateRequest_When_Not_Configured()
        {
            //should force app ctx to show not-configured
            ConfigurationManager.AppSettings.Set(Constants.AppSettings.ConfigurationStatus, "");

            var httpContextAccessor = TestHelper.GetHttpContextAccessor();
            var globalSettings = TestObjects.GetGlobalSettings();
            var umbracoContext = new UmbracoContext(
                httpContextAccessor,
                Mock.Of<IPublishedSnapshotService>(),
                new WebSecurity(httpContextAccessor, ServiceContext.UserService, globalSettings, IOHelper), globalSettings,
                new TestVariationContextAccessor(),
                IOHelper,
                UriUtility);

            var runtime = Mock.Of<IRuntimeState>(x => x.Level == RuntimeLevel.Install);
            var mgr = new BackOfficeCookieManager(
                Mock.Of<IUmbracoContextAccessor>(accessor => accessor.UmbracoContext == umbracoContext), runtime, TestObjects.GetGlobalSettings(), IOHelper, AppCaches.RequestCache);

            var result = mgr.ShouldAuthenticateRequest(Mock.Of<IOwinContext>(), new Uri("http://localhost/umbraco"));

            Assert.IsFalse(result);
        }

        [Test]
        public void ShouldAuthenticateRequest_When_Configured()
        {
            var httpContextAccessor = TestHelper.GetHttpContextAccessor();
            var globalSettings = TestObjects.GetGlobalSettings();
            var umbCtx = new UmbracoContext(
                httpContextAccessor,
                Mock.Of<IPublishedSnapshotService>(),
                new WebSecurity(httpContextAccessor, ServiceContext.UserService, globalSettings, IOHelper),
                globalSettings,
                new TestVariationContextAccessor(),
                IOHelper,
                UriUtility);

            var runtime = Mock.Of<IRuntimeState>(x => x.Level == RuntimeLevel.Run);
            var mgr = new BackOfficeCookieManager(Mock.Of<IUmbracoContextAccessor>(accessor => accessor.UmbracoContext == umbCtx), runtime,  TestObjects.GetGlobalSettings(), IOHelper, AppCaches.RequestCache);

            var request = new Mock<OwinRequest>();
            request.Setup(owinRequest => owinRequest.Uri).Returns(new Uri("http://localhost/umbraco"));

            var result = mgr.ShouldAuthenticateRequest(
                Mock.Of<IOwinContext>(context => context.Request == request.Object),
                new Uri("http://localhost/umbraco"));

            Assert.IsTrue(result);
        }

        // TODO: Write remaining tests for `ShouldAuthenticateRequest`
    }
}
