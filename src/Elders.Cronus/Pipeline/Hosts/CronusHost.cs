﻿using System;
using System.Linq;
using System.Collections.Generic;
using Elders.Cronus.DomainModeling;
using Elders.Cronus.EventSourcing;
using Elders.Cronus.Serializer;
using Elders.Cronus.Pipeline.Hosts.DisposableExtensions;

namespace Elders.Cronus.Pipeline.Hosts
{
    public class CronusHost : ICronusPlayer, ICronusHost
    {
        static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(CronusHost));

        public CronusHost()
        {
            EventStores = new Dictionary<string, IEventStore>();
            Consumers = new List<IEndpointConsumer>();
        }


        public Dictionary<string, IEventStore> EventStores { get; set; }

        public List<IEndpointConsumer> Consumers { get; set; }

        public ISerializer Serializer { get; set; }

        public IPublisher<ICommand> CommandPublisher { get; set; }

        public IPublisher<IEvent> EventPublisher { get; set; }

        public ICronusPlayer Player { get; set; }

        public bool Start()
        {
            foreach (var consumer in Consumers)
            {
                consumer.Start();
            }
            log.Info("Cronus hosts started succesfully.");
            return true;
        }
        public bool Replay()
        {
            log.Info("Start replaying events...");
            var publisher = EventPublisher;
            int totalMessagesPublished = 0;
            //TODO: when we start making hosts per BC  configuration.EventStores will no be collection
            foreach (var evnt in EventStores.Single().Value.Player.GetEventsFromStart())
            {
                totalMessagesPublished++;
                EventPublisher.Publish(evnt);
            }

            log.Info("Replay finished.");
            return true;
        }
        public bool Stop()
        {
            foreach (var consumer in Consumers)
            {
                consumer.Stop();
            }
            log.Info("Cronus hosts stopped succesfully.");
            return true;
        }

        public void Dispose()
        {
            EventStores.TryDisposeCollection(x => x.Value);
            Consumers.TryDisposeCollection(x => x);
            Serializer.TryDispose();
            CommandPublisher.TryDispose();
            EventPublisher.TryDispose();
        }
    }
}
namespace Elders.Cronus.Pipeline.Hosts.DisposableExtensions
{
    public static class IDisposableExtensions
    {
        public static void TryDispose<T>(this T @self)
        {
            if (@self != null)
            {
                var asDisposable = @self as IDisposable;
                if (asDisposable != null)
                    asDisposable.Dispose();
            }
        }
        public static void TryDisposeCollection<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (source != null)
            {
                foreach (var es in source)
                {
                    var selected = keySelector(es);
                    selected.TryDispose();
                }
            }
        }
    }
}