namespace GeometryDashPlace.Web.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class PostgreSqlIntegrationCollection :
    ICollectionFixture<PostgreSqlIntegrationFixture>
{
    public const string Name = "PostgreSQL integration";
}
