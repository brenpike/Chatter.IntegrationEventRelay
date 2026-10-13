namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// <c>.scripts/cdc/switch-to-cdc.sql</c> and <c>.scripts/cdc/switch-to-service-broker.sql</c>, with their parameters set.
    /// </summary>
    public static class SwitchScripts
    {
        public static string RenderToCdc(string tableSchema, string tableName, string captureInstance, string sourceName, string relaySchema = "Chatter", bool keepDeadLetters = false, int timeoutSeconds = 300)
            => new SqlScriptFile("switch-to-cdc.sql")
                .Set("tableSchema", SqlScriptFile.Literal(tableSchema))
                .Set("tableName", SqlScriptFile.Literal(tableName))
                .Set("captureInstance", SqlScriptFile.Literal(captureInstance))
                .Set("sourceName", SqlScriptFile.Literal(sourceName))
                .Set("relaySchema", SqlScriptFile.Literal(relaySchema))
                .Set("keepDeadLetters", SqlScriptFile.Bit(keepDeadLetters))
                .Set("timeoutSeconds", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Text;

        public static string RenderToServiceBroker(string tableSchema, string tableName, string captureInstance, string relaySchema = "Chatter", int timeoutSeconds = 300)
            => new SqlScriptFile("switch-to-service-broker.sql")
                .Set("tableSchema", SqlScriptFile.Literal(tableSchema))
                .Set("tableName", SqlScriptFile.Literal(tableName))
                .Set("captureInstance", SqlScriptFile.Literal(captureInstance))
                .Set("relaySchema", SqlScriptFile.Literal(relaySchema))
                .Set("timeoutSeconds", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Text;
    }
}
