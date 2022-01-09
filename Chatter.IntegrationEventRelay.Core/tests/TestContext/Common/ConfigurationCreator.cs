using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;

public class ConfigurationCreator : MockCreator<IConfiguration>
{
    public ConfigurationCreator(IMockContext newContext, IConfiguration creation = null)
        : base(newContext, creation)
    {
    }

    public ConfigurationCreator FromSectionObject<TSection>(TSection sectionObject, string sectionName = "") where TSection : class, new()
    {
        var appSettings = JsonSerializer.Serialize(sectionObject, new JsonSerializerOptions() { IgnoreReadOnlyProperties = true }); ;
        var builder = new ConfigurationBuilder();
        builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(appSettings)));
        Mock = builder.Build();

        return this;
    }
}
