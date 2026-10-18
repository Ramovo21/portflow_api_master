namespace PortFlow.Tests.Support.Security;

/// <summary>Base cho test dùng Keycloak thật: <see cref="IntegrationTest.Http"/> mang token thật của <c>admin</c>.</summary>
[Collection(KeycloakCollection.Name)]
[Trait("Category", "Keycloak")]
public abstract class KeycloakIntegrationTest(KeycloakFixture fixture) : IntegrationTest(fixture)
{
    protected KeycloakTestClient Kc { get; } = fixture.Kc;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Http = ClientWith("Bearer " + await Kc.AccessTokenAsync("admin"));
    }
}
