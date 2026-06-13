#nullable enable
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class ScreenshotCommandHandlerTests
    {
        [SetUp]
        public void SetUp()
        {
            ScreenshotCommandHandler.ResetLastCapturedSize();
        }

        [TearDown]
        public void TearDown()
        {
            ScreenshotCommandHandler.ResetLastCapturedSize();
        }

        [Test]
        public void ResetLastCapturedSize_ClearsCachedDimensions()
        {
            ScreenshotCommandHandler.SetLastCapturedSizeForTesting(961, 554);

            ScreenshotCommandHandler.ResetLastCapturedSize();

            Assert.That(ScreenshotCommandHandler.LastCapturedWidth, Is.Zero);
            Assert.That(ScreenshotCommandHandler.LastCapturedHeight, Is.Zero);
        }

        [TestCase(PlayModeStateChange.ExitingPlayMode)]
        [TestCase(PlayModeStateChange.EnteredPlayMode)]
        public void OnPlayModeStateChanged_ForPlayModeBoundary_ClearsCachedDimensions(PlayModeStateChange state)
        {
            ScreenshotCommandHandler.SetLastCapturedSizeForTesting(961, 554);

            InvokePlayModeStateChanged(state);

            Assert.That(ScreenshotCommandHandler.LastCapturedWidth, Is.Zero);
            Assert.That(ScreenshotCommandHandler.LastCapturedHeight, Is.Zero);
        }

        [Test]
        public void RegistrationOnLoad_SubscribesOnPlayModeStateChanged()
        {
            Assert.That(
                CountScreenshotHandlerPlayModeStateChangedSubscribers(),
                Is.EqualTo(1),
                "ScreenshotCommandHandler should subscribe during editor load so play mode transitions clear cached screenshot dimensions.");

            InvokeEnsurePlayModeStateChangedSubscribed();
            InvokeEnsurePlayModeStateChangedSubscribed();

            Assert.That(
                CountScreenshotHandlerPlayModeStateChangedSubscribers(),
                Is.EqualTo(1),
                "Repeated registration should stay idempotent.");
        }

        private static void InvokePlayModeStateChanged(PlayModeStateChange state)
        {
            MethodInfo? method = typeof(ScreenshotCommandHandler).GetMethod(
                "OnPlayModeStateChanged",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method!.Invoke(null, new object[] { state });
        }

        private static void InvokeEnsurePlayModeStateChangedSubscribed()
        {
            MethodInfo? method = typeof(ScreenshotCommandHandler).GetMethod(
                "EnsurePlayModeStateChangedSubscribed",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method!.Invoke(null, Array.Empty<object>());
        }

        private static int CountScreenshotHandlerPlayModeStateChangedSubscribers()
        {
            int count = 0;
            foreach (Delegate subscriber in GetPlayModeStateChangedSubscribers())
            {
                MethodInfo method = subscriber.Method;
                if (method.DeclaringType == typeof(ScreenshotCommandHandler)
                    && string.Equals(method.Name, "OnPlayModeStateChanged", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static Delegate[] GetPlayModeStateChangedSubscribers()
        {
            FieldInfo? field = typeof(EditorApplication).GetField(
                "m_PlayModeStateChangedEvent",
                BindingFlags.Static | BindingFlags.NonPublic);
            field ??= typeof(EditorApplication).GetField(
                "playModeStateChanged",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(field, Is.Not.Null);

            Delegate? callback = field!.GetValue(null) as Delegate;
            return callback?.GetInvocationList() ?? Array.Empty<Delegate>();
        }
    }
}
