#nullable enable
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

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

    public sealed class QaCommandHandlerUiDumpTextTests
    {
        [Test]
        public void GetFirstTextValue_ForNestedClickableElements_UsesOnlyOwnedLabelSubtree()
        {
            GameObject parent = CreateClickable("ParentButton");
            GameObject child = CreateClickable("ChildButton", parent);
            CreateLabel("ChildLabel", "Save", child);
            CreateLabel("ParentLabel", "Auto", parent);

            try
            {
                Assert.That(QaCommandHandler.GetFirstTextValue(parent), Is.EqualTo("Auto"));
                Assert.That(QaCommandHandler.GetFirstTextValue(child), Is.EqualTo("Save"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void GetFirstTextValue_ForSingleClickableElement_ReturnsDescendantLabel()
        {
            GameObject button = CreateClickable("Button");
            CreateLabel("Label", "Play", button);

            try
            {
                Assert.That(QaCommandHandler.GetFirstTextValue(button), Is.EqualTo("Play"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(button);
            }
        }

        private static GameObject CreateClickable(string name, GameObject? parent = null)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(TestClickHandler));
            if (parent != null)
            {
                gameObject.transform.SetParent(parent.transform, worldPositionStays: false);
            }

            return gameObject;
        }

        private static GameObject CreateLabel(string name, string text, GameObject parent)
        {
            var gameObject = new GameObject(name, typeof(TestTextComponent));
            gameObject.transform.SetParent(parent.transform, worldPositionStays: false);
            gameObject.GetComponent<TestTextComponent>().text = text;
            return gameObject;
        }

        private sealed class TestClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData eventData)
            {
            }
        }

        private sealed class TestTextComponent : MonoBehaviour
        {
            public string text { get; set; } = string.Empty;
        }
    }
}
