using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;

namespace QuickTechSystems.Application.Events
{
    public interface IEventAggregator
    {
        void Publish<TEvent>(TEvent eventToPublish);
        void Subscribe<TEvent>(Action<TEvent> action);
        void Unsubscribe<TEvent>(Action<TEvent> action);
    }

    public class EventAggregator : IEventAggregator
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<object>> _subscribers = new();
        private readonly ReaderWriterLockSlim _lock = new();

        public void Publish<TEvent>(TEvent eventToPublish)
        {
            var eventTypeKey = GenerateEventKey<TEvent>();
            Debug.WriteLine($"Publishing event: {eventTypeKey}");

            _lock.EnterReadLock();
            try
            {
                if (!_subscribers.TryGetValue(eventTypeKey, out var subscriberBag))
                {
                    Debug.WriteLine($"No subscribers found for {eventTypeKey}");
                    return;
                }

                var actions = subscriberBag.OfType<Action<TEvent>>().ToArray();
                Debug.WriteLine($"Found {actions.Length} subscribers for {eventTypeKey}");

                foreach (var action in actions)
                {
                    try
                    {
                        action.Invoke(eventToPublish);
                        Debug.WriteLine("Subscriber invoked successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in subscriber: {ex}");
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Subscribe<TEvent>(Action<TEvent> action)
        {
            var eventTypeKey = GenerateEventKey<TEvent>();
            Debug.WriteLine($"Subscribing to {eventTypeKey}");

            _lock.EnterWriteLock();
            try
            {
                var subscriberBag = _subscribers.GetOrAdd(eventTypeKey, _ => new ConcurrentBag<object>());
                subscriberBag.Add(action);

                Debug.WriteLine($"Successfully subscribed. Total subscribers for {eventTypeKey}: {subscriberBag.Count}");
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Unsubscribe<TEvent>(Action<TEvent> action)
        {
            var eventTypeKey = GenerateEventKey<TEvent>();

            _lock.EnterWriteLock();
            try
            {
                if (_subscribers.TryGetValue(eventTypeKey, out var subscriberBag))
                {
                    var remainingActions = subscriberBag.Where(s => !ReferenceEquals(s, action)).ToArray();
                    _subscribers[eventTypeKey] = new ConcurrentBag<object>(remainingActions);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private static string GenerateEventKey<TEvent>()
        {
            var eventType = typeof(TEvent);

            if (eventType.IsGenericType)
            {
                var genericTypeDef = eventType.GetGenericTypeDefinition();
                var genericArgs = eventType.GetGenericArguments();
                var argNames = string.Join(",", genericArgs.Select(t => t.FullName));
                return $"{genericTypeDef.FullName}[{argNames}]";
            }

            return eventType.FullName ?? eventType.Name;
        }
    }
}