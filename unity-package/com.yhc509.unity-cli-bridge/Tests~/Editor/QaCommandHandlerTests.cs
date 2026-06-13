#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityCliBridge.Bridge.Editor.Tests
{
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
