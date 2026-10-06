using System.Text.Json;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Xunit;

namespace Aiyara.Phase1.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Identity_grpc_contract_exposes_validation_and_profile_lookup()
    {
        var methods = IdentityValidationService.Descriptor.Methods.Select(x => x.Name).ToArray();
        Assert.Contains("ValidateAccessToken", methods);
        Assert.Contains("LookupProfile", methods);
        Assert.Equal("aiyara.timesheet.identity.v1",
            ValidateAccessTokenRequest.Descriptor.File.Package);
        Assert.Equal(1, ValidateAccessTokenRequest.Descriptor.FindFieldByName("access_token").FieldNumber);
        Assert.Equal(2, ValidateAccessTokenRequest.Descriptor.FindFieldByName("correlation_id").FieldNumber);
        Assert.Equal(3, ValidateAccessTokenResponse.Descriptor.FindFieldByName("tenant_id").FieldNumber);
        Assert.Equal(9, ValidateAccessTokenResponse.Descriptor.FindFieldByName("must_change_password").FieldNumber);
        Assert.Equal(10, ValidateAccessTokenResponse.Descriptor.FindFieldByName("policy_version").FieldNumber);
    }

    [Fact]
    public void Message_envelope_preserves_tenant_correlation_and_idempotency_metadata()
    {
        var tenantId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var envelope = new MessageEnvelope<TenantCreatedV1>(Guid.NewGuid(), correlationId,
            "tenant-created:test", tenantId, DateTime.UtcNow, MessageTypes.TenantCreated,
            new TenantCreatedV1(tenantId, "test", "Asia/Bangkok"));

        var json = JsonSerializer.Serialize(envelope);
        Assert.Contains("\"CorrelationId\"", json);
        Assert.Contains("\"IdempotencyKey\"", json);
        Assert.Contains("\"TenantId\"", json);
        var copy = JsonSerializer.Deserialize<MessageEnvelope<TenantCreatedV1>>(json);
        Assert.NotNull(copy);
        Assert.Equal(tenantId, copy.TenantId);
        Assert.Equal(correlationId, copy.CorrelationId);
        Assert.Equal("tenant-created:test", copy.IdempotencyKey);
        Assert.Equal(tenantId, copy.Payload.TenantId);
    }
}
