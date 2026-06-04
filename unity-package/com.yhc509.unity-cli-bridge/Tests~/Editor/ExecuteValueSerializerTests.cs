#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor.Tests
{
    public sealed class ExecuteValueSerializerTests
    {
        [Test] public void Null_Serializes_AsJsonNull()
            => Assert.AreEqual("null", ExecuteValueSerializer.Serialize(null));

        [Test] public void Bool_Serializes_AsLowercase()
            => Assert.AreEqual("true", ExecuteValueSerializer.Serialize(true));

        [Test] public void Int_Serializes_Raw()
            => Assert.AreEqual("42", ExecuteValueSerializer.Serialize(42));

        [Test] public void Float_Uses_G9_RoundTrip()
        {
            // 0.1f의 round-trip 표현은 "0.100000001"
            Assert.AreEqual("0.100000001", ExecuteValueSerializer.Serialize(0.1f));
        }

        [Test] public void Double_Uses_G17_RoundTrip()
        {
            Assert.AreEqual("0.10000000000000001", ExecuteValueSerializer.Serialize(0.1d));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Float_NonFinite_Serializes_AsNull(float value)
        {
            Assert.AreEqual("null", ExecuteValueSerializer.Serialize(value));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Double_NonFinite_Serializes_AsNull(double value)
        {
            Assert.AreEqual("null", ExecuteValueSerializer.Serialize(value));
        }

        [Test] public void String_Is_Escaped()
            => Assert.AreEqual("\"a\\\"b\\nc\"", ExecuteValueSerializer.Serialize("a\"b\nc"));

        [Test] public void Array_Serializes_AsJsonArray()
            => Assert.AreEqual("[1,2,3]", ExecuteValueSerializer.Serialize(new[] { 1, 2, 3 }));

        [Test] public void Dictionary_Serializes_AsObject()
            => Assert.AreEqual("{\"a\":1}", ExecuteValueSerializer.Serialize(new Dictionary<string, int> { ["a"] = 1 }));

        [Test] public void Vector3_Serializes_Fields()
        {
            var json = ExecuteValueSerializer.Serialize(new Vector3(1f, 2f, 3f));
            Assert.AreEqual("{\"x\":1,\"y\":2,\"z\":3}", json);
        }

        [Test] public void Vector3_NonFiniteComponent_Serializes_AsNull()
        {
            var json = ExecuteValueSerializer.Serialize(new Vector3(float.NaN, 1f, 2f));
            Assert.AreEqual("{\"x\":null,\"y\":1,\"z\":2}", json);
        }

        [Test] public void PlainObject_Uses_PublicMembers()
        {
            var json = ExecuteValueSerializer.Serialize(new { name = "go", count = 2 });
            StringAssert.Contains("\"name\":\"go\"", json);
            StringAssert.Contains("\"count\":2", json);
        }

        [Test] public void CyclicReference_Does_Not_StackOverflow()
        {
            var a = new Node();
            a.Self = a;
            Assert.DoesNotThrow(() => ExecuteValueSerializer.Serialize(a));
        }

        private sealed class Node { public Node? Self; }
    }
}
