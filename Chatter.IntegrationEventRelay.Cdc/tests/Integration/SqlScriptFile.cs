using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// A script from <c>.scripts/cdc</c>, copied to the test output, whose parameters are <c>DECLARE @name type = value;</c>
    /// lines at the start of a line.
    /// </summary>
    public sealed class SqlScriptFile
    {
        private readonly string _path;
        private string _script;

        public SqlScriptFile(string name)
        {
            _path = Path.Combine(AppContext.BaseDirectory, "Scripts", name);
            _script = File.ReadAllText(_path);
        }

        public string Text => _script;

        /// <summary>
        /// Sets the value of parameter <c>@<paramref name="parameter"/></c> to the SQL expression <paramref name="value"/>.
        /// </summary>
        public SqlScriptFile Set(string parameter, string value)
        {
            var declaration = new Regex($@"^(DECLARE @{parameter} \S+ = )[^;]+;", RegexOptions.Multiline);
            if (declaration.Matches(_script).Count != 1)
                throw new InvalidOperationException($"{_path} must declare @{parameter} once, at the start of a line.");
            _script = declaration.Replace(_script, m => m.Groups[1].Value + value + ";");
            return this;
        }

        public static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";

        public static string Bit(bool value) => value ? "1" : "0";
    }
}
