//
// Copyright (c) 2017 Michael W Powell <mwpowellhtx@gmail.com>
// Copyright 2017 Garrett D'Amore <garrett@damore.org>
// Copyright 2017 Capitar IT Group BV <info@capitar.com>
//
// This software is supplied under the terms of the MIT License, a
// copy of which should be located in the distribution where this
// file was obtained (LICENSE.txt).  A copy of the license may also be
// found online at https://opensource.org/licenses/MIT.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nanomsg2.Sharp
{
    using Messaging;
    using Xunit;
    using Xunit.Abstractions;
    //using static Math;
    using static Imports;
    using static SocketAddressFamily;

    public abstract class TestFixtureBase : IRequiresUniquePort
    {
        // TODO: TBD: potentially belonging in its own "session" class... possibly needing to also be thread local storage?
        [DllImport(NngDll, EntryPoint = "nng_fini", CallingConvention = Cdecl)]
        private static extern void Fini();

        protected ITestOutputHelper Out { get; }

        private static readonly object Sync = new object();
        private static long _count = 0;

        protected TestFixtureBase(ITestOutputHelper @out)
        {
            lock (Sync) _count++;
            Out = @out;

            Out.WriteLine($"Current process Id: {Process.GetCurrentProcess().Id}");
            Out.WriteLine($"Managed thread Id: {Thread.CurrentThread.ManagedThreadId}");
        }

        ~TestFixtureBase()
        {
            //// TODO: TBD: seems to me like this is better; but still an issue in the runner?
            //lock (Sync)
            //{
            //    --_count;
            //    if (_count == 0) Fini();
            //}
            //// TODO: TBD: could it be this was causing a premature shutdown? running multiple tests? doesn't seem like it...
            //Fini();
        }

        protected static void VerifyDefaultSocket<T>(T s)
            where T : Socket, new()
        {
            Assert.NotNull(s);
            Assert.True(s.HasOne);
            Assert.NotNull(s.Options);
            Assert.True(s.Options.HasOne);
        }

        protected static T CreateOne<T>()
            where T : Socket, new()
        {
            var s = new T();
            VerifyDefaultSocket(s);
            return s;
        }

        protected static void ConfigureAll(Action<Socket> action, params Socket[] sockets)
        {
            Assert.NotNull(action);
            sockets.ToList().ForEach(action);
        }

        protected static void VerifyDefaultMessagePipe(MessagePipe p, bool expectingOne)
        {
            Assert.NotNull(p);
            Assert.Equal(expectingOne, p.HasOne);
            Assert.NotNull(p.Options);
        }

        protected static MessagePipe CreateMessagePipe(Message m, bool expectingOne = true)
        {
            var pipe = new MessagePipe(m);
            VerifyDefaultMessagePipe(pipe, expectingOne);
            return pipe;
        }

        protected static void VerifyDefaultMessage(Message m)
        {
            Assert.NotNull(m);
            Assert.True(m.HasOne);
            Assert.True(m.Size == 0ul);
        }

        protected static Message CreateMessage()
        {
            var message = new Message();
            VerifyDefaultMessage(message);
            return message;
        }

        protected static void DisposeAll(params IDisposable[] items)
        {
            (items??new IDisposable[0]).ToList().ForEach(d => d?.Dispose());
        }
    }

    internal static class PortExtensionMethods
    {
        private const ushort MinPort = 10000;
        //private const ushort MaxPort = 10999;

        private static readonly object Sync = new object();

        //private static ushort? _port;

        private static readonly IDictionary<Type, ushort> CurrentPorts;

        static PortExtensionMethods()
        {
            var current = MinPort;
            const ushort delta = 0x10;

            var requiresUniquePort = typeof(IRequiresUniquePort);

            var types = typeof(PortExtensionMethods).Assembly.GetTypes()
                .Where(x => x.IsClass && !x.IsAbstract
                            && requiresUniquePort.IsAssignableFrom(x)).ToArray();

            CurrentPorts = new ConcurrentDictionary<Type, ushort>(
                types.ToDictionary(k => k, x => current += delta)
            );
        }

        private static ushort GetPort(Type rootType, ushort delta)
        {
            //_port = _port ?? 0;
            //_port += delta;
            //// ReSharper disable once PossibleInvalidOperationException
            //return (_port = Max(MinPort, Min(_port.Value, MaxPort))).Value;

            var requiresUniquePort = typeof(IRequiresUniquePort);

            if (!requiresUniquePort.IsAssignableFrom(rootType))
            {
                throw new ArgumentException($"Unable to service '{rootType.FullName}'", nameof(rootType));
            }

            var port = CurrentPorts[rootType];

            CurrentPorts[rootType] = (ushort) (port + delta);

            return port;
        }

        public static string WithPort(this string addr, Type rootType, ushort delta = 1000)
        {
            return $"{addr}:{GetPort(rootType, delta)}";
        }

        public static string BuildAddress<T>(this SocketAddressFamily family)
            => family.BuildAddress(typeof(T));

        public static string BuildAddress(this SocketAddressFamily family, Type rootType)
        {
            /* Guard against potentially parallel execution paths. We want unique
             * addresses regardless of where/when the request originated. */
            lock (Sync)
            {
                var uuid = Guid.NewGuid();
                // ReSharper disable once SwitchStatementMissingSomeCases
                switch (family)
                {
                    case InProcess:
                        return $"inproc://{uuid}";

                    case InterProcess:
                        return $"ipc://pipe/{uuid}";

                    case IPv4:
                        return $"tcp://127.0.0.1".WithPort(rootType);

                    case IPv6:
                        return $"tcp://[::1]".WithPort(rootType);

                    case Unspecified:
                    default:
                        throw new ArgumentException($"Address invalid for family '{family}'", nameof(family));
                }
            }
        }
    }
}
