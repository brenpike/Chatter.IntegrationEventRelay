namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// <c>.scripts/cdc/grant-relay-permissions.sql</c>, with its parameters set.
    /// </summary>
    public static class GrantScript
    {
        public static string Render(string user, string captureInstance, string schema, CdcErrorPolicy errorPolicy, string gatingRole = null, bool whatIf = false)
            => new SqlScriptFile("grant-relay-permissions.sql")
                .Set("user", SqlScriptFile.Literal(user))
                .Set("captureInstance", SqlScriptFile.Literal(captureInstance))
                .Set("schema", SqlScriptFile.Literal(schema))
                .Set("errorPolicy", SqlScriptFile.Literal(errorPolicy.ToString()))
                .Set("gatingRole", gatingRole is null ? "NULL" : SqlScriptFile.Literal(gatingRole))
                .Set("whatIf", SqlScriptFile.Bit(whatIf))
                .Text;
    }
}
