﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using Orleans;
using Orleans.CodeGeneration;
using Orleans.Internals;
using Orleans.Concurrency;

namespace Orleankka.Core
{
    using Utility;
    using Annotations;

    public class ActorType
    {
        internal static Dispatcher Dispatcher(Type actor) => dispatchers.Find(actor) ?? new Dispatcher(actor);

        static readonly Dictionary<Type, Dispatcher> dispatchers =
                    new Dictionary<Type, Dispatcher>();

        static readonly Dictionary<string, ActorType> types =
                    new Dictionary<string, ActorType>();

        static readonly Dictionary<int, ActorType> typeCodes =
                    new Dictionary<int, ActorType>();

        public static ActorType Of<T>() => Of(typeof(T));
        public static ActorType Of(Type type) => Of(ActorTypeName.Of(type));

        public static ActorType Of(string name)
        {
            Requires.NotNull(name, nameof(name));

            var result = types.Find(name);
            if (result == null)
                throw new InvalidOperationException(
                    $"Unable to map actor type name '{name}' to the corresponding actor implementation class");

            return result;
        }

        public static ActorType Of(int typeCode)
        {
            var result = typeCodes.Find(typeCode);
            if (result == null)
                throw new InvalidOperationException(
                    $"Unable to map actor type code '{typeCode}' to the corresponding actor type");

            return result;
        }
        
        internal static void Register(Assembly[] assemblies, string[] conventions)
        {
            var unregistered = assemblies
                .SelectMany(x => x.ActorTypes())
                .Where(x => !types.ContainsKey(ActorTypeName.Of(x)));

            using (Trace.Execution("Generation of actor implementation assemblies"))
            {
                var actors = ActorTypeDeclaration.Generate(assemblies.ToArray(), unregistered, conventions);

                foreach (var actor in actors)
                {
                    types.Add(actor.Name, actor);
                    typeCodes.Add(actor.TypeCode, actor);
                    typeCodes.Add(actor.Interface.TypeCode, actor);
                }
            }
        }

        public static IEnumerable<ActorType> Registered() => types.Values;
        internal string Name => Interface.Mapping.TypeName;

        public readonly Type Class;
        public readonly ActorInterface Interface;
        public readonly int TypeCode;
        internal readonly Type Grain;

        readonly TimeSpan keepAliveTimeout;
        readonly Func<object, bool> interleavePredicate;
        readonly string invoker;
        readonly Dispatcher dispatcher;

        internal ActorType(Type @class, ActorInterface @interface, Type grain, string[] conventions)
        {
            Class = @class;
            Interface = @interface;
            Grain = grain;
            TypeCode = grain.TypeCode();
            
            Sticky = StickyAttribute.IsApplied(@class);
            keepAliveTimeout = Sticky ? TimeSpan.FromDays(365 * 10) : KeepAliveAttribute.Timeout(@class);
            interleavePredicate = InterleaveAttribute.MayInterleavePredicate(@class);
            invoker = InvokerAttribute.From(@class);
            
            dispatcher = new Dispatcher(@class, conventions);
            dispatchers.Add(@class, dispatcher);

            Init(grain);            
        }

        void Init(Type grain)
        {
            Debug.Assert(grain.BaseType != null);
            var field = grain.BaseType.GetField("type", BindingFlags.NonPublic | BindingFlags.Static);

            Debug.Assert(field != null);
            field.SetValue(null, this);
        }

        internal Actor Activate(IActorHost host, ActorPath path, IActorRuntime runtime, IActorActivator activator)
        {
            var instance = activator.Activate(Class, path.Id, runtime, dispatcher);
            instance.Initialize(host, path, runtime, dispatcher);
            return instance;
        }

        internal IActorInvoker Invoker(ActorInvocationPipeline pipeline) => 
            pipeline.GetInvoker(Class, invoker);

        /// <summary> 
        /// FOR INTERNAL USE ONLY!
        /// </summary>
        [UsedImplicitly]
        public bool MayInterleave(InvokeMethodRequest request)
        {
            if (request?.Arguments == null)
                return false;

            var receiveMessage = request.Arguments.Length == 1;
            if (receiveMessage)
                return interleavePredicate(UnwrapImmutable(request.Arguments[0]));

            var streamMessage = request.Arguments.Length == 5;
            return streamMessage && interleavePredicate(UnwrapImmutable(request.Arguments[2]));
        }

        static object UnwrapImmutable(object item) => 
            item is Immutable<object> ? ((Immutable<object>)item).Value : item;

        internal void KeepAlive(Grain grain)
        {
            if (keepAliveTimeout == TimeSpan.Zero)
                return;

            grain.Runtime().DelayDeactivation(grain, keepAliveTimeout);
        }

        internal IEnumerable<StreamSubscriptionSpecification> Subscriptions() => 
            StreamSubscriptionSpecification.From(Class, dispatcher);

        internal bool Sticky { get; }
    }
}