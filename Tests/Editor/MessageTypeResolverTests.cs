using System.Collections.Generic;
using NUnit.Framework;
using Tutan.Messages;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tutan.Messages.Tests
{
    // ── MessageTypeResolverTests ─────────────────────────────────────────

    public class MessageTypeResolverTests
    {
        public struct ResolverProbe : IEvent { public int Value; }

        [Test]
        public void Resolve_AssemblyQualifiedName_ReturnsType()
        {
            Assert.AreEqual(typeof(ResolverProbe),
                MessageTypeResolver.Resolve(typeof(ResolverProbe).AssemblyQualifiedName));
        }

        [Test]
        public void Resolve_FullNameWithoutAssembly_ReturnsType()
        {
            // Type.GetType alone only searches the calling assembly and mscorlib.
            Assert.AreEqual(typeof(ResolverProbe),
                MessageTypeResolver.Resolve(typeof(ResolverProbe).FullName));
        }

        [Test]
        public void Resolve_RenamedAssembly_FallsBackToFullNameLookup()
        {
            // Simulates a script moved into a different asmdef after the name was serialized.
            string stale = typeof(ResolverProbe).FullName + ", Some.Renamed.Assembly, Version=9.9.9.9, Culture=neutral, PublicKeyToken=null";
            Assert.AreEqual(typeof(ResolverProbe), MessageTypeResolver.Resolve(stale));
        }

        [Test]
        public void Resolve_UnknownOrEmpty_ReturnsNull_WithoutThrowing()
        {
            Assert.IsNull(MessageTypeResolver.Resolve(null));
            Assert.IsNull(MessageTypeResolver.Resolve(string.Empty));
            Assert.IsNull(MessageTypeResolver.Resolve("No.Such.Type, No.Such.Assembly"));
            Assert.IsNull(MessageTypeResolver.Resolve("[[,,]]"));
        }

        [Test]
        public void StripAssemblyName_KeepsGenericArgumentsIntact()
        {
            string aqn = typeof(List<int>).AssemblyQualifiedName;
            string stripped = MessageTypeResolver.StripAssemblyName(aqn);
            Assert.AreEqual(typeof(List<int>).FullName, stripped);
        }

        [Test]
        public void EventReference_WithStaleAssemblyName_StillResolves()
        {
            var reference = new EventReference
            {
                typeName = typeof(ResolverProbe).FullName + ", Old.Assembly",
                dataJson = "{\"Value\":7}"
            };
            Assert.AreEqual(typeof(ResolverProbe), reference.GetMessageType());
        }

        [Test]
        public void CreateMessage_CorruptJson_FallsBackToDefaults_AndWarns()
        {
            var reference = new EventReference
            {
                typeName = typeof(ResolverProbe).AssemblyQualifiedName,
                dataJson = "{ not json"
            };
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not valid JSON"));
            var message = reference.CreateMessage();
            Assert.IsInstanceOf<ResolverProbe>(message);
            Assert.AreEqual(0, ((ResolverProbe)message).Value);
        }
    }
}
