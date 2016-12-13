﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

using Orleans;

namespace Orleankka.Behaviors
{
    using Utility;

    public sealed class ActorBehavior
    {
        static readonly Dictionary<Type, Dictionary<string, Action<object>>> behaviors = 
                    new Dictionary<Type, Dictionary<string, Action<object>>>();

        public static void Reset() => behaviors.Clear();

        public static void Register(Type actor)
        {
            var found = new Dictionary<string, Action<object>>();

            var type = actor;
            while (type != typeof(Actor))
            {
                const BindingFlags scope = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var method in actor.GetMethods(scope))
                {
                    if (method.ReturnType != typeof(void) ||
                        method.GetGenericArguments().Length > 0 ||
                        method.GetParameters().Length > 0)
                        continue;

                    var target = Expression.Parameter(typeof(object));
                    var call = Expression.Call(Expression.Convert(target, actor), method);
                    var action = Expression.Lambda<Action<object>>(call, target).Compile();

                    found.Add(method.Name, action);
                }

                Debug.Assert(type != null, "type != null");
                type = type.BaseType;
            }

            behaviors.Add(actor, found);
        }

        Action<object> RegisteredAction(string behavior)
        {
            var action = behaviors[actor.GetType()].Find(behavior);

            if (action == null)
                throw new InvalidOperationException(
                    $"Can't find method with proper signature for behavior '{behavior}' defined on actor {actor.GetType()}. " +
                    "Should be void, non-generic and parameterless");

            return action;
        }

        void AssertHasRegisteredAction(Action behavior)
        {
            var action = behaviors[actor.GetType()].Find(behavior.Method.Name);

            if (action == null)
                throw new InvalidOperationException(
                    $"Can't find method with proper signature for behavior '{behavior}' defined on actor {actor.GetType()}. " +
                    "Should be void, non-generic and parameterless");
        }

        public static ActorBehavior Null(Actor actor) => new ActorBehavior(actor)
        {
            current = CustomBehavior.Null
        };

        static readonly Func<Type, object, string, Task<object>> OnUnhandledReceiveDefaultCallback = 
            (actor, message, state) => { throw new UnhandledMessageException(actor, state, message); };

        static readonly Func<Type, string, string, Task> OnUnhandledReminderDefaultCallback = 
            (actor, reminder, state) => { throw new UnhandledReminderException(actor, state, reminder); };

        readonly Actor actor;
        Func<string, string, Task> onBecome;
        Func<Type, object, string, Task<object>> onUnhandledReceive;
        Func<Type, string, string, Task> onUnhandledReminder;

        CustomBehavior current;
        CustomBehavior next;

        ActorBehavior(Actor actor)
        {
            this.actor = actor;
        }

        internal Task HandleActivate() => current.HandleActivate(default(Transition));
        internal Task HandleDeactivate() => current.HandleDeactivate(default(Transition));

        internal Task<object> HandleReceive(object message) => 
            current.HandleReceive(actor, message, onUnhandledReceive ?? OnUnhandledReceiveDefaultCallback);

        internal Task HandleReminder(string id) => 
            current.HandleReminder(actor, id, onUnhandledReminder ?? OnUnhandledReminderDefaultCallback);

        CustomBehavior Next
        {
            get
            {
                if (next == null)
                    throw new InvalidOperationException("Behavior can only be configured from within Become");

                return next;
            }
        }

        public void Initial(Action behavior) => Initial(behavior.Method.Name);

        public void Initial(string behavior)
        {
            if (!IsNull())
                throw new InvalidOperationException("Initial behavior has been already set");

            var action = RegisteredAction(behavior);
            next = new CustomBehavior(behavior);
            action(actor);

            current = next;
            next = null;
        }

        bool IsNull() => ReferenceEquals(current, CustomBehavior.Null);

        public string Current => current.Name;

        public async Task Become(Action behavior)
        {
            AssertHasRegisteredAction(behavior);

            if (IsNull())
                throw new InvalidOperationException("Initial behavior should be set before calling Become");

            if (next != null)
                throw new InvalidOperationException("Become cannot be called while configuring behavior");

            if (Current == behavior.Method.Name)
                throw new InvalidOperationException($"Actor is already behaving as '{behavior.Method.Name}'");

            next = new CustomBehavior(behavior.Method.Name);
            behavior();

            var transition = new Transition(current, next);

            await current.HandleDeactivate(transition);
            await current.HandleUnbecome(transition);
            
            current = next;

            await current.HandleBecome(transition);
            if (onBecome != null)
                await onBecome(transition.From.Name, transition.To.Name);

            await current.HandleActivate(transition);

            next = null;
        }

        public void Super(Action behavior)
        {
            AssertHasRegisteredAction(behavior);

            if (next == null)
                throw new InvalidOperationException("Super can only be called while configuring behavior");

            if (next.Includes(behavior.Method.Name))
                throw new InvalidOperationException(
                    "Detected cyclic declaration of super behaviors. " +
                    $"'{behavior.Method.Name}' is already within super chain of {next.Name}");

            var existent = current.FindSuper(behavior.Method.Name);
            if (existent != null)
            {
                next.Super(existent);
                return;
            }

            var prev = next;
            next = new CustomBehavior(behavior.Method.Name);

            prev.Super(next);
            behavior();
            
            next = prev;
        }

        public void OnBecome(Action<string, string> onBecomeCallback)
        {
            Requires.NotNull(onBecomeCallback, nameof(onBecomeCallback));
            OnBecome((current, previous) =>
            {
                onBecomeCallback(current, previous);
                return TaskResult.Done;
            });
        }

        public void OnBecome(Func<string, string, Task> onBecomeCallback)
        {
            Requires.NotNull(onBecomeCallback, nameof(onBecomeCallback));
            onBecome = onBecomeCallback;
        }

        public void OnUnhandledReceive(Action<object, string> unhandledReceiveCallback)
        {
            Requires.NotNull(unhandledReceiveCallback, nameof(unhandledReceiveCallback));
            OnUnhandledReceive((message, state) =>
            {
                unhandledReceiveCallback(message, state);
                return TaskResult.Done;
            });
        }

        public void OnUnhandledReceive(Func<object, string, Task> unhandledReceiveCallback)
        {
            Requires.NotNull(unhandledReceiveCallback, nameof(unhandledReceiveCallback));

            OnUnhandledReceive(async (message, state) =>
            {
                await unhandledReceiveCallback(message, state);
                return null;
            });
        }

        public void OnUnhandledReceive(Func<object, string, object> unhandledReceiveCallback)
        {
            Requires.NotNull(unhandledReceiveCallback, nameof(unhandledReceiveCallback));
            OnUnhandledReceive((message, state) => Task.FromResult(unhandledReceiveCallback(message, state)));
        }

        public void OnUnhandledReceive(Func<object, string, Task<object>> unhandledReceiveCallback)
        {
            Requires.NotNull(unhandledReceiveCallback, nameof(unhandledReceiveCallback));

            if (onUnhandledReceive != null)
                throw new InvalidOperationException("Unhandled message callback has been already set");

            if (next != null)
                throw new InvalidOperationException("Unhandled message callback cannot be set while configuring behavior");

            onUnhandledReceive = (actor, message, state) => unhandledReceiveCallback(message, state);
        }

        public void OnUnhandledReminder(Action<string, string> unhandledReminderCallback)
        {
            Requires.NotNull(unhandledReminderCallback, nameof(unhandledReminderCallback));

            OnUnhandledReminder((reminder, state) =>
            {
                unhandledReminderCallback(reminder, state);
                return TaskResult.Done;
            });
        }

        public void OnUnhandledReminder(Func<string, string, Task> unhandledReminderCallback)
        {
            Requires.NotNull(unhandledReminderCallback, nameof(unhandledReminderCallback));

            if (onUnhandledReminder != null)
                throw new InvalidOperationException("Unhandled reminder callback has been already set");

            if (next != null)
                throw new InvalidOperationException("Unhandled reminder callback cannot be set while configuring behavior");

            onUnhandledReminder = (actor, reminder, state) => unhandledReminderCallback(reminder, state);
        }

        public void OnBecome(Action action)
        {
            Requires.NotNull(action, nameof(action));
            OnBecome(() =>
            {
                action();
                return TaskDone.Done;
            });
        }

        public void OnBecome(Func<Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnBecome(action);
        }

        public void OnUnbecome(Action action)
        {
            Requires.NotNull(action, nameof(action));
            OnUnbecome(() =>
            {
                action();
                return TaskDone.Done;
            });
        }

        public void OnUnbecome(Func<Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnUnbecome(action);
        }

        public void OnReceive<TMessage>(Action<TMessage> action)
        {
            Requires.NotNull(action, nameof(action));
            OnReceive<TMessage>(x =>
            {
                action(x);
                return TaskDone.Done;
            });
        }

        public void OnReceive<TMessage, TResult>(Func<TMessage, TResult> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReceive<TMessage>((a, m) => Task.FromResult((object)action(m)));
        }

        public void OnReceive<TMessage>(Func<TMessage, Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReceive<TMessage>(async (a, m) =>
            {
                await action(m);
                return null;
            });
        }

        public void OnReceive<TMessage>(Func<TMessage, Task<object>> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReceive<TMessage>((a, m) => action(m));
        }

        public void OnReceive<TMessage, TResult>(Func<TMessage, Task<TResult>> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReceive<TMessage>(async (a, m) => await action(m));
        }

        public void OnReceive(Action<object> action)
        {
            Requires.NotNull(action, nameof(action));
            OnReceive(x =>
            {
                action(x);
                return TaskDone.Done;
            });
        }

        public void OnReceive(Func<object, Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReceive(async (a, m) =>
            {
                await action(m);
                return null;
            });
        }

        public void OnReceive(Func<object, Task<object>> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReceive((a, m) => action(m));
        }

        public void OnActivate(Action action)
        {
            Requires.NotNull(action, nameof(action));
            OnActivate(() =>
            {
                action();
                return TaskDone.Done;
            });
        }

        public void OnActivate(Func<Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnActivate(action);
        }

        public void OnDeactivate(Action action)
        {
            Requires.NotNull(action, nameof(action));
            OnDeactivate(() =>
            {
                action();
                return TaskDone.Done;
            });
        }

        public void OnDeactivate(Func<Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnDeactivate(action);
        }

        public void OnReminder(string id, Action action)
        {
            Requires.NotNullOrWhitespace(id, nameof(id));
            Requires.NotNull(action, nameof(action));
            OnReminder(id, ()=>
            {
                action();
                return TaskDone.Done;
            });
        }

        public void OnReminder(string id, Func<Task> action)
        {
            Requires.NotNullOrWhitespace(id, nameof(id));
            Requires.NotNull(action, nameof(action));
            Next.OnReminder(id, a => action());
        }

        public void OnReminder(Action<string> action)
        {
            Requires.NotNull(action, nameof(action));
            OnReminder(x =>
            {
                action(x);
                return TaskDone.Done;
            });
        }

        public void OnReminder(Func<string, Task> action)
        {
            Requires.NotNull(action, nameof(action));
            Next.OnReminder((a, id) => action(id));
        }
    }
}