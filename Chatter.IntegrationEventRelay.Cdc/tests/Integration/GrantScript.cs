using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// <c>.scripts/cdc/grant-relay-permissions.sql</c>, with its parameters set.
    /// </summary>
    public static class GrantScript
    {
        private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "Scripts", "grant-relay-permissions.sql");

        public static string Render(string user, string captureInstance, string schema, CdcErrorPolicy errorPolicy, bool whatIf = false)
        {
            var script = File.ReadAllText(Path);
            script = Set(script, "user", Literal(user));
            script = Set(script, "captureInstance", Literal(captureInstance));
            script = Set(script, "schema", Literal(schema));
            script = Set(script, "errorPolicy", Literal(errorPolicy.ToString()));
            return Set(script, "whatIf", whatIf ? "1" : "0");
        }

        private static string Set(string script, string parameter, string value)
        {
            var declaration = new Regex($@"^(DECLARE @{parameter} \S+ = )[^;]+;", RegexOptions.Multiline);
            if (declaration.Matches(script).Count != 1)
                throw new InvalidOperationException($"{Path} must declare @{parameter} once, at the start of a line.");
            return declaration.Replace(script, m => m.Groups[1].Value + value + ";");
        }

        private static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";
    }
}
