using Aiyara.Timesheet.Api;
using Xunit;

namespace Aiyara.Phase3.Tests;

public sealed class TimesheetActorRoleTests
{
    [Theory]
    [InlineData("TenantAdmin", true)]
    [InlineData("Employee", false)]
    [InlineData("PlatformAdmin", false)]
    [InlineData("", false)]
    public void Only_tenant_admins_manage_tenant_settings(string role, bool expected) =>
        Assert.Equal(expected, new Actor(Guid.NewGuid(), Guid.NewGuid(), role, "Asia/Bangkok").IsAdmin);
}
