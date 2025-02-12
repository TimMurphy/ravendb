﻿using System;
using System.Threading;
using Raven.Server.Documents.PeriodicBackup.Aws;
using SlowTests.Server.Documents.PeriodicBackup.Restore;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.PeriodicBackup;

public class CustomS3 : RestoreFromS3
{
    public CustomS3(ITestOutputHelper output) : base(output, isCustom: true)
    {
    }

    [CustomS3RetryFact]
    public void can_use_custom_region()
    {
        const string customUrl = "https://s3.pl-waw.scw.cloud";
        const string customRegion = "pl-waw";

        var settings = GetS3Settings();
        settings.CustomServerUrl = customUrl;
        settings.AwsRegionName = customRegion;

        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
        using (var client = new RavenAwsS3Client(settings, DefaultConfiguration, cancellationToken: cts.Token))
        {
            Assert.StartsWith(customUrl, client.Config.ServiceURL);
            Assert.Equal(customRegion, client.Config.AuthenticationRegion);
        }
    }
}
