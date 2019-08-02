﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Renci.SshNet.Channels;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Connection;
using Renci.SshNet.Tests.Common;

namespace Renci.SshNet.Tests.Classes
{
    /// <summary>
    /// Contains operation for working with SSH Shell.
    /// </summary>
    [TestClass]
    public class ShellStreamTest : TestBase
    {
        private Mock<ISession> _sessionMock;
        private Mock<IConnectionInfo> _connectionInfoMock;
        private Encoding _encoding;
        private string _terminalName;
        private uint _widthColumns;
        private uint _heightRows;
        private uint _widthPixels;
        private uint _heightPixels;
        private Dictionary<TerminalModes, uint> _terminalModes;
        private int _bufferSize;
        private Mock<IChannelSession> _channelSessionMock;

        protected override void OnInit()
        {
            base.OnInit();

            var random = new Random();
            _terminalName = random.Next().ToString(CultureInfo.InvariantCulture);
            _widthColumns = (uint) random.Next();
            _heightRows = (uint) random.Next();
            _widthPixels = (uint)random.Next();
            _heightPixels = (uint)random.Next();
            _terminalModes = new Dictionary<TerminalModes, uint>();
            _bufferSize = random.Next(100, 500);

            _encoding = Encoding.UTF8;
            _sessionMock = new Mock<ISession>(MockBehavior.Strict);
            _connectionInfoMock = new Mock<IConnectionInfo>(MockBehavior.Strict);
            _channelSessionMock = new Mock<IChannelSession>(MockBehavior.Strict);
        }

        [TestMethod] // issue #2190
        public void ReadLine_MultiByteCharacters()
        {
            // bash: /root/menu.sh: Отказан
            const string data1 = "bash: /root/menu.sh: \u041e\u0442\u043a\u0430\u0437\u0430\u043d";
            // о в доступе
            const string data2 = "\u043e \u0432 \u0434\u043e\u0441\u0442\u0443\u043f\u0435";
            // done
            const string data3 = "done";

            var shellStream = CreateShellStream();

            var channelDataPublishThread = new Thread(() =>
                {
                    _channelSessionMock.Raise(p => p.DataReceived += null,
                        new ChannelDataEventArgs(5, _encoding.GetBytes(data1)));
                    Thread.Sleep(50);
                    _channelSessionMock.Raise(p => p.DataReceived += null,
                        new ChannelDataEventArgs(5, _encoding.GetBytes(data2 + "\r\n")));
                    _channelSessionMock.Raise(p => p.DataReceived += null,
                        new ChannelDataEventArgs(5, _encoding.GetBytes(data3 + "\r\n")));
                });
            channelDataPublishThread.Start();

            Assert.AreEqual(data1 + data2, shellStream.ReadLine());
            Assert.AreEqual(data3, shellStream.ReadLine());

            channelDataPublishThread.Join();
        }

        [TestMethod]
        public void Write_Text_ShouldWriteNothingWhenTextIsNull()
        {
            var shellStream = CreateShellStream();
            const string text = null;

            shellStream.Write(text);

            _channelSessionMock.Verify(p => p.SendData(It.IsAny<byte[]>()), Times.Never);
        }

        [TestMethod]
        public void WriteLine_Line_ShouldOnlyWriteLineTerminatorWhenLineIsNull()
        {
            var shellStream = CreateShellStream();
            const string line = null;
            var lineTerminator = _encoding.GetBytes("\r");

            _channelSessionMock.Setup(p => p.SendData(lineTerminator));

            shellStream.WriteLine(line);
            shellStream.Flush();

            _channelSessionMock.Verify(p => p.SendData(lineTerminator), Times.Once);
        }

        [TestMethod]
        public void Expect_Regex_ShouldOnlyReturnUpToTheMatchedBuffer()
        {
            var shellStream = CreateShellStream();
            var input = "abc\rdef\rghi\rprompt> ";
            var bytes = _encoding.GetBytes(input);
            var regex = new Regex(@"prompt>");

            _channelSessionMock.Raise(p => p.DataReceived += null, this, new ChannelDataEventArgs(0, bytes));
            var output = shellStream.Expect(regex, TimeSpan.FromSeconds(1));

            Assert.AreEqual(input.Substring(0, input.Length - 1), output);

        }

        [TestMethod]
        public void Expect_Regex_ShouldNotWaitForMoreDataWhenDisposed()
        {
            SetupDispose();
            var shellStream = CreateShellStream();
            var timeout = 2;

            _channelSessionMock.Raise(p => p.Closed += null, this, new ChannelEventArgs(0));

            var now = DateTime.Now;
            var output = shellStream.Expect("not there", TimeSpan.FromSeconds(timeout));
            var end = DateTime.Now;

            Assert.AreEqual(null, output);
            Assert.IsTrue((end - now).TotalSeconds < timeout);
        }

        [TestMethod]
        public void Closed_Event_ShouldRaiseWhenIChannelClosed()
        {
            var shellStream = CreateShellStream();
            SetupDispose();
            var called = false;

            shellStream.Closed += (sender, args) => 
            {
                called = true;
                Assert.IsFalse(shellStream.Disposed);
            };

            _channelSessionMock.Raise(p => p.Closed += null, this, new ChannelEventArgs(0));

            Assert.IsTrue(called);
            Assert.IsTrue(shellStream.Disposed);
        }

        [TestMethod]
        public void Write_AlwaysUsesUnderlyingBuffer()
        {
            var shellStream = CreateShellStream();
            var command1 = "abcd\r";
            var command2 = "efgh\r";
            var command1Bytes = _encoding.GetBytes(command1);
            var command2Bytes = _encoding.GetBytes(command2);
            var expectedBytes = command1Bytes.Concat(command2Bytes);

            _channelSessionMock.Setup(p => p.SendData(It.IsAny<byte[]>()));

            shellStream.Write(command1Bytes, 0, command1Bytes.Length);
            shellStream.Write(command2);
            shellStream.Flush();

            _channelSessionMock.Verify(p => p.SendData(expectedBytes), Times.Once);
        }

        [TestMethod]
        public void Expect_ShouldAlwaysFlushTheWriteBuffer()
        {
            var shellStream = CreateShellStream();
            var command1 = "abcd\r";
            var command2 = "efgh\r";
            var command1Bytes = _encoding.GetBytes(command1);
            var command2Bytes = _encoding.GetBytes(command2);
            var expectedBytes = command1Bytes.Concat(command2Bytes);

            _channelSessionMock.Setup(p => p.SendData(It.IsAny<byte[]>()))
                .Raises(p => p.DataReceived += null, new ChannelDataEventArgs(0, expectedBytes));
            

            shellStream.Write(command1Bytes, 0, command1Bytes.Length);
            shellStream.Write(command2);
            var output = shellStream.Expect("h\r", TimeSpan.FromMilliseconds(1));

            _channelSessionMock.Verify(p => p.SendData(expectedBytes), Times.Once);
            Assert.AreEqual(command1 + command2, output);
        }

        [TestMethod]
        public void Read_ShouldAlwaysFlushTheWriteBuffer()
        {
            var shellStream = CreateShellStream();
            var command1 = "abcd\r";
            var command2 = "efgh\r";
            var command1Bytes = _encoding.GetBytes(command1);
            var command2Bytes = _encoding.GetBytes(command2);
            var expectedBytes = command1Bytes.Concat(command2Bytes);

            _channelSessionMock.Setup(p => p.SendData(It.IsAny<byte[]>()))
                .Raises(p => p.DataReceived += null, new ChannelDataEventArgs(0, expectedBytes));


            shellStream.Write(command1Bytes, 0, command1Bytes.Length);
            shellStream.Write(command2);
            var output = shellStream.Read();

            _channelSessionMock.Verify(p => p.SendData(expectedBytes), Times.Once);
            Assert.AreEqual(command1 + command2, output);
        }

        [TestMethod]
        public void Read_Bytes_ShouldAlwaysFlushTheWriteBuffer()
        {
            var shellStream = CreateShellStream();
            var command1 = "abcd\r";
            var command2 = "efgh\r";
            var command1Bytes = _encoding.GetBytes(command1);
            var command2Bytes = _encoding.GetBytes(command2);
            var expectedBytes = command1Bytes.Concat(command2Bytes);

            _channelSessionMock.Setup(p => p.SendData(It.IsAny<byte[]>()))
                .Raises(p => p.DataReceived += null, new ChannelDataEventArgs(0, expectedBytes));


            shellStream.Write(command1Bytes, 0, command1Bytes.Length);
            shellStream.Write(command2);
            var output = new byte[expectedBytes.Length];
            var count = shellStream.Read(output, 0, output.Length);

            _channelSessionMock.Verify(p => p.SendData(expectedBytes), Times.Once);
            CollectionAssert.AreEqual(expectedBytes, output);
            Assert.AreEqual(expectedBytes.Length, count);
        }

        [TestMethod]
        public void ReadLine_ShouldAlwaysFlushTheWriteBuffer()
        {
            var shellStream = CreateShellStream();
            var command1 = "abcd\r\n";
            var command2 = "efgh\r\n";
            var command1Bytes = _encoding.GetBytes(command1);
            var command2Bytes = _encoding.GetBytes(command2);
            var expectedBytes = command1Bytes.Concat(command2Bytes);

            _channelSessionMock.Setup(p => p.SendData(It.IsAny<byte[]>()))
                .Raises(p => p.DataReceived += null, new ChannelDataEventArgs(0, expectedBytes));


            shellStream.Write(command1Bytes, 0, command1Bytes.Length);
            shellStream.Write(command2);

            var output1 = shellStream.ReadLine(TimeSpan.FromMilliseconds(1));
            var output2 = shellStream.ReadLine(TimeSpan.FromMilliseconds(1));

            _channelSessionMock.Verify(p => p.SendData(expectedBytes), Times.Once);
            Assert.AreEqual(command1.Substring(0, command1.Length - 2), output1);
            Assert.AreEqual(command2.Substring(0, command2.Length - 2), output2);
        }

        private ShellStream CreateShellStream()
        {
            _sessionMock.Setup(p => p.ConnectionInfo).Returns(_connectionInfoMock.Object);
            _connectionInfoMock.Setup(p => p.Encoding).Returns(_encoding);
            _sessionMock.Setup(p => p.CreateChannelSession()).Returns(_channelSessionMock.Object);
            _channelSessionMock.Setup(p => p.Open());
            _channelSessionMock.Setup(p => p.SendPseudoTerminalRequest(_terminalName, _widthColumns, _heightRows,
                _widthPixels, _heightPixels, _terminalModes)).Returns(true);
            _channelSessionMock.Setup(p => p.SendShellRequest()).Returns(true);

            return new ShellStream(_sessionMock.Object,
                                   _terminalName,
                                   _widthColumns,
                                   _heightRows,
                                   _widthPixels,
                                   _heightPixels,
                                   _terminalModes,
                                   _bufferSize);
        }

        private void SetupDispose()
        {
            _channelSessionMock.Setup(t => t.Dispose());
        }
    }
}