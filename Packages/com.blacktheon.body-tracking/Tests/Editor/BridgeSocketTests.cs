using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using NUnit.Framework;

namespace Blacktheon.BodyTracking.Tests
{
    public sealed class BridgeSocketTests
    {
        [Test]
        public void FragmentedTextReassemblesEvenAcrossUtf8CodePoints()
        {
            var reader = new WebSocketFrameBuffer();
            byte[] data = Encoding.UTF8.GetBytes("鞋 L");
            Assert.That(Feed(reader, data, 0, 2, false), Is.Null);
            Assert.That(Feed(reader, data, 2, data.Length - 2, true), Is.EqualTo("鞋 L"));
            Assert.That(Feed(reader, data, 0, data.Length, true), Is.EqualTo("鞋 L"));
        }

        [Test]
        public void OversizeTextIsRejected()
        {
            var reader = new WebSocketFrameBuffer();
            int capacity = reader.Remaining.Count;
            Assert.Throws<InvalidDataException>(() => reader.Append(new WebSocketReceiveResult(capacity, WebSocketMessageType.Text, false)));
        }

        [Test]
        public void BinaryBroadcastsCanSpanSeveralBuffersBetweenTextMessages()
        {
            var reader = new WebSocketFrameBuffer();
            var text = Encoding.UTF8.GetBytes("pose");
            Assert.That(Feed(reader, text, 0, text.Length, true), Is.EqualTo("pose"));
            int capacity = reader.Remaining.Count;
            Assert.That(reader.Append(new WebSocketReceiveResult(capacity, WebSocketMessageType.Binary, false)), Is.Null);
            Assert.That(reader.Remaining.Count, Is.EqualTo(capacity));
            Assert.That(reader.Append(new WebSocketReceiveResult(capacity, WebSocketMessageType.Binary, true)), Is.Null);
            Assert.That(Feed(reader, text, 0, text.Length, true), Is.EqualTo("pose"));
        }

        private static string Feed(WebSocketFrameBuffer reader, byte[] data, int offset, int count, bool end)
        {
            var target = reader.Remaining;
            Array.Copy(data, offset, target.Array, target.Offset, count);
            return reader.Append(new WebSocketReceiveResult(count, WebSocketMessageType.Text, end));
        }
    }
}
