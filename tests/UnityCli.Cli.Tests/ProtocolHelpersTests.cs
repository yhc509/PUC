using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class ProtocolHelpersTests
{
    [Fact]
    public void GetSupportedCommands_IncludesSceneCommands()
    {
        string[] commands = ProtocolHelpers.GetSupportedCommands();

        Assert.Contains(ProtocolConstants.CommandExecuteCode, commands);
        Assert.Contains(ProtocolConstants.CommandCustom, commands);
        Assert.Contains(ProtocolConstants.CommandSceneOpen, commands);
        Assert.Contains(ProtocolConstants.CommandSceneInspect, commands);
        Assert.Contains(ProtocolConstants.CommandScenePatch, commands);
        Assert.Contains(ProtocolConstants.CommandSceneSetTransform, commands);
        Assert.Contains(ProtocolConstants.CommandSceneAssignMaterial, commands);
        Assert.Contains(ProtocolConstants.CommandSceneListComponents, commands);
    }

    [Fact]
    public void IsSceneCommand_RecognizesSceneSurface()
    {
        Assert.True(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandSceneOpen));
        Assert.True(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandSceneInspect));
        Assert.True(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandScenePatch));
        Assert.True(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandSceneSetTransform));
        Assert.True(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandSceneAssignMaterial));
        Assert.True(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandSceneListComponents));
        Assert.False(ProtocolHelpers.IsSceneCommand(ProtocolConstants.CommandPrefabPatch));
    }

    [Fact]
    public void BuiltInAssetCreateCatalog_NormalizesAliases()
    {
        Assert.True(BuiltInAssetCreateCatalog.TryNormalizeTypeId("controller", out string animatorController));
        Assert.True(BuiltInAssetCreateCatalog.TryNormalizeTypeId("rendertexture", out string renderTexture));
        Assert.True(BuiltInAssetCreateCatalog.TryNormalizeTypeId("scriptableobject", out string scriptableObject));

        Assert.Equal("animator-controller", animatorController);
        Assert.Equal("render-texture", renderTexture);
        Assert.Equal("scriptable-object", scriptableObject);
    }

    [Fact]
    public void BuiltInAssetCreateCatalog_DescriptorsIncludeSceneAndPrefab()
    {
        AssetCreateTypeDescriptor[] descriptors = BuiltInAssetCreateCatalog.GetDescriptors();

        Assert.Contains(descriptors, descriptor => descriptor.typeId == "scene");
        Assert.Contains(descriptors, descriptor => descriptor.typeId == "prefab");
    }

    [Fact]
    public void TestFullNameMatchesFilter_MatchesCaseInsensitiveSubstring()
    {
        const string fullName = "UnityCliBridge.Sample.EditMode.Tests.SmokeTests.Smoke_Arithmetic_Passes";

        Assert.True(ProtocolHelpers.TestFullNameMatchesFilter(fullName, "smoke"));
        Assert.True(ProtocolHelpers.TestFullNameMatchesFilter(fullName, "SMOKE"));
        Assert.True(ProtocolHelpers.TestFullNameMatchesFilter(fullName, "Smoke_Arithmetic_Passes"));
    }

    [Fact]
    public void TestFullNameMatchesFilter_RejectsMissingSubstring()
    {
        const string fullName = "UnityCliBridge.Sample.EditMode.Tests.SmokeTests.Smoke_Arithmetic_Passes";

        Assert.False(ProtocolHelpers.TestFullNameMatchesFilter(fullName, "NonExistent"));
        Assert.False(ProtocolHelpers.TestFullNameMatchesFilter(fullName, string.Empty));
        Assert.False(ProtocolHelpers.TestFullNameMatchesFilter(string.Empty, "Smoke"));
    }
}
