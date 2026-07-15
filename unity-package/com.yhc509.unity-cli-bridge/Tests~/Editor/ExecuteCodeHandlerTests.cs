#nullable enable
using NUnit.Framework;
using UnityCli.Protocol;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class ExecuteCodeHandlerTests
    {
        [Test]
        public void Handle_WhenUserCodeThrows_RaisesCommandFailure()
        {
            var handler = new ExecuteCodeHandler();
            string argsJson = ProtocolJson.Serialize(new ExecuteCodeArgs
            {
                code = "throw new System.Exception(\"boom\");",
                force = true,
            });

            var ex = Assert.Throws<CommandFailureException>(
                () => handler.Handle(ProtocolConstants.CommandExecuteCode, argsJson));

            Assert.That(ex!.ErrorCode, Is.EqualTo("EXECUTE_FAILED"));
            Assert.That(ex.Message, Does.Contain("boom"));
        }

        [Test]
        public void Handle_WhenUserCodeSucceeds_ReturnsSuccessPayload()
        {
            var handler = new ExecuteCodeHandler();
            string argsJson = ProtocolJson.Serialize(new ExecuteCodeArgs
            {
                code = "var __x = 1 + 1;",
                force = true,
            });

            string result = handler.Handle(ProtocolConstants.CommandExecuteCode, argsJson);
            ExecuteCodePayload? payload = ProtocolJson.Deserialize<ExecuteCodePayload>(result);

            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.success, Is.True);
        }
    }
}
