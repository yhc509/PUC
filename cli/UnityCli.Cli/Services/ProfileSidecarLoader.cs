#nullable enable
using System.IO;
using System.Text.Json;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

/// <summary>Shared local read of a capture sidecar for the CLI-local profile commands. No IPC.</summary>
internal static class ProfileSidecarLoader
{
    /// <summary>
    /// Resolves and deserializes the capture sidecar at
    /// <c>{projectRoot}/Library/com.yhc509.unity-cli-bridge/profiles/{captureId}.json</c>.
    /// Returns false and fills <paramref name="failure"/> with the response envelope on any error.
    /// </summary>
    internal static bool TryLoad(
        string? projectRoot,
        string? captureId,
        out ProfileSidecarFile sidecar,
        out ResponseEnvelope? failure)
    {
        sidecar = null!;
        failure = null;

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            failure = ResponseEnvelope.Failure(
                "local",
                null,
                "CLI_USAGE",
                "프로젝트 루트를 찾을 수 없습니다. Unity 프로젝트 안에서 실행하거나 --project를 지정하세요.",
                false,
                0,
                "cli");
            return false;
        }

        string sidecarPath = Path.Combine(
            projectRoot,
            ProtocolConstants.ProfilesDirectoryRelative.Replace('/', Path.DirectorySeparatorChar),
            captureId + ".json");
        if (!File.Exists(sidecarPath))
        {
            failure = ResponseEnvelope.Failure(
                "local",
                null,
                ProtocolConstants.ErrorProfileNotFound,
                $"captureId `{captureId}`의 sidecar를 찾을 수 없습니다: {sidecarPath}",
                false,
                0,
                "cli");
            return false;
        }

        ProfileSidecarFile? loaded;
        try
        {
            loaded = ProtocolJson.Deserialize<ProfileSidecarFile>(File.ReadAllText(sidecarPath));
        }
        catch (JsonException exception)
        {
            failure = ResponseEnvelope.Failure(
                "local",
                null,
                ProtocolConstants.ErrorProfileFailed,
                "sidecar JSON을 읽지 못했습니다: " + exception.Message,
                false,
                0,
                "cli");
            return false;
        }

        if (loaded is null)
        {
            failure = ResponseEnvelope.Failure(
                "local", null, ProtocolConstants.ErrorProfileFailed, "sidecar가 비어 있습니다.", false, 0, "cli");
            return false;
        }

        sidecar = loaded;
        return true;
    }
}
