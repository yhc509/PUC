using System.Reflection;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityCli.Cli.Models;
using UnityCli.Cli.Services;
using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ForceGateTests
{
    [Fact]
    public void ForceRequiredByCatalog_ReturnsTrueForEveryRequiresForceCommand()
    {
        CliCommandDescriptor[] forceCommands = CliCommandCatalog.GetCommands()
            .Where(command => command.RequiresForce)
            .ToArray();
        Assert.NotEmpty(forceCommands);

        foreach (CliCommandDescriptor command in forceCommands)
        {
            Assert.True(InvokeForceRequiredByCatalog(new ParsedCommand(CommandKind.Help), command.Command), command.Command);
            Assert.False(InvokeForceRequiredByCatalog(new ParsedCommand(CommandKind.Help) { Force = true }, command.Command), command.Command);
        }
    }

    [Theory]
    [MemberData(nameof(DestructiveCommandsWithoutForce))]
    public void Parse_DestructiveCommandWithoutForce_ThrowsUsage(string[] args)
    {
        var ex = Assert.Throws<CliUsageException>(() => CliArgumentParser.Parse(args));

        Assert.Contains("--force", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ForceEnvelopeCases))]
    public void Parse_DestructiveCommandWithForce_SerializesForceTrue(string[] args, string protocolCommand)
    {
        var parsed = CliArgumentParser.Parse(args);

        CommandEnvelope envelope = parsed.ToEnvelope();
        using JsonDocument arguments = JsonDocument.Parse(envelope.argumentsJson);

        Assert.Equal(protocolCommand, envelope.command);
        Assert.True(arguments.RootElement.GetProperty("force").GetBoolean());
    }

    [Fact]
    public void Parse_RawWithForce_InjectsForceTrueIntoArguments()
    {
        var parsed = CliArgumentParser.Parse([
            "raw",
            "--json", "{\"command\":\"package-remove\",\"arguments\":{\"name\":\"com.example.demo\",\"force\":false}}",
            "--force"
        ]);

        CommandEnvelope envelope = parsed.ToEnvelope();
        using JsonDocument arguments = JsonDocument.Parse(envelope.argumentsJson);

        Assert.Equal(ProtocolConstants.CommandPackageRemove, envelope.command);
        Assert.Equal("com.example.demo", arguments.RootElement.GetProperty("name").GetString());
        Assert.True(arguments.RootElement.GetProperty("force").GetBoolean());
    }

    [Fact]
    public void Parse_RawWithoutForce_PreservesPayloadForceValue()
    {
        var parsed = CliArgumentParser.Parse([
            "raw",
            "--json", "{\"command\":\"package-remove\",\"arguments\":{\"name\":\"com.example.demo\",\"force\":false,\"extra\":7}}"
        ]);

        CommandEnvelope envelope = parsed.ToEnvelope();
        using JsonDocument arguments = JsonDocument.Parse(envelope.argumentsJson);

        Assert.Equal(ProtocolConstants.CommandPackageRemove, envelope.command);
        Assert.Equal("com.example.demo", arguments.RootElement.GetProperty("name").GetString());
        Assert.False(arguments.RootElement.GetProperty("force").GetBoolean());
        Assert.Equal(7, arguments.RootElement.GetProperty("extra").GetInt32());
    }

    [Fact]
    public void ForceFields_RoundTripBetweenSystemTextJsonAndNewtonsoft()
    {
        Type[] forceTypes = typeof(CommandEnvelope).Assembly.GetTypes()
            .Where(type => type.Namespace == "UnityCli.Protocol")
            .Where(type => type.GetField("force", BindingFlags.Instance | BindingFlags.Public) != null)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(typeof(PackageRemoveArgs), forceTypes);

        foreach (Type type in forceTypes)
        {
            FieldInfo forceField = type.GetField("force", BindingFlags.Instance | BindingFlags.Public)!;
            object instance = Activator.CreateInstance(type)!;
            forceField.SetValue(instance, true);

            string systemTextJson = System.Text.Json.JsonSerializer.Serialize(instance, type, ProtocolJson.Default);
            Assert.Contains("\"force\":true", systemTextJson);
            Assert.DoesNotContain("\"Force\"", systemTextJson);

            object newtonsoftValue = JsonConvert.DeserializeObject(systemTextJson, type)!;
            Assert.True((bool)forceField.GetValue(newtonsoftValue)!);

            string newtonsoftJson = JsonConvert.SerializeObject(instance);
            var newtonsoftObject = JObject.Parse(newtonsoftJson);
            Assert.True(newtonsoftObject.Value<bool>("force"));
            Assert.Null(newtonsoftObject["Force"]);

            object? systemTextJsonValue = System.Text.Json.JsonSerializer.Deserialize(newtonsoftJson, type, ProtocolJson.Default);
            Assert.NotNull(systemTextJsonValue);
            Assert.True((bool)forceField.GetValue(systemTextJsonValue)!);
        }
    }

    public static TheoryData<string[]> DestructiveCommandsWithoutForce()
    {
        return new TheoryData<string[]>
        {
            new[] { "asset", "delete", "--path", "Assets/DeleteMe.asset" },
            new[] { "prefab", "patch", "--path", "Assets/Prefabs/Enemy.prefab", "--spec-json", "{\"version\":1,\"operations\":[{\"op\":\"remove-node\",\"target\":\"/Enemy[0]\"}]}" },
            new[] { "execute", "--code", "Debug.Log(42);" },
            new[] { "package", "remove", "--name", "com.example.demo" },
        };
    }

    public static TheoryData<string[], string> ForceEnvelopeCases()
    {
        return new TheoryData<string[], string>
        {
            {
                new[] { "asset", "delete", "--path", "Assets/DeleteMe.asset", "--force" },
                ProtocolConstants.CommandAssetDelete
            },
            {
                new[] { "prefab", "patch", "--path", "Assets/Prefabs/Enemy.prefab", "--spec-json", "{\"version\":1,\"operations\":[{\"op\":\"remove-node\",\"target\":\"/Enemy[0]\"}]}", "--force" },
                ProtocolConstants.CommandPrefabPatch
            },
            {
                new[] { "execute", "--code", "Debug.Log(42);", "--force" },
                ProtocolConstants.CommandExecuteCode
            },
            {
                new[] { "package", "remove", "--name", "com.example.demo", "--force" },
                ProtocolConstants.CommandPackageRemove
            },
        };
    }

    private static bool InvokeForceRequiredByCatalog(ParsedCommand parsed, string commandPath)
    {
        MethodInfo method = typeof(CliArgumentParser).GetMethod(
            "ForceRequiredByCatalog",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, [parsed, commandPath])!;
    }
}
