using Aiyara.Report.Api;
using Xunit;

namespace Aiyara.Phase4.Tests;

public sealed class ReportActorRoleTests
{
    [Theory]
    [InlineData("TenantAdmin", true)]
    [InlineData("Employee", false)]
    [InlineData("PlatformAdmin", false)]
    [InlineData("", false)]
    public void Only_tenant_admins_see_every_report_in_the_tenant(string role, bool expected) =>
        Assert.Equal(expected, new ReportActor(Guid.NewGuid(), Guid.NewGuid(), role, "Asia/Bangkok").IsAdmin);
}
