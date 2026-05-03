#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using UnityCli.Protocol;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class PackageCommandHandlerTests
    {
        [SetUp]
        public void SetUp()
        {
            PackageCommandHandler.ResetActiveRequestForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            PackageCommandHandler.ResetActiveRequestForTesting();
        }

        [Test]
        public void StartDeferred_WhenActiveRequestExists_ReturnsPackageBusy()
        {
            Assert.That(PackageCommandHandler.TryBeginActiveRequestForTesting(), Is.True);
            var completion = CreateCompletion("package-busy-request");

            new PackageCommandHandler().StartDeferred(
                ProtocolConstants.CommandPackageList,
                "{}",
                completion,
                "project-hash");

            Assert.That(completion.Task.IsCompleted, Is.True);
            ResponseEnvelope response = completion.Task.GetAwaiter().GetResult();

            Assert.That(response.requestId, Is.EqualTo("package-busy-request"));
            Assert.That(response.target, Is.EqualTo("project-hash"));
            Assert.That(response.status, Is.EqualTo(ProtocolConstants.StatusError));
            Assert.That(response.retryable, Is.True);
            Assert.That(response.error, Is.Not.Null);
            Assert.That(response.error!.code, Is.EqualTo(ProtocolConstants.ErrorPackageBusy));
            Assert.That(response.error.message, Is.EqualTo(ProtocolConstants.PackageBusyMessage));
        }

        [Test]
        public void ActiveRequestState_AllowsNewRequestAfterRelease()
        {
            Assert.That(PackageCommandHandler.TryBeginActiveRequestForTesting(), Is.True);
            Assert.That(PackageCommandHandler.TryBeginActiveRequestForTesting(), Is.False);
            Assert.That(PackageCommandHandler.HasActiveRequestForTesting(), Is.True);

            PackageCommandHandler.EndActiveRequestForTesting();

            Assert.That(PackageCommandHandler.HasActiveRequestForTesting(), Is.False);
            Assert.That(PackageCommandHandler.TryBeginActiveRequestForTesting(), Is.True);
        }

        [Test]
        public void ActiveRequestCompletionTracker_KeepsLockUntilRequestCompletes()
        {
            bool isCompleted = false;
            int stopTrackingCalls = 0;

            Assert.That(PackageCommandHandler.TryBeginActiveRequestForTesting(), Is.True);
            var tracker = new PackageCommandHandler.ActiveRequestCompletionTracker(
                () => isCompleted,
                () => stopTrackingCalls++,
                PackageCommandHandler.EndActiveRequestForTesting);

            tracker.Poll();

            Assert.That(PackageCommandHandler.HasActiveRequestForTesting(), Is.True);
            Assert.That(stopTrackingCalls, Is.EqualTo(0));

            isCompleted = true;
            tracker.Poll();

            Assert.That(PackageCommandHandler.HasActiveRequestForTesting(), Is.False);
            Assert.That(stopTrackingCalls, Is.EqualTo(1));
        }

        [Test]
        public void ActiveRequestCompletionTracker_ReleasesLockOnlyOnce()
        {
            int stopTrackingCalls = 0;
            int endActiveCalls = 0;
            var tracker = new PackageCommandHandler.ActiveRequestCompletionTracker(
                () => true,
                () => stopTrackingCalls++,
                () => endActiveCalls++);

            tracker.Poll();
            tracker.Poll();

            Assert.That(stopTrackingCalls, Is.EqualTo(1));
            Assert.That(endActiveCalls, Is.EqualTo(1));
        }

        private static TaskCompletionSource<ResponseEnvelope> CreateCompletion(string requestId)
        {
            return new TaskCompletionSource<ResponseEnvelope>(
                requestId,
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
